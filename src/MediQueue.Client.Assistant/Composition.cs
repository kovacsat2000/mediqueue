using MediQueue.Client.Core.Api;
using MediQueue.Client.Core.Realtime;
using MediQueue.Client.Core.ViewModels;
using MediQueue.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediQueue.Client.Assistant;

/// <summary>
/// The composition root: the one place that knows how the parts fit together.
/// </summary>
/// <remarks>
/// Nothing below the views constructs a service, and no view constructs
/// anything at all — a <c>new</c> inside a window is how a client ends up with
/// two sessions and one of them signed out.
/// </remarks>
public static class Composition
{
    /// <summary>Builds the container.</summary>
    /// <returns>The service provider the application resolves from.</returns>
    public static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            // Editable beside the binary, so pointing the app at a different
            // host needs no rebuild.
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var baseAddress = configuration["Api:BaseAddress"]
            ?? throw new InvalidOperationException(
                "Configuration 'Api:BaseAddress' is missing from appsettings.json.");

        var services = new ServiceCollection();

        // The address is configuration, never a literal in the client.
        //
        // Registered as IAssistantApi and nothing else. The concrete client also
        // implements IDoctorApi, and deliberately is not registered as it — so
        // this application has no way to reach an endpoint returning a
        // diagnosis, by resolution or by compilation.
        services.AddHttpClient<MediQueueApiClient>(client => client.BaseAddress = new Uri(baseAddress));
        services.AddSingleton<IAssistantApi>(provider => provider.GetRequiredService<MediQueueApiClient>());
        services.AddSingleton<ILoginApi>(provider => provider.GetRequiredService<IAssistantApi>());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAuthSession, AuthSession>();

        // The hub lives under the same host as the API, so its address is
        // derived rather than configured separately — two settings that must
        // agree are one setting somebody will eventually get wrong.
        services.AddSingleton<IQueueConnection>(provider => new QueueConnection(
            new Uri(new Uri(baseAddress), "hubs/queue"),
            provider.GetRequiredService<IAuthSession>()));

        // This application admits assistants only, and says so to anybody else.
        services.AddSingleton(provider => new LoginViewModel(
            provider.GetRequiredService<ILoginApi>(),
            provider.GetRequiredService<IAuthSession>(),
            UserRole.Assistant));

        services.AddSingleton<RegistrationViewModel>();
        services.AddSingleton<PracticeViewModel>();
        services.AddSingleton<AssistantViewModel>();

        services.AddSingleton(provider =>
        {
            var assistant = provider.GetRequiredService<AssistantViewModel>();

            return new ShellViewModel(
                provider.GetRequiredService<LoginViewModel>(), assistant, assistant.StartCommand);
        });

        return services.BuildServiceProvider();
    }
}
