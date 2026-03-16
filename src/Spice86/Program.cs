namespace Spice86;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

using Spice86.Core.CLI;

/// <summary>
/// Entry point for Spice86 application.
/// </summary>
public class Program {
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
        Configuration? configuration = new CommandLineParser().ParseCommandLine(args);
        if (configuration == null) {
            return;
        }

        switch (configuration.HeadlessMode) {
            case HeadlessType.Minimal: {
                Spice86DependencyInjection spice86DependencyInjection = new(configuration);
                spice86DependencyInjection.HeadlessModeStart();
                break;
            }
            case HeadlessType.Avalonia:
                BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions {
                    UseHeadlessDrawing = false
                }).StartWithClassicDesktopLifetime(args, ShutdownMode.OnLastWindowClose);
                break;
            default:
                // Start the application - close all windows when main window closes
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
                break;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}