namespace Spice86; 

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using OxyPlot.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Shared.Interfaces;

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
        IServiceCollection serviceCollection = new ServiceCollection();
        IServiceProvider serviceProvider = new Startup(serviceCollection).BuildServiceContainer(args);
        ICommandLineParser commandLineParser = serviceProvider.GetRequiredService<ICommandLineParser>();
        Configuration configuration = commandLineParser.ParseCommandLine(args);

        if (!configuration.HeadlessMode) {
            OxyPlotModule.EnsureLoaded();
            AppBuilder appBuilder = BuildAvaloniaApp();
            ClassicDesktopStyleApplicationLifetime desktop = SetuptWithClassicDesktopLifetime(appBuilder, args);
            App app = (App)appBuilder.Instance;
            app.SetupMainWindow(serviceProvider);
            desktop.Start(args);
        }
        else {
            ILoggerService loggerService = serviceProvider.GetRequiredService<ILoggerService>();
            ProgramExecutor programExecutor = new(loggerService, null, configuration);
            programExecutor.Run();
        }
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

    /// <summary>
    /// Configures and builds an Avalonia application instance.
    /// </summary>
    /// <returns>The built <see cref="AppBuilder"/> instance.</returns>
    public static AppBuilder BuildAvaloniaApp() {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}