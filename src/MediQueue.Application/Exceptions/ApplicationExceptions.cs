namespace MediQueue.Application.Exceptions;

/// <summary>
/// The caller is authenticated, but not for this particular resource.
/// </summary>
/// <remarks>
/// Distinct from a failed sign-in, which is a 401 and means "we do not know who
/// you are". This is a 403: we know exactly who you are, and the answer is
/// still no. A doctor reaching for another doctor's visit is the case it exists
/// for.
/// </remarks>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>
/// The resource does not exist, or is invisible to this caller.
/// </summary>
/// <remarks>
/// The two are deliberately the same answer. A soft-deleted visit is filtered
/// out of every query, so it is not found — and telling a caller that something
/// exists but they may not see it is itself a disclosure.
/// </remarks>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>
/// A business rule was broken that needed a query to detect.
/// </summary>
/// <remarks>
/// These are not <c>DomainException</c>s, and the line is the one D-32 draws: a
/// domain exception means an aggregate holding only its own state could see the
/// rule was broken. A patient already having an open visit, or a specialty
/// having no available doctor, cannot be decided by any single aggregate — both
/// need a lookup. Putting them in the domain would mean the domain knowing
/// about repositories.
/// </remarks>
public sealed class ConflictException(string message) : Exception(message);
