namespace Spice86;

using Avalonia;
using Avalonia.Threading;

using Serilog;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Keyboard;
using Spice86.Logging;

using System;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Spice86 Entry Point
/// </summary>
public class Program {
    private static ILogger _logger = Serilogger.Logger.ForContext<Program>();

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
        try {
            Configuration configuration = CommandLineParser.ParseCommandLine(args);
            if(!configuration.HeadlessMode) {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
            }
            else {
                try {
                    ProgramExecutor programExecutor = new ProgramExecutor(null, null, configuration);
                    programExecutor.Run();
                } catch (Exception e) {
                    e.Demystify();
                    if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                        _logger.Error(e, "An error occurred during execution");
                    }
                    if(configuration.HeadlessMode) {
                        throw;
                    }
                }
            }
        }
        finally {
            ((IDisposable)Serilogger.Logger).Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}