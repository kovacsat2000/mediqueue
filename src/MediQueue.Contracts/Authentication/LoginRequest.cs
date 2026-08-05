namespace MediQueue.Contracts.Authentication;

/// <summary>Credentials presented to <c>POST /api/auth/login</c>.</summary>
/// <param name="Username">The username.</param>
/// <param name="Password">The password, in plain text over TLS.</param>
public sealed record LoginRequest(string Username, string Password);
