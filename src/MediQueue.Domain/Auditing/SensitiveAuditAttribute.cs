namespace MediQueue.Domain.Auditing;

/// <summary>
/// Marks a property whose values must never be shown verbatim in the audit log
/// to someone who is not allowed to see the property itself.
/// </summary>
/// <remarks>
/// <para>
/// The audit log is required to record what changed, and assistants are
/// required to be able to query it — but an assistant must never learn a
/// diagnosis. Without this marker the audit trail would hand over through the
/// back door exactly what the API withholds at the front.
/// </para>
/// <para>
/// The attribute lives in the domain so that the rule travels with the property
/// it describes, and so this project keeps its zero dependencies. The audit
/// interceptor in the infrastructure layer reads it and redacts accordingly.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAuditAttribute : Attribute;
