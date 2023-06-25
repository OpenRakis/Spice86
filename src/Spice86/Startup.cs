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
    private readonly Configuration _configuration;
    
    public Startup(ILoggerService loggerService, Configuration configuration) {
        _loggerService = loggerService;
        _configuration = configuration;
    }
    
    public void StartApp() {
        SetLoggingLevel();
        if (!_configuration.HeadlessMode) {
            StartMainWindow();
        } else {
            StartConsole();
        }
    }

    private void SetLoggingLevel() {
        if (_configuration.SilencedLogs) {
            _loggerService.AreLogsSilenced = true;
        }
        else if (_configuration.WarningLogs) {
            _loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Warning;
        }
        else if (_configuration.VerboseLogs) {
            _loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
        }
    }

    private void StartConsole() {
        ProgramExecutor programExecutor = new(_loggerService, null, _configuration);
        programExecutor.Run();
    }

    private void StartMainWindow() {
        OxyPlotModule.EnsureLoaded();
        AppBuilder appBuilder = BuildAvaloniaApp();
        ClassicDesktopStyleApplicationLifetime desktop = SetuptWithClassicDesktopLifetime(appBuilder, Array.Empty<string>());
        App app = (App) appBuilder.Instance;
        app.SetupMainWindow(_configuration, _loggerService);
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