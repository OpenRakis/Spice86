namespace Spice86;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Shared.Interfaces;
using Spice86.Infrastructure;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;

using Spice86.Logging;
using Spice86.Views;
using Spice86.ViewModels;

using MainWindow = Spice86.ViewModels.MainWindow;

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
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<ILoggerPropertyBag, LoggerPropertyBag>();
        serviceCollection.AddSingleton<ILoggerService, LoggerService>();
        
        if (!configuration.HeadlessMode) {
            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
            ILoggerService loggerService = serviceProvider.GetRequiredService<ILoggerService>();
            Startup.SetLoggingLevel(loggerService, configuration);
            
            AppBuilder appBuilder = BuildAvaloniaApp();
            ClassicDesktopStyleApplicationLifetime desktop = SetupWithClassicDesktopLifetime(appBuilder, args);
            App? app = (App?)appBuilder.Instance;
            
            if (app is null) {
                return;
            }
            
            Views.MainWindow mainWindow = new();
            using var mainWindowViewModel = new MainWindow(new AvaloniaKeyScanCodeConverter(),
                new ProgramExecutorFactory(configuration, loggerService),
                new UIDispatcher(Dispatcher.UIThread), new HostStorageProvider(mainWindow.StorageProvider),
                new TextClipboard(mainWindow.Clipboard), new UIDispatcherTimerFactory(), configuration, loggerService);
            mainWindow.DataContext = mainWindowViewModel;
            desktop.MainWindow = mainWindow;
            desktop.Start(args);
        }
        else {
            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
            ILoggerService loggerService = serviceProvider.GetRequiredService<ILoggerService>();
            Startup.SetLoggingLevel(loggerService, configuration);
            ProgramExecutor programExecutor = new(configuration, loggerService, null);
            programExecutor.Run();
        }
    }
    
    /// <summary>
    /// Configures and builds an Avalonia application instance.
    /// </summary>
    /// <returns>The built <see cref="AppBuilder"/> instance.</returns>
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