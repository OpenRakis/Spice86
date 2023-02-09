using Spice86.Core.DI;

namespace Spice86;

using Avalonia;

using OxyPlot.Avalonia;

using Serilog;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Logging;

using System;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Spice86 Entry Point
/// </summary>
public class Program {
    private static readonly ILoggerService _loggerService;

    static Program() {
        _loggerService = new ServiceProvider().GetService<ILoggerService>();
    }

    /// <summary>
    /// Alternate Entry Point
    /// </summary>
    [STAThread]
    public static void RunWithOverrides<T>(string[] args, string expectedChecksum) where T : class, new() {
        List<string> argsList = args.ToList();

        // Inject override
        argsList.Add($"--{nameof(Configuration.OverrideSupplierClassName)}={typeof(T).AssemblyQualifiedName}");
        argsList.Add($"--{nameof(Configuration.ExpectedChecksum)}={expectedChecksum}");
        Main(argsList.ToArray());
    }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) {
        Configuration configuration = new CommandLineParser(
            new ServiceProvider().GetService<ILoggerService>())
                .ParseCommandLine(args);
        if(!configuration.HeadlessMode) {
            OxyPlotModule.EnsureLoaded();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
        }
        else {
            try {
                ProgramExecutor programExecutor = new ProgramExecutor(
                    new ServiceProvider().GetService<ILoggerService>(),
                    null, null, configuration);
                programExecutor.Run();
            } catch (Exception e) {
                e.Demystify();
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _loggerService.Error(e, "An error occurred during execution");
                }
                throw;
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}