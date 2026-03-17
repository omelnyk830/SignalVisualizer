using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SignalVisualizer.Services;
using SignalVisualizer.ViewModels;
using SignalVisualizer.Views;

namespace SignalVisualizer;

public partial class App : Application
{
    private static ServiceProvider? _serviceProvider;
    public static IServiceProvider Services => _serviceProvider!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            _serviceProvider = ConfigureServices();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                _serviceProvider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Signal source — swap this line to change the input
        services.AddSingleton(_ => new MavlinkSignalSource("/dev/tty.usbmodem1103"));
        services.AddSingleton<ISignalSource>(sp => sp.GetRequiredService<MavlinkSignalSource>());
        services.AddSingleton<ICommandSource>(sp => sp.GetRequiredService<MavlinkSignalSource>());
        // For sources without commands, just register ISignalSource:
        // services.AddSingleton<ISignalSource>(_ => new MockSignalSource(frequencyHz: 1.0, samplesPerSecond: 100));

        services.AddSingleton<ISignalProcessor, SignalProcessor>();
        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}