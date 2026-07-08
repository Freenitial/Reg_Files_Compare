// Avalonia application root. Builds the dependency-injection container, resolves the main
// view model, and wires it to the main window when the framework lifecycle is ready.

using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RegCompare.Services;
using RegCompare.ViewModels;
using RegCompare.Views;

namespace RegCompare;

public partial class App : Application
{
    /// <summary>
    /// Application-wide DI container. Resolved in <see cref="OnFrameworkInitializationCompleted"/>.
    /// </summary>
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
            Services.GetRequiredService<ThemeService>().Apply();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(static builder =>
        {
            builder.AddSimpleConsole(opts =>
            {
                opts.SingleLine = true;
                opts.UseUtcTimestamp = true;
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddSingleton<RegFileParser>();
        services.AddSingleton<DragDropService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
