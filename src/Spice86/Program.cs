namespace Spice86;

using Avalonia;
using Avalonia.ReactiveUI;

using Serilog;

using Spice86.UI;

using System;
using System.Linq;

/// <summary>
/// Spice86 Entry Point
/// </summary>
internal class Program {

    private static readonly ILogger _logger = new LoggerConfiguration()
        .WriteTo.Console()
        .WriteTo.Debug()
        .CreateLogger();

    public Program() {
        Log.Logger = _logger;
    }

    /// <summary>
    /// Alternate Entry Point
    /// </summary>
    [STAThread]
    public static void RunWithOverrides<T>(string[] args, T overrides, string expectedChecksum) where T : class, new() {
        var argsList = args.ToList();

        // Inject override
        argsList.Add($"--overrideSupplierClassName={overrides.GetType().FullName}");
        argsList.Add($"--expectedChecksum={expectedChecksum}");
        Program.Main(args.ToArray());
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
}