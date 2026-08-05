namespace MediQueue.Contracts.Authentication;

/// <summary>A successful sign-in.</summary>
/// <param name="AccessToken">The bearer token to send on every subsequent request.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
/// <param name="User">Who the token belongs to, so the client needs no second call.</param>
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);
