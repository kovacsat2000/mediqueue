using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Validation;

namespace MediQueue.Domain.Users;

/// <summary>
/// Someone who signs in: an assistant or a doctor.
/// </summary>
/// <remarks>
/// <para>
/// One entity with a <see cref="UserRole"/> rather than a type hierarchy. At two
/// roles, inheritance would buy a discriminator column and cast-heavy queries in
/// exchange for a guarantee that one factory method and one test already give.
/// Role reads as data here, not as type.
/// </para>
/// <para>
/// The invariant — a doctor has a specialty, an assistant does not — is enforced
/// in the private constructor, which both factories funnel through. The public
/// surface then makes the illegal states hard to even express: there is no
/// parameter with which to give an assistant a specialty.
/// </para>
/// </remarks>
public sealed class User
{
    /// <summary>The longest username the system accepts. The database column is sized from this.</summary>
    public const int MaxUsernameLength = 50;

    /// <summary>The longest full name the system accepts. The database column is sized from this.</summary>
    public const int MaxFullNameLength = 200;

    private User(
        Guid id,
        string username,
        string fullName,
        string passwordHash,
        UserRole role,
        Guid? specialtyId,
        bool isActive)
    {
        if (role == UserRole.Doctor && (specialtyId is null || specialtyId == Guid.Empty))
        {
            throw new DomainException("A doctor must belong to a specialty.");
        }

        if (role == UserRole.Assistant && specialtyId is not null)
        {
            throw new DomainException("An assistant must not belong to a specialty.");
        }

        Id = id;
        Username = username;
        FullName = fullName;
        PasswordHash = passwordHash;
        Role = role;
        SpecialtyId = specialtyId;
        IsActive = isActive;
    }

    /// <summary>The identifier. Time-ordered, so index pages stay dense as rows are inserted.</summary>
    public Guid Id { get; private set; }

    /// <summary>The name used to sign in. Unique across the practice.</summary>
    public string Username { get; private set; }

    /// <summary>The name shown in the interface and recorded in the audit log.</summary>
    public string FullName { get; private set; }

    /// <summary>The hashed password. The domain never sees the plaintext.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Which of the two roles this user holds.</summary>
    public UserRole Role { get; private set; }

    /// <summary>The doctor's specialty. Always <c>null</c> for an assistant.</summary>
    public Guid? SpecialtyId { get; private set; }

    /// <summary>Whether the user may sign in and be assigned work.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Creates an assistant, who by construction has no specialty.</summary>
    /// <param name="username">The name used to sign in.</param>
    /// <param name="fullName">The name shown in the interface.</param>
    /// <param name="passwordHash">The hashed password.</param>
    /// <param name="now">The current time, supplied by the caller so the identifier is deterministic.</param>
    /// <returns>The new assistant.</returns>
    /// <exception cref="ValidationException"><paramref name="username"/> or <paramref name="fullName"/> is blank or too long.</exception>
    public static User CreateAssistant(string username, string fullName, string passwordHash, DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(now),
            TextRules.RequiredSingleWord(username, nameof(Username), MaxUsernameLength),
            TextRules.Required(fullName, nameof(FullName), MaxFullNameLength),
            passwordHash,
            UserRole.Assistant,
            specialtyId: null,
            isActive: true);

    /// <summary>Creates a doctor, who must belong to a specialty.</summary>
    /// <param name="username">The name used to sign in.</param>
    /// <param name="fullName">The name shown in the interface.</param>
    /// <param name="passwordHash">The hashed password.</param>
    /// <param name="specialtyId">The specialty the doctor practises. Required.</param>
    /// <param name="now">The current time, supplied by the caller so the identifier is deterministic.</param>
    /// <returns>The new doctor.</returns>
    /// <exception cref="DomainException"><paramref name="specialtyId"/> is empty.</exception>
    /// <exception cref="ValidationException"><paramref name="username"/> or <paramref name="fullName"/> is blank or too long.</exception>
    public static User CreateDoctor(
        string username,
        string fullName,
        string passwordHash,
        Guid specialtyId,
        DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(now),
            TextRules.RequiredSingleWord(username, nameof(Username), MaxUsernameLength),
            TextRules.Required(fullName, nameof(FullName), MaxFullNameLength),
            passwordHash,
            UserRole.Doctor,
            specialtyId,
            isActive: true);
}
