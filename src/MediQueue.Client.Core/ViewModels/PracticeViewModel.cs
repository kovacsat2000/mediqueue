using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediQueue.Client.Core.Api;
using MediQueue.Client.Core.Realtime;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.ViewModels;

/// <summary>
/// What an assistant watches: everyone who has arrived and nobody has routed
/// yet, and every doctor's waiting list.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A visit is in exactly one of these lists, always.</strong> That is
/// the whole difficulty of this screen compared with the doctor's, which has
/// one list and only ever adds to or removes from it. Here a
/// <c>VisitQueued</c> means "leave the unrouted list and join a doctor's", and
/// the two halves happen under one lock so there is no instant at which the
/// patient is in both or in neither.
/// </para>
/// <para>
/// Every list here is <see cref="VisitSummaryDto"/>. There is no member on
/// <see cref="IAssistantApi"/> that could return anything else, so the type
/// that cannot carry a diagnosis is the only type this screen has ever seen.
/// </para>
/// </remarks>
public sealed partial class PracticeViewModel : ObservableObject
{
    private readonly IAssistantApi _api;
    private readonly IQueueConnection _realtime;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// One lock over both lists, for the reason P4b established.
    /// </summary>
    /// <remarks>
    /// A refresh holds it across its two HTTP calls and the swap that follows,
    /// so a push cannot land between fetching the queues and displaying them.
    /// A push waits for it rather than skipping, because a push is the only
    /// notice this client will ever get of that event (D-65).
    /// </remarks>
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>Builds the screen and subscribes to the push channel.</summary>
    /// <param name="api">The assistant's half of the API.</param>
    /// <param name="realtime">The push channel.</param>
    /// <param name="timeProvider">Supplies the zone times are displayed in.</param>
    public PracticeViewModel(IAssistantApi api, IQueueConnection realtime, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(realtime);

        _api = api;
        _realtime = realtime;
        _timeProvider = timeProvider;

        realtime.VisitRegistered += (_, visit) => _ = ApplyAsync(() => Arrive(visit));
        realtime.VisitQueued += (_, visit) => _ = ApplyAsync(() => Route(visit));
        realtime.VisitCalledIn += (_, visit) => _ = ApplyAsync(() => Route(visit));
        realtime.VisitReleased += (_, visit) => _ = ApplyAsync(() => Remove(visit.Id));
        realtime.VisitDeleted += (_, payload) => _ = ApplyAsync(() => Remove(payload.VisitId));
        realtime.StatusChanged += OnStatusChanged;
    }

    /// <summary>Arrivals nobody has routed yet, oldest first.</summary>
    public ObservableCollection<QueueRow> Unrouted { get; } = [];

    /// <summary>One panel per active doctor, including those with nobody waiting.</summary>
    public ObservableCollection<DoctorQueueViewModel> Queues { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>The server's refusal of the last action, if it refused one.</summary>
    [ObservableProperty]
    public partial string? ActionError { get; set; }

    /// <summary>How the push channel is doing.</summary>
    [ObservableProperty]
    public partial RealtimeStatus ConnectionStatus { get; set; }

    /// <summary>Whether the lists are being kept current by the server.</summary>
    public bool IsLive => ConnectionStatus == RealtimeStatus.Live;

    /// <summary>Whether to show the empty-state line rather than a blank panel.</summary>
    public bool NobodyIsUnrouted => Unrouted.Count == 0;

    /// <summary>Opens the push channel, then loads both lists.</summary>
    /// <remarks>
    /// Connect first, as in the doctor client: anything happening during the
    /// fetch is then delivered rather than missed.
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
            // The lists still load over HTTP; the status line reports the rest.
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Fetches both lists and replaces what is on screen.</summary>
    /// <param name="cancellationToken">Cancels the requests.</param>
    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            // Both fetched before either is displayed. Swapping one at a time
            // would show a visit in the unrouted list and in a queue at once,
            // or in neither, for as long as the second request took.
            var unrouted = await _api.GetUnassignedAsync(cancellationToken).ConfigureAwait(true);
            var queues = await _api.GetAllQueuesAsync(cancellationToken).ConfigureAwait(true);

            Unrouted.Clear();

            foreach (var visit in unrouted)
            {
                Unrouted.Add(ToRow(visit));
            }

            Queues.Clear();

            foreach (var queue in queues)
            {
                Queues.Add(new DoctorQueueViewModel(
                    queue.DoctorId,
                    queue.DoctorFullName,
                    queue.SpecialtyName,
                    [.. queue.Visits.Select(ToRow)]));
            }
        }
        catch (ApiException exception)
        {
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
            IsBusy = false;
            OnPropertyChanged(nameof(NobodyIsUnrouted));
            _refreshLock.Release();
        }
    }

    /// <summary>Routes an unrouted visit to a specialty. The server picks the doctor.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="specialtyId">The specialty.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task AssignAsync(Guid visitId, Guid specialtyId, CancellationToken cancellationToken)
    {
        ActionError = null;

        try
        {
            // Nothing is moved here. The push that follows moves it, so there is
            // one code path that changes the lists whether the change came from
            // this client or another one.
            await _api.AssignSpecialtyAsync(visitId, specialtyId, cancellationToken).ConfigureAwait(true);
        }
        catch (ApiException exception)
        {
            // "No doctor is currently available in Reumatológia" is the
            // sentence that matters here, and it is the server's.
            ActionError = exception.Detail;
        }
        catch (HttpRequestException)
        {
            ActionError = "The server is not reachable. Check that it is running.";
        }
    }

    /// <summary>Withdraws a visit.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task DeleteAsync(Guid visitId, CancellationToken cancellationToken)
    {
        ActionError = null;

        try
        {
            await _api.DeleteVisitAsync(visitId, cancellationToken).ConfigureAwait(true);
        }
        catch (ApiException exception)
        {
            ActionError = exception.Detail;
        }
        catch (HttpRequestException)
        {
            ActionError = "The server is not reachable. Check that it is running.";
        }
    }

    /// <summary>Puts a freshly registered, unrouted visit into the unrouted list.</summary>
    private void Arrive(VisitSummaryDto visit)
    {
        RemoveEverywhere(visit.Id);
        Unrouted.Insert(PositionFor(Unrouted, visit.QueuedAt ?? visit.RegisteredAt), ToRow(visit));
    }

    /// <summary>
    /// Moves a visit into the queue it now belongs to, leaving wherever it was.
    /// </summary>
    /// <remarks>
    /// Removal first, insertion second, both inside one call, and the whole
    /// call under the lock. The order is what stops the patient existing twice;
    /// the lock is what stops anybody observing the instant between the two.
    /// </remarks>
    private void Route(VisitSummaryDto visit)
    {
        RemoveEverywhere(visit.Id);

        if (visit.DoctorId is not { } doctorId)
        {
            Unrouted.Insert(PositionFor(Unrouted, visit.RegisteredAt), ToRow(visit));

            return;
        }

        if (Queues.FirstOrDefault(queue => queue.DoctorId == doctorId) is not { } target)
        {
            // A doctor this client has never heard of — the queues were fetched
            // before they became active. The refresh will pick them up; dropping
            // the row is better than inventing a panel with a blank name.
            return;
        }

        target.Rows.Insert(PositionFor(target.Rows, visit.QueuedAt), ToRow(visit));
    }

    private void Remove(Guid visitId)
    {
        RemoveEverywhere(visitId);
        OnPropertyChanged(nameof(NobodyIsUnrouted));
    }

    /// <summary>Takes a visit out of whichever single list is holding it.</summary>
    private void RemoveEverywhere(Guid visitId)
    {
        if (Unrouted.FirstOrDefault(row => row.VisitId == visitId) is { } unrouted)
        {
            Unrouted.Remove(unrouted);
        }

        foreach (var queue in Queues)
        {
            if (queue.Rows.FirstOrDefault(row => row.VisitId == visitId) is { } queued)
            {
                queue.Rows.Remove(queued);
            }
        }
    }

    /// <summary>Where a row goes so the list stays in arrival order.</summary>
    private static int PositionFor(ObservableCollection<QueueRow> rows, DateTimeOffset? arrivedAt)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].QueuedAt > arrivedAt)
            {
                return index;
            }
        }

        return rows.Count;
    }

    /// <summary>Applies one pushed change under the lock a refresh holds.</summary>
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
            OnPropertyChanged(nameof(NobodyIsUnrouted));
        }
    }

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

    private QueueRow ToRow(VisitSummaryDto visit) => new(
        visit.Id,
        visit.QueuedAt ?? visit.RegisteredAt,
        visit.PatientFullName,
        visit.Taj,
        visit.Complaint,
        FormatLocal(visit.QueuedAt ?? visit.RegisteredAt),
        visit.Status);

    /// <summary>
    /// Renders a UTC instant in the configured local zone.
    /// </summary>
    /// <remarks>
    /// Through <see cref="TimeProvider.LocalTimeZone"/>, the same single place
    /// and the same reason as the doctor client: the wire value stays UTC all
    /// the way in, and because the provider is injected the rule is testable
    /// against a fixed zone rather than against whatever machine runs the test.
    /// </remarks>
    private string FormatLocal(DateTimeOffset? instant) =>
        instant is { } value
            ? TimeZoneInfo.ConvertTime(value, _timeProvider.LocalTimeZone)
                .ToString("HH:mm", CultureInfo.InvariantCulture)
            : QueueViewModel.NoTimePlaceholder;
}

/// <summary>One doctor's panel on the assistant's screen.</summary>
public sealed class DoctorQueueViewModel : ObservableObject
{
    /// <summary>Builds a panel for one doctor.</summary>
    /// <param name="doctorId">Whose queue.</param>
    /// <param name="doctorFullName">Their name.</param>
    /// <param name="specialtyName">The specialty they practise.</param>
    /// <param name="rows">Their waiting patients, in arrival order.</param>
    public DoctorQueueViewModel(
        Guid doctorId,
        string doctorFullName,
        string specialtyName,
        IEnumerable<QueueRow> rows)
    {
        DoctorId = doctorId;
        DoctorFullName = doctorFullName;
        SpecialtyName = specialtyName;
        Rows = [.. rows];

        // A computed property over an ObservableCollection is not automatic:
        // the collection reports its own changes and nothing else's, so without
        // this the empty-state line would appear once and never leave.
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Whose queue this is.</summary>
    public Guid DoctorId { get; }

    /// <summary>The name to show.</summary>
    public string DoctorFullName { get; }

    /// <summary>The specialty they practise.</summary>
    public string SpecialtyName { get; }

    /// <summary>The patients waiting for them.</summary>
    public ObservableCollection<QueueRow> Rows { get; }

    /// <summary>
    /// Whether to say so rather than showing a blank rectangle.
    /// </summary>
    /// <remarks>
    /// An empty queue is information — it is how an assistant sees that a
    /// doctor is free — so the panel says it in words rather than leaving a
    /// space that reads as a failure to load.
    /// </remarks>
    public bool IsEmpty => Rows.Count == 0;
}
