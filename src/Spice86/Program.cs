namespace Spice86;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Microsoft.Extensions.DependencyInjection;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.VM;
using Spice86.DependencyInjection;
using Spice86.Infrastructure;
using Spice86.Shared.Interfaces;
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
        ServiceCollection serviceCollection = InjectCommonServices(args);
        //We need to build the service provider before retrieving the configuration service
        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        Configuration configuration = serviceProvider.GetRequiredService<Configuration>();
        if (configuration.HeadlessMode) {
            ProgramExecutor programExecutor = new(configuration, serviceProvider.GetRequiredService<ILoggerService>(),
                serviceProvider.GetRequiredService<IPauseHandler>(), null);
            programExecutor.Run();
        } else {
            ClassicDesktopStyleApplicationLifetime desktop = CreateDesktopApp();
            MainWindow mainWindow = new();
            serviceCollection.AddGuiInfrastructure(mainWindow);
            //We need to rebuild the service provider after adding new services to the collection
            using MainWindowViewModel mainWindowViewModel = serviceCollection.BuildServiceProvider().GetRequiredService<MainWindowViewModel>();
            StartGraphicalUserInterface(desktop, mainWindowViewModel, mainWindow, args);
        }
    }

    private static void StartGraphicalUserInterface(ClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel mainWindowViewModel, MainWindow mainWindow, string[] args) {
        mainWindow.DataContext = mainWindowViewModel;
        desktop.MainWindow = mainWindow;
        desktop.Start(args);
    }

    private static ClassicDesktopStyleApplicationLifetime CreateDesktopApp() {
        AppBuilder appBuilder = BuildAvaloniaApp();
        ClassicDesktopStyleApplicationLifetime desktop = SetupWithClassicDesktopLifetime(appBuilder);
        return desktop;
    }

    private static ServiceCollection InjectCommonServices(string[] args) {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddConfiguration(args);
        serviceCollection.AddLogging();
        serviceCollection.AddScoped<IPauseHandler, PauseHandler>();
        serviceCollection.AddScoped<IProgramExecutorFactory, ProgramExecutorFactory>();
        serviceCollection.AddScoped<MainWindowViewModel>();
        return serviceCollection;
    }

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .WithInterFont();

    private static ClassicDesktopStyleApplicationLifetime SetupWithClassicDesktopLifetime(AppBuilder builder) {
        var lifetime = new ClassicDesktopStyleApplicationLifetime {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        builder.SetupWithLifetime(lifetime);
        return lifetime;
    }
}