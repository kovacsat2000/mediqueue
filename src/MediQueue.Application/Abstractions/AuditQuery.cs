namespace MediQueue.Application.Abstractions;

/// <summary>
/// A validated, clamped request for one page of the audit trail.
/// </summary>
/// <remarks>
/// <para>
/// The clamping lives in the constructor rather than in the service, so a
/// repository cannot be handed a page size nobody bounded — the same argument
/// as D-47's value objects: the type carries the proof inward, and
/// "read a hundred thousand rows because a client sent
/// <c>pageSize=1000000</c>" stops being expressible.
/// </para>
/// <para>
/// Out-of-range values are clamped rather than rejected, at both ends and by
/// the same rule: a caller asking for 0 gets 1 and a caller asking for 500 gets
/// 200. Refusing one end while accepting the other needs two sentences to
/// explain and buys nothing — nobody can act differently on being told their
/// page size was too large, which is D-50's test.
/// </para>
/// </remarks>
public sealed record AuditQuery
{
    /// <summary>How many entries a page holds when the caller does not say.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The largest page the API will assemble, however much is asked for.</summary>
    public const int MaxPageSize = 200;

    private AuditQuery(Guid? patientId, Guid? userId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize)
    {
        PatientId = patientId;
        UserId = userId;
        From = from;
        To = to;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>Only entries concerning this patient.</summary>
    public Guid? PatientId { get; }

    /// <summary>Only entries made by this user.</summary>
    public Guid? UserId { get; }

    /// <summary>Only entries at or after this instant.</summary>
    public DateTimeOffset? From { get; }

    /// <summary>Only entries at or before this instant.</summary>
    public DateTimeOffset? To { get; }

    /// <summary>Which page, one-based and at least 1.</summary>
    public int Page { get; }

    /// <summary>How many entries this page holds, between 1 and <see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; }

    /// <summary>How many entries to skip to reach this page.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>Builds a query from whatever the caller sent.</summary>
    /// <param name="patientId">Filter by patient.</param>
    /// <param name="userId">Filter by actor.</param>
    /// <param name="from">Earliest instant.</param>
    /// <param name="to">Latest instant.</param>
    /// <param name="page">Which page; below 1 is clamped to 1.</param>
    /// <param name="pageSize">Page size; clamped into 1..<see cref="MaxPageSize"/>, defaulted when absent.</param>
    /// <returns>The clamped query.</returns>
    public static AuditQuery Create(
        Guid? patientId = null,
        Guid? userId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? page = null,
        int? pageSize = null) =>
        new(
            patientId,
            userId,
            from,
            to,
            Math.Max(1, page ?? 1),
            Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
