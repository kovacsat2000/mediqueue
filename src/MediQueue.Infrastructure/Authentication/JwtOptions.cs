namespace MediQueue.Infrastructure.Authentication;

/// <summary>Everything the token scheme needs, bound from the <c>Jwt</c> configuration section.</summary>
public sealed class JwtOptions
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// HS256 derives its key directly from these bytes and refuses anything
    /// shorter than the hash it produces.
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>Who issued the token. Validated on every request.</summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Who the token is for. Validated on every request.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>The symmetric signing key. At least <see cref="MinimumSigningKeyBytes"/> bytes.</summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// How long a token lasts. Eight hours because a clinic shift is the unit
    /// that makes sense here — long enough that nobody is asked to sign in again
    /// halfway through a surgery, and short enough that a token left on a
    /// workstation is useless the next day. It also removes any chance of a
    /// token expiring in the middle of a demonstration.
    /// </summary>
    public int LifetimeHours { get; init; } = 8;
}
