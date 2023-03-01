namespace Spice86;

using Avalonia;

using OxyPlot.Avalonia;

using Microsoft.Extensions.DependencyInjection;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Shared.Interfaces;

using System;
using System.Linq;

/// <summary>
/// Spice86 Entry Point
/// </summary>
public class Program {
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
        Configuration configuration = CommandLineParser.ParseCommandLine(args);
        
        if(!configuration.HeadlessMode) {
            OxyPlotModule.EnsureLoaded();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
        }
        else {
            ServiceProvider serviceProvider = Startup.StartupInjectedServices(args);
            ILoggerService? loggerService = serviceProvider.GetService<ILoggerService>();
            if (loggerService is null) {
                throw new InvalidOperationException("Could not get logging service from DI !");
            }
            
            ProgramExecutor programExecutor = new ProgramExecutor(
                loggerService,
                null, null, configuration);
            programExecutor.Run();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}