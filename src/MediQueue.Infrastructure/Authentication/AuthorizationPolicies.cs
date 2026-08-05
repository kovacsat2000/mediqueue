namespace MediQueue.Infrastructure.Authentication;

/// <summary>
/// The names of the authorization policies, in one place so a controller and the
/// registration cannot drift apart over a typo.
/// </summary>
/// <remarks>
/// Resource-level rules — a doctor may only touch their own queue — are not
/// policies. They depend on the row being acted on, so they arrive in P4 beside
/// the endpoints that need them rather than being guessed at here.
/// </remarks>
public static class AuthorizationPolicies
{
    /// <summary>Only an assistant may pass.</summary>
    public const string AssistantOnly = nameof(AssistantOnly);

    /// <summary>Only a doctor may pass.</summary>
    public const string DoctorOnly = nameof(DoctorOnly);
}
