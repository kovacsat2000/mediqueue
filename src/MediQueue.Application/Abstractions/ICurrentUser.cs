using MediQueue.Contracts;

namespace MediQueue.Application.Abstractions;

/// <summary>Who is making the current request.</summary>
/// <remarks>
/// It exists now, ahead of most of its consumers, because the audit interceptor
/// in P5 needs to record who changed what and its shape should not change then.
/// Everything is nullable: a request may be anonymous, and an assistant has no
/// specialty.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>The signed-in user's identifier, or <c>null</c> if the request is anonymous.</summary>
    Guid? UserId { get; }

    /// <summary>The signed-in user's role, or <c>null</c> if the request is anonymous.</summary>
    UserRole? Role { get; }

    /// <summary>The doctor's specialty. <c>null</c> for an assistant or an anonymous request.</summary>
    Guid? SpecialtyId { get; }

    /// <summary>Whether the request carries a valid identity at all.</summary>
    bool IsAuthenticated { get; }
}
