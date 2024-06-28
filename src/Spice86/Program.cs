namespace Spice86;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Threading;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Shared.Interfaces;

using Microsoft.Extensions.DependencyInjection;

using Spice86.DependencyInjection;
using Spice86.Infrastructure;
using Spice86.Logging;
using Spice86.ViewModels;
using Spice86.Views;

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
        Configuration configuration = CommandLineParser.ParseCommandLine(args);

        if (configuration.HeadlessMode) {
            ProgramExecutor programExecutor = new(configuration, new LoggerService(new LoggerPropertyBag()), null);
            programExecutor.Run();
        }
        else {
            StartGraphicalUserInterface(args, configuration);
        }
    }

    private static void StartGraphicalUserInterface(string[] args, Configuration configuration) {
        ClassicDesktopStyleApplicationLifetime desktop = CreateDesktopApp(args);

        MainWindow mainWindow = new();
        ServiceProvider serviceProvider = InjectServices(configuration, mainWindow);

        using MainWindowViewModel mainWindowViewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
        
        mainWindow.DataContext = mainWindowViewModel;
        desktop.MainWindow = mainWindow;
        desktop.Start(args);
    }
    
    private static ClassicDesktopStyleApplicationLifetime CreateDesktopApp(string[] args) {
        AppBuilder appBuilder = BuildAvaloniaApp();
        ClassicDesktopStyleApplicationLifetime desktop = SetupWithClassicDesktopLifetime(appBuilder, args);
        return desktop;
    }

    private static ServiceProvider InjectServices(Configuration configuration, MainWindow mainWindow) {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddUserInterfaceInfrastructure(mainWindow);
        serviceCollection.AddSingleton(configuration);
        serviceCollection.AddTransient<IProgramExecutorFactory, ProgramExecutorFactory>();
        serviceCollection.AddScoped<MainWindowViewModel>();
        
        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        ILoggerService loggerService = serviceProvider.GetRequiredService<ILoggerService>();
        Startup.SetLoggingLevel(loggerService, configuration);
        return serviceProvider;
    }

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .WithInterFont();

    private static ClassicDesktopStyleApplicationLifetime SetupWithClassicDesktopLifetime(
        AppBuilder builder, string[] args) {
        var lifetime = new ClassicDesktopStyleApplicationLifetime {
            Args = args,
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        builder.SetupWithLifetime(lifetime);
        return lifetime;
    }
}