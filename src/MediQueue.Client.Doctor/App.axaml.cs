using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MediQueue.Client.Core.ViewModels;
using MediQueue.Client.Doctor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MediQueue.Client.Doctor;

/// <summary>The application.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = Composition.Build();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<ShellViewModel>(),
            };

            desktop.ShutdownRequested += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
