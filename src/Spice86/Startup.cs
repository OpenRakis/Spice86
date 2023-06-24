namespace Spice86;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using OxyPlot.Avalonia;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Shared.Interfaces;

public class Startup {
    private readonly ILoggerService _loggerService;
    
    
    public Startup(ILoggerService loggerService) {
        _loggerService = loggerService;
    }
    
    public void StartApp(Configuration configuration) {
        SetLoggingLevel(configuration, _loggerService);
        if (!configuration.HeadlessMode) {
            StartMainWindow(configuration, _loggerService);
        } else {
            StartConsole(configuration, _loggerService);
        }
    }

    private static void SetLoggingLevel(Configuration configuration, ILoggerService loggerService) {
        if (configuration.SilencedLogs) {
            loggerService.AreLogsSilenced = true;
        }
        else if (configuration.WarningLogs) {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Warning;
        }
        else if (configuration.VerboseLogs) {
            loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
        }
    }

    private static void StartConsole(Configuration configuration, ILoggerService loggerService) {
        ProgramExecutor programExecutor = new(loggerService, null, configuration);
        programExecutor.Run();
    }

    private static void StartMainWindow(Configuration configuration, ILoggerService loggerService) {
        OxyPlotModule.EnsureLoaded();
        AppBuilder appBuilder = BuildAvaloniaApp();
        ClassicDesktopStyleApplicationLifetime desktop = SetuptWithClassicDesktopLifetime(appBuilder, Array.Empty<string>());
        App app = (App) appBuilder.Instance;
        app.SetupMainWindow(configuration, loggerService);
        desktop.Start(Array.Empty<string>());
    }

    /// <summary>
    /// Configures and builds an Avalonia application instance.
    /// </summary>
    /// <returns>The built <see cref="AppBuilder"/> instance.</returns>
    private static AppBuilder BuildAvaloniaApp() {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    private static ClassicDesktopStyleApplicationLifetime SetuptWithClassicDesktopLifetime<T>(
        T builder, string[] args)
        where T : AppBuilderBase<T>, new() {
        var lifetime = new ClassicDesktopStyleApplicationLifetime {
            Args = args,
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        builder.SetupWithLifetime(lifetime);
        return lifetime;
    }
}