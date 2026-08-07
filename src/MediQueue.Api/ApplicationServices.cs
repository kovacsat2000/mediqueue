using MediQueue.Application.Auditing;
using MediQueue.Application.Authentication;
using MediQueue.Application.Visits;
using MediQueue.Domain.Scheduling;

namespace MediQueue.Api;

/// <summary>Registers the application layer's use cases.</summary>
/// <remarks>
/// This lives in the API rather than in <c>MediQueue.Application</c> on purpose.
/// Application may reference a package only if every type it uses from it is an
/// abstraction implying no storage, no transport and no hosting model —
/// <c>IServiceCollection</c> implies a hosting model, so an
/// <c>AddApplication()</c> inside that project would break the rule the project
/// exists to demonstrate. The composition root is supposed to know the
/// composition.
/// </remarks>
public static class ApplicationServices
{
    /// <summary>Adds the use cases and the assignment policy.</summary>
    /// <param name="services">The container.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AuthenticationService>();

        services.AddScoped<VisitContextLoader>();
        services.AddScoped<VisitAnnouncer>();
        services.AddScoped<VisitRegistrationService>();
        services.AddScoped<VisitAssignmentService>();
        services.AddScoped<VisitLifecycleService>();
        services.AddScoped<VisitQueryService>();
        services.AddScoped<QueueQueryService>();
        services.AddScoped<AuditQueryService>();

        // The one genuinely algorithmic rule in the assignment, named and
        // swappable: an alternative policy is a change to this line.
        services.AddSingleton<IDoctorAssignmentStrategy, ShortestQueueAssignmentStrategy>();

        return services;
    }
}
