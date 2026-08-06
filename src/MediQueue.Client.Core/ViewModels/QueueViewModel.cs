using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediQueue.Client.Core.Api;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.ViewModels;

/// <summary>The signed-in doctor's waiting list.</summary>
public sealed partial class QueueViewModel(
    MediQueueApiClient api,
    IAuthSession session,
    TimeProvider timeProvider) : ObservableObject
{
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
    public string DoctorName => session.CurrentUser?.FullName ?? string.Empty;

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
            var queue = await api.GetMyQueueAsync(cancellationToken).ConfigureAwait(true);

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

    private QueueRow ToRow(VisitSummaryDto visit) => new(
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
            ? TimeZoneInfo.ConvertTime(value, timeProvider.LocalTimeZone)
                .ToString("HH:mm", CultureInfo.InvariantCulture)
            : NoTimePlaceholder;
}
