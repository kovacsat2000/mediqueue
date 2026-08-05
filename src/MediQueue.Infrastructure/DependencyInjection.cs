using MediQueue.Domain.Users;
using MediQueue.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediQueue.Infrastructure;

/// <summary>
/// Registers everything this layer implements. This is the only place the API
/// project touches Infrastructure — controllers depend on abstractions.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Adds persistence, the clock, password hashing and the seeder.</summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Supplies <c>ConnectionStrings:Default</c>.</param>
    /// <returns>The container, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The connection string is missing.</exception>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is not configured. See appsettings.Development.json.");

        services.AddDbContext<MediQueueDbContext>(options => options.UseNpgsql(connectionString));

        // The clock, everywhere outside Domain. TimeProvider is built into the
        // framework and has a first-party fake, so tests substitute it without a
        // hand-rolled IClock interface.
        services.TryAddSingletonTimeProvider();

        // The hasher only, not the ASP.NET Core Identity stack. P3 reuses it
        // to verify passwords at login.
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddScoped<DatabaseSeeder>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
