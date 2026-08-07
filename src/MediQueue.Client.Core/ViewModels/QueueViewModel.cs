using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediQueue.Client.Core.Api;
using MediQueue.Client.Core.Realtime;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.ViewModels;

/// <summary>The signed-in doctor's waiting list, kept current by push.</summary>
public sealed partial class QueueViewModel : ObservableObject
{
    private readonly MediQueueApiClient _api;
    private readonly IAuthSession _session;
    private readonly IQueueConnection _realtime;
    private readonly TimeProvider _timeProvider;

    /// <summary>Subscribes to the push channel. Nothing is fetched until Refresh.</summary>
    /// <param name="api">The HTTP client.</param>
    /// <param name="session">Who is signed in.</param>
    /// <param name="realtime">The push channel.</param>
    /// <param name="timeProvider">Supplies the zone times are displayed in.</param>
    public QueueViewModel(
        MediQueueApiClient api,
        IAuthSession session,
        IQueueConnection realtime,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(realtime);

        _api = api;
        _session = session;
        _realtime = realtime;
        _timeProvider = timeProvider;

        // Fire-and-forget handlers, deliberately: an event handler cannot be
        // awaited, and each of these takes the same lock the refresh does, so
        // the ordering is settled by the lock rather than by who was first.
        realtime.VisitQueued += (_, visit) => _ = ApplyAsync(() => Upsert(visit));
        realtime.VisitCalledIn += (_, visit) => _ = ApplyAsync(() => Upsert(visit));
        realtime.VisitReleased += (_, visit) => _ = ApplyAsync(() => Remove(visit.Id));
        realtime.VisitDeleted += (_, payload) => _ = ApplyAsync(() => Remove(payload.VisitId));
        realtime.StatusChanged += OnStatusChanged;
    }

    /// <summary>Opens the push channel, then loads the queue.</summary>
    /// <remarks>
    /// In this order on purpose. Connecting first means anything happening
    /// during the fetch is delivered rather than missed; the refresh's lock then
    /// settles which of the two writes the rows. The other order has a gap in
    /// it — between the fetch returning and the subscription starting — that
    /// nothing would ever close.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    [RelayCommand]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _realtime.StartAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // The list still loads over HTTP, so a hub that will not open leaves
            // a working screen with a stale-looking status rather than no screen.
            // Reported through ConnectionStatus, which the connection has already
            // set to Disconnected.
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Tracks the connection, and resynchronises after a gap.
    /// </summary>
    /// <remarks>
    /// Automatic reconnect restores the socket; it does not replay what was sent
    /// while the client was away. Coming back Live therefore means "you have
    /// missed an unknown amount", and the only honest response is to fetch the
    /// queue again rather than to carry on from rows that may already be wrong.
    /// </remarks>
    private void OnStatusChanged(object? sender, RealtimeStatus status)
    {
        var wasAway = ConnectionStatus == RealtimeStatus.Reconnecting;

        ConnectionStatus = status;
        OnPropertyChanged(nameof(IsLive));

        if (wasAway && status == RealtimeStatus.Live)
        {
            RefreshCommand.Execute(null);
        }
    }

    /// <summary>How the push channel is doing, for the status line.</summary>
    [ObservableProperty]
    public partial RealtimeStatus ConnectionStatus { get; set; }

    /// <summary>Whether the list is being kept current by the server.</summary>
    public bool IsLive => ConnectionStatus == RealtimeStatus.Live;

    /// <summary>Shown when a visit has somehow reached the list without a queue time.</summary>
    public const string NoTimePlaceholder = "—";

    /// <summary>
    /// Guards the method rather than only the command.
    /// </summary>
    /// <remarks>
    /// <c>AllowConcurrentExecutions = false</c> protects the command, so a user
    /// double-clicking Refresh is safe. It does nothing for a caller that
    /// invokes the method directly — which the shell does when a doctor signs
    /// in. Two overlapping refreshes each clear the rows and then each add
    /// their own, and the list ends up with every patient twice.
    /// </remarks>
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>The rows currently on screen.</summary>
    public ObservableCollection<QueueRow> Rows { get; } = [];

    /// <summary>The signed-in doctor's name, for the window title.</summary>
    public string DoctorName => _session.CurrentUser?.FullName ?? string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Whether to show the empty-state message rather than a blank panel.</summary>
    public bool IsEmpty => !IsBusy && Rows.Count == 0 && ErrorMessage is null;

