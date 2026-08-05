using System.Security.Claims;
using System.Text;
using MediQueue.Application.Abstractions;
using MediQueue.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MediQueue.Infrastructure.Authentication;

/// <summary>Issues HS256 JSON Web Tokens.</summary>
/// <remarks>
/// The only class in the system that knows what a JWT is. Everything above it
/// deals in <see cref="ITokenIssuer"/>, which says a token is a string with an
/// expiry and nothing more.
/// </remarks>
public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenIssuer
{
    /// <summary>The claim carrying a doctor's specialty. Absent for assistants.</summary>
    public const string SpecialtyIdClaim = "specialtyId";

    /// <summary>
    /// The claim carrying the role, as a short name rather than the
    /// WS-Federation URI that <see cref="ClaimTypes.Role"/> expands to.
    /// </summary>
    public const string RoleClaim = "role";

    /// <summary>The claim carrying the display name.</summary>
    public const string NameClaim = JwtRegisteredClaimNames.Name;

    private readonly JwtOptions _options = options.Value;

    /// <inheritdoc />
    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddHours(_options.LifetimeHours);

        // Short claim names, matching what the validation parameters are told to
        // read. Nothing beyond what authorisation and auditing need: no email,
        // no permission list, nothing that would have to be re-issued when it
        // changes.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            // The short name, deliberately. System.Security.Claims.ClaimTypes.Role
            // is the WS-Federation URI, and putting that in the token is half of
            // the claim-mapping trap this scheme is configured to avoid.
            new(RoleClaim, user.Role.ToString()),
        };

        if (user.SpecialtyId is { } specialtyId)
        {
            claims.Add(new Claim(SpecialtyIdClaim, specialtyId.ToString()));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
