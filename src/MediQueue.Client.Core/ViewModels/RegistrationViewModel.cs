using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediQueue.Client.Core.Api;
using MediQueue.Contracts.Directory;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.ViewModels;

/// <summary>
/// The registration form: a patient's details, their complaint, and optionally
/// the specialty to route them to.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No validation rule is re-implemented here.</strong> The form submits
/// what was typed and renders the server's per-field messages against the
/// inputs that caused them. The TAJ format, the name's character rules and the
/// length bounds all live in <c>Domain</c> (D-31), and a second copy in the
/// client is a second definition that can disagree with the first.
/// </para>
/// <para>
/// The cost is one round trip for a mistyped TAJ. That is the honest price of
/// "all logic on the server", and the standing list already names the fix —
/// serving the rules from the server so there is still only one definition.
/// </para>
/// </remarks>
public sealed partial class RegistrationViewModel(IAssistantApi api) : ObservableObject
{
    /// <summary>Raised when a visit has been registered, so the lists can react.</summary>
    public event EventHandler<VisitSummaryDto>? Registered;

    /// <summary>The specialties offered, plus a "decide later" entry.</summary>
    public ObservableCollection<SpecialtyChoice> Specialties { get; } = [];

    [ObservableProperty]
    public partial string FullName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Address { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Taj { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Complaint { get; set; } = string.Empty;

    /// <summary>The chosen specialty, or the "decide later" entry.</summary>
    [ObservableProperty]
    public partial SpecialtyChoice? SelectedSpecialty { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The server's refusal, when it was not about one field.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Shown next to the name input.</summary>
    [ObservableProperty]
    public partial string? FullNameError { get; set; }

    /// <summary>Shown next to the address input.</summary>
    [ObservableProperty]
    public partial string? AddressError { get; set; }

    /// <summary>Shown next to the TAJ input.</summary>
    [ObservableProperty]
    public partial string? TajError { get; set; }

    /// <summary>Shown next to the complaint input.</summary>
    [ObservableProperty]
    public partial string? ComplaintError { get; set; }

    /// <summary>
    /// Whether the form has enough in it to be worth sending.
    /// </summary>
    /// <remarks>
    /// Presence only. Disabling a button while a required box is empty is
    /// interface behaviour and costs nothing; deciding whether a TAJ is
    /// well-formed is the domain's, and this deliberately does not try.
    /// </remarks>
    public bool CanSubmit =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(FullName)
        && !string.IsNullOrWhiteSpace(Address)
        && !string.IsNullOrWhiteSpace(Taj)
        && !string.IsNullOrWhiteSpace(Complaint);

    /// <summary>Loads the specialties for the picker.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task LoadSpecialtiesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var specialties = await api.GetSpecialtiesAsync(cancellationToken).ConfigureAwait(true);

            Specialties.Clear();

            // First, and the default: a patient can arrive before anyone knows
            // where they should go, and that is a state the system has (D-51).
            Specialties.Add(SpecialtyChoice.DecideLater);

            foreach (var specialty in specialties)
            {
                Specialties.Add(new SpecialtyChoice(specialty.Id, specialty.Name));
            }

            SelectedSpecialty = Specialties[0];
        }
        catch (ApiException exception)
        {
            ErrorMessage = exception.Detail;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "The server is not reachable. Check that it is running.";
        }
    }

    /// <summary>Submits the form.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task SubmitAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ClearErrors();
        OnPropertyChanged(nameof(CanSubmit));

        try
        {
            var visit = await api.RegisterVisitAsync(
                new RegisterVisitRequest(FullName, Address, Taj, Complaint, SelectedSpecialty?.Id),
                cancellationToken).ConfigureAwait(true);

            Clear();
            Registered?.Invoke(this, visit);
        }
        catch (ApiException exception)
        {
            Show(exception);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "The server is not reachable. Check that it is running.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanSubmit));
        }
    }

    /// <summary>
    /// Puts the server's messages where the person can see which box caused
    /// them.
    /// </summary>
    /// <remarks>
    /// The field names are the domain's own — <c>PatientName</c> and
    /// <c>TajNumber</c> are the value objects that refused the input, and
    /// <c>Address</c> and <c>Complaint</c> are the properties. A message that
    /// matches no input still appears, as the general error, so nothing the
    /// server said is ever swallowed.
    /// </remarks>
    private void Show(ApiException exception)
    {
        FullNameError = exception.ErrorFor("PatientName") ?? exception.ErrorFor("FullName");
        AddressError = exception.ErrorFor("Address");
        TajError = exception.ErrorFor("TajNumber") ?? exception.ErrorFor("Taj");
        ComplaintError = exception.ErrorFor("Complaint");

        var placed = FullNameError ?? AddressError ?? TajError ?? ComplaintError;

        // A conflict — the patient already has a visit open — names no field,
        // and is the most common refusal after a mistyped TAJ.
        ErrorMessage = placed is null ? exception.Detail : null;
    }

    /// <summary>Empties the form after a successful registration.</summary>
    /// <remarks>
    /// Only after success. A refused submission keeps everything that was
    /// typed, because retyping an address to fix one digit of a TAJ is how a
    /// reception desk comes to hate a system.
    /// </remarks>
    private void Clear()
    {
        FullName = string.Empty;
        Address = string.Empty;
        Taj = string.Empty;
        Complaint = string.Empty;
        SelectedSpecialty = Specialties.Count > 0 ? Specialties[0] : null;

        ClearErrors();
    }

    private void ClearErrors()
    {
        ErrorMessage = null;
        FullNameError = null;
        AddressError = null;
        TajError = null;
        ComplaintError = null;
    }

    partial void OnFullNameChanged(string value) => OnPropertyChanged(nameof(CanSubmit));

    partial void OnAddressChanged(string value) => OnPropertyChanged(nameof(CanSubmit));

    partial void OnTajChanged(string value) => OnPropertyChanged(nameof(CanSubmit));

    partial void OnComplaintChanged(string value) => OnPropertyChanged(nameof(CanSubmit));
}

/// <summary>One entry in the specialty picker.</summary>
/// <param name="Id">The specialty, or <c>null</c> for "decide later".</param>
/// <param name="Name">What to show.</param>
public sealed record SpecialtyChoice(Guid? Id, string Name)
{
    /// <summary>Register the arrival without routing it anywhere yet.</summary>
    public static SpecialtyChoice DecideLater { get; } = new(null, "— decide later —");
}
