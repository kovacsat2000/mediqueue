using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediQueue.Client.Core.Api;

namespace MediQueue.Client.Core.ViewModels;

/// <summary>
/// The assistant's whole screen: the registration form on one side, the
/// unrouted arrivals and every doctor's queue on the other.
/// </summary>
/// <remarks>
/// It owns almost nothing itself. The two halves are separate view models
/// because they have separate jobs, and this exists to start them together and
/// to carry the one thing they share — which specialty a row is being routed to.
/// </remarks>
public sealed partial class AssistantViewModel : ObservableObject
{
    /// <summary>Builds the screen from its two halves.</summary>
    /// <param name="registration">The registration form.</param>
    /// <param name="practice">The unrouted list and the queues.</param>
    /// <param name="session">Who is signed in.</param>
    public AssistantViewModel(
        RegistrationViewModel registration,
        PracticeViewModel practice,
        IAuthSession session)
    {
        ArgumentNullException.ThrowIfNull(registration);

        Registration = registration;
        Practice = practice;
        Session = session;

        // The lists are deliberately not touched when a registration succeeds.
        // The push delivers the same event a moment later, so letting only the
        // push move them means one code path whether the change came from this
        // client or from another one — and no reconciliation between the two.
    }

    /// <summary>The registration form.</summary>
    public RegistrationViewModel Registration { get; }

    /// <summary>The unrouted list and the doctors' queues.</summary>
    public PracticeViewModel Practice { get; }

    /// <summary>Who is signed in, for the window heading.</summary>
    public IAuthSession Session { get; }

    /// <summary>The signed-in assistant's name.</summary>
    public string AssistantName => Session.CurrentUser?.FullName ?? string.Empty;

    /// <summary>The row the assistant is about to route or withdraw.</summary>
    [ObservableProperty]
    public partial QueueRow? SelectedUnrouted { get; set; }

    /// <summary>Which specialty the selected row is being routed to.</summary>
    [ObservableProperty]
    public partial SpecialtyChoice? RouteTo { get; set; }

    /// <summary>Whether the selected row can be routed anywhere yet.</summary>
    public bool CanAssign => SelectedUnrouted is not null && RouteTo?.Id is not null;

    /// <summary>Whether there is a row to withdraw.</summary>
    public bool CanDelete => SelectedUnrouted is not null;

    /// <summary>Loads the specialties, opens the push channel and fetches the lists.</summary>
    /// <param name="cancellationToken">Cancels the requests.</param>
    [RelayCommand]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Registration.LoadSpecialtiesAsync(cancellationToken).ConfigureAwait(true);
        await Practice.StartAsync(cancellationToken).ConfigureAwait(true);

        OnPropertyChanged(nameof(AssistantName));
    }

    /// <summary>Routes the selected arrival to the chosen specialty.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    [RelayCommand]
    public async Task AssignAsync(CancellationToken cancellationToken)
    {
        if (SelectedUnrouted is not { } row || RouteTo?.Id is not { } specialtyId)
        {
            return;
        }

        await Practice.AssignAsync(row.VisitId, specialtyId, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Withdraws the selected arrival.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedUnrouted is not { } row)
        {
            return;
        }

        await Practice.DeleteAsync(row.VisitId, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>The specialties offered, so the routing picker and the form share one list.</summary>
    public IReadOnlyList<SpecialtyChoice> Specialties => Registration.Specialties;

    partial void OnSelectedUnroutedChanged(QueueRow? value) => RaiseAvailability();

    partial void OnRouteToChanged(SpecialtyChoice? value) => RaiseAvailability();

    private void RaiseAvailability()
    {
        OnPropertyChanged(nameof(CanAssign));
        OnPropertyChanged(nameof(CanDelete));
    }
}