    /// <summary>Fetches the queue and replaces the rows.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(true))
        {
            // A refresh is already running and will produce the same answer.
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var queue = await _api.GetMyQueueAsync(cancellationToken).ConfigureAwait(true);

            // Fetched first, then swapped in one go: the rows are never half of
            // an old queue and half of a new one.
            Rows.Clear();

            foreach (var visit in queue)
            {
                Rows.Add(ToRow(visit));
            }
        }
        catch (ApiException exception)
        {
            // The server's own sentence, which is written for a person. The
            // trace id goes with it so a complaint can be found in the log.
            ErrorMessage = exception.TraceId is null
                ? exception.Detail
                : $"{exception.Detail} (reference {exception.TraceId})";
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "The server is not reachable. Check that it is running.";
        }
        finally
        {
            // In finally, so a failure cannot leave the UI stuck behind a
            // spinner with no way back.
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Applies one pushed change under the same lock a refresh holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It waits for the lock rather than skipping, which is the
    /// opposite of what a refresh does, and the difference matters.</strong> A
    /// second refresh can be dropped because the one already running will
    /// produce the same answer. A pushed change cannot: it is the only notice
    /// this client will ever get of that event, and dropping it leaves a row on
    /// screen that is not in the database.
    /// </para>
    /// <para>
    /// The refresh holds the lock across its HTTP call as well as its row swap,
    /// so a push can never land between the fetch and the swap and be silently
    /// overwritten. It applies strictly before or strictly after a whole
    /// refresh — and if it applies after, it is the newer fact, which is the
    /// right way round. This is the interleaving that produced duplicate rows
    /// in P4b, met again from the other side.
    /// </para>
    /// </remarks>
    private async Task ApplyAsync(Action change)
    {
        await _refreshLock.WaitAsync().ConfigureAwait(true);

        try
        {
            change();
        }
        finally
        {
            _refreshLock.Release();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Adds a visit to the list, or updates it where it already is.
    /// </summary>
    /// <remarks>
    /// The doctor is only ever in their own group, so everything arriving is
    /// theirs — but a row belongs in <em>this</em> list only if it is in
    /// <em>this</em> doctor's queue, and that is the view model's own question
    /// rather than a second opinion on the server's authorization.
    /// </remarks>
    private void Upsert(VisitSummaryDto visit)
    {
        if (_session.CurrentUser is { } user && visit.DoctorId != user.Id)
        {
            return;
        }

        var existing = Rows.FirstOrDefault(row => row.VisitId == visit.Id);
        var replacement = ToRow(visit);

        if (existing is not null)
        {
            // Replaced in place: the row keeps its position, so a status change
            // does not make a patient jump around the screen.
            Rows[Rows.IndexOf(existing)] = replacement;

            return;
        }

        Rows.Insert(PositionFor(replacement), replacement);
    }

    private void Remove(Guid visitId)
    {
        if (Rows.FirstOrDefault(row => row.VisitId == visitId) is { } row)
        {
            Rows.Remove(row);
        }
    }

    /// <summary>Where a new row goes, so the list stays in arrival order.</summary>
    /// <remarks>
    /// Inserted at the right index rather than appended and re-sorted: a
    /// re-sort replaces every row, and an observable collection reports that as
    /// the whole list changing, which the view redraws.
    /// </remarks>
    private int PositionFor(QueueRow row)
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            if (Rows[index].QueuedAt > row.QueuedAt)
            {
                return index;
            }
        }

        return Rows.Count;
    }

    private QueueRow ToRow(VisitSummaryDto visit) => new(
        visit.Id,
        visit.QueuedAt,
        visit.PatientFullName,
        visit.Taj,
        visit.Complaint,
        FormatLocal(visit.QueuedAt),
        visit.Status.ToString());

    /// <summary>
    /// Renders a UTC instant in the configured local zone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Through <see cref="TimeProvider.LocalTimeZone"/> rather than
    /// <c>ToLocalTime()</c> or <c>DateTime.Now</c>, and that is the whole point
    /// of putting the formatting here. The wire value stays UTC all the way in;
    /// this is the single place a time zone is applied, and because the provider
    /// is injected the rule can be tested against a fixed zone instead of
    /// against whatever machine happens to run the test.
    /// </para>
    /// </remarks>
    private string FormatLocal(DateTimeOffset? instant) =>
        instant is { } value
            ? TimeZoneInfo.ConvertTime(value, _timeProvider.LocalTimeZone)
                .ToString("HH:mm", CultureInfo.InvariantCulture)
            : NoTimePlaceholder;
}
