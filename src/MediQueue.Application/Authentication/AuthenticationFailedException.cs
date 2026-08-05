namespace MediQueue.Application.Authentication;

/// <summary>
/// Sign-in was refused.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the application layer rather than the domain because it is not a
/// business rule about a medical practice; it is a rule about this system's front
/// door. The domain has no concept of signing in.
/// </para>
/// <para>
/// <strong>There is deliberately only one of these, with one message.</strong>
/// An unknown username, a wrong password and a deactivated account are
/// indistinguishable to the caller. Telling them apart would let anyone
/// enumerate valid usernames one request at a time, and "user not found" versus
/// "wrong password" is exactly the distinction that makes that possible.
/// </para>
/// </remarks>
public sealed class AuthenticationFailedException : Exception
{
    /// <summary>The only message this exception ever carries.</summary>
    public const string GenericMessage = "Invalid username or password.";

    /// <summary>Creates the exception with the fixed, deliberately uninformative message.</summary>
    public AuthenticationFailedException()
        : base(GenericMessage)
    {
    }
}
