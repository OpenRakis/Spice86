namespace Spice86;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.DependencyInjection;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;

/// <summary>
/// Entry point for Spice86 application.
/// </summary>
public class Program {
    private readonly ILoggerService _loggerService;
    internal Program(ILoggerService loggerService) => _loggerService = loggerService;

    /// <summary>
    /// Alternate entry point to use when injecting a class that defines C# overrides of the x86 assembly code found in the target DOS program.
    /// </summary>
    /// <typeparam name="T">Type of the class that defines C# overrides of the x86 assembly code.</typeparam>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="expectedChecksum">The expected checksum of the target DOS program.</param>
    [STAThread]
    public static void RunWithOverrides<T>(string[] args, string expectedChecksum) where T : class, new() {
        List<string> argsList = args.ToList();

        // Inject override
        argsList.Add($"--{nameof(Configuration.OverrideSupplierClassName)}={typeof(T).AssemblyQualifiedName}");
        argsList.Add($"--{nameof(Configuration.ExpectedChecksum)}={expectedChecksum}");
        Main(argsList.ToArray());
    }

    /// <summary>
    /// Entry point of the application.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    [STAThread]
    public static void Main(string[] args) {
        Configuration configuration = CommandLineParser.ParseCommandLine(args);
        Program program = new Composition().Resolve<Program>();
        program.StartApp(configuration, args);
    }

    private void StartApp(Configuration configuration, string[] args) {
        Startup.SetLoggingLevel(_loggerService, configuration);
        if (!configuration.HeadlessMode) {
            StartMainWindow(configuration, _loggerService, args);
        }
        else {
            StartConsole(configuration, _loggerService);
        }
    }

    private static void StartConsole(Configuration configuration, ILoggerService loggerService) {
        ProgramExecutor programExecutor = new(loggerService, null, configuration);
        programExecutor.Run();
    }

    private static void StartMainWindow(Configuration configuration, ILoggerService loggerService, string[] args) {
        AppBuilder appBuilder = BuildAvaloniaApp();
        ClassicDesktopStyleApplicationLifetime desktop = SetuptWithClassicDesktopLifetime(appBuilder, args);
        App? app = (App?)appBuilder.Instance;
        app?.SetupMainWindow(desktop, configuration, loggerService);
        desktop.Start(args);
    }

    /// <summary>
    /// Configures and builds an Avalonia application instance.
    /// </summary>
    /// <returns>The built <see cref="AppBuilder"/> instance.</returns>
    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static ClassicDesktopStyleApplicationLifetime SetuptWithClassicDesktopLifetime(
        AppBuilder builder, string[] args) {
        var lifetime = new ClassicDesktopStyleApplicationLifetime {
            Args = args,
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        builder.SetupWithLifetime(lifetime);
        return lifetime;
    }
}