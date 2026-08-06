namespace MediQueue.Infrastructure.Auditing;

/// <summary>
/// Lets one specific operation declare that its writes are not business events.
/// </summary>
/// <remarks>
/// <para>
/// There is exactly one caller: the database seeder. Seed rows are fixture, not
/// history — nobody registered Tóth Erzsébet, she was compiled in — and an audit
/// trail whose first two dozen entries have no actor teaches a reader that "no
/// actor" is normal. It must never be normal.
/// </para>
/// <para>
/// <strong>The shape matters more than the feature.</strong> The tempting
/// implementation is "skip the entry when there is no current user", which needs
/// no type at all. That version is dangerous: it makes a broken identity
/// pipeline — precisely the failure D-37 documents, where every actor silently
/// became null — produce an audit log that is silently *empty*. Suppression is
/// therefore something a caller has to ask for, in writing, and the absence of
/// an actor is never on its own a reason to skip.
/// </para>
/// <para>
/// Scoped, so a suppressed seed cannot leak into a concurrent request.
/// </para>
/// </remarks>
public sealed class AuditSuppression
{
    /// <summary>Whether the current scope has declared its writes to be fixture.</summary>
    public bool IsSuppressed { get; private set; }

    /// <summary>Suppresses auditing until the returned handle is disposed.</summary>
    /// <returns>The handle that restores auditing.</returns>
    public IDisposable Suppress()
    {
        IsSuppressed = true;

        return new Restore(this);
    }

    private sealed class Restore(AuditSuppression owner) : IDisposable
    {
        public void Dispose() => owner.IsSuppressed = false;
    }
}
