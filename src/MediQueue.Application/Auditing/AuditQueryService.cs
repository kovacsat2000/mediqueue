using MediQueue.Application.Abstractions;
using MediQueue.Contracts.Auditing;

namespace MediQueue.Application.Auditing;

/// <summary>
/// Reading the audit trail, projected for whoever is asking.
/// </summary>
/// <remarks>
/// Any authenticated role may read the log. The role does not decide
/// <em>whether</em> the entries come back, only whether the clinical values in
/// them are legible — see <see cref="AuditMapper"/>, which is where that single
/// decision lives.
/// </remarks>
public sealed class AuditQueryService(IAuditRepository audit, ICurrentUser currentUser)
{
    /// <summary>Reads one page of the trail, newest first.</summary>
    /// <param name="query">The filter and the page. Already clamped by its own constructor.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page, with sensitive values redacted unless the caller is a doctor.</returns>
    public async Task<AuditPageDto> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await audit.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        return AuditMapper.ToPage(
            page.Entries,
            page.TotalCount,
            query.Page,
            query.PageSize,
            currentUser.Role);
    }
}
