namespace MediQueue.Domain.Specialties;

/// <summary>
/// A field of medicine a patient can be routed to, and that a doctor practises.
/// </summary>
public sealed class Specialty
{
    private Specialty(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>The identifier. Time-ordered, so index pages stay dense as rows are inserted.</summary>
    public Guid Id { get; private set; }

    /// <summary>The name, as the practice refers to it.</summary>
    public string Name { get; private set; }

    /// <summary>Creates a specialty.</summary>
    /// <param name="name">The name, as the practice refers to it.</param>
    /// <param name="now">The current time, supplied by the caller so the identifier is deterministic.</param>
    /// <returns>The new specialty.</returns>
    public static Specialty Create(string name, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), name);
}
