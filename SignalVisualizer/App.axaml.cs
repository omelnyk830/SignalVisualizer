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

        // Factory — swap to SerialSourceFactory for raw binary protocol
        services.AddSingleton<ISignalSourceFactory, MavlinkSourceFactory>();

        // UDP discovery — drones announce on port 14550
        services.AddSingleton<UdpDroneDiscovery>();

        // DroneManager: serial scan + UDP discovery
        services.AddSingleton(sp => new DroneManager(
            sp.GetRequiredService<ISignalSourceFactory>(),
            sp.GetRequiredService<UdpDroneDiscovery>(),
            portPattern: "usbmodem"));

        services.AddSingleton<MainWindowViewModel>();

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