namespace Spice86;

using Avalonia;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using Spice86.Emulator;
using Spice86.UI;

using System;
using System.Linq;

/// <summary>
/// Spice86 Entry Point
/// </summary>
public class Program {
    private const string LogFormat = "[{Timestamp:HH:mm:ss} {Level:u3} {Properties}] {Message:lj}{NewLine}{Exception}";
    public static LoggingLevelSwitch LogLevelSwitch { get; set; } = new(LogEventLevel.Warning);
    private static readonly ILogger _logger = new LoggerConfiguration()
        .WriteTo.Console(outputTemplate: LogFormat)
        .WriteTo.Debug(outputTemplate: LogFormat)
        .MinimumLevel.ControlledBy(LogLevelSwitch)
        .MinimumLevel.Override("Spice86.Emulator.Devices.Sound.SoundBlaster", LogEventLevel.Debug)
        .MinimumLevel.Override("Spice86.Emulator.IOPorts.IOPortDispatcher", LogEventLevel.Debug)
        //PIC can be very verbose when programs mistreat it ...
        .MinimumLevel.Override("Spice86.Emulator.Devices.ExternalInput.Pic", LogEventLevel.Debug)
        //Timer can be very verbose
        .MinimumLevel.Override("Spice86.Emulator.Devices.Timer.Timer", LogEventLevel.Debug)
        ////PC speaker is usually not interesting
        .MinimumLevel.Override("Spice86.Emulator.Devices.Sound.PcSpeaker", LogEventLevel.Debug)
        ////Display file IO and other DOS interactions
        .MinimumLevel.Override("Spice86.Emulator.InterruptHandlers.Dos", LogEventLevel.Information)
        ////Display Video bios interactions
        .MinimumLevel.Override("Spice86.Emulator.InterruptHandlers.Vga", LogEventLevel.Debug)
        ////A few logs at load time
        //.MinimumLevel.Override("Spice86.Emulator.LoadableFile", LogEventLevel.Information)
        ////Display program load informations
        //.MinimumLevel.Override("Spice86.Emulator.ProgramExecutor", LogEventLevel.Information)
        .CreateLogger();
    
    public static ILogger Logger => _logger;

    /// <summary>
    /// Alternate Entry Point
    /// </summary>
    [STAThread]
    public static void RunWithOverrides<T>(string[] args, string expectedChecksum) where T : class, new() {
        var argsList = args.ToList();

        // Inject override
        argsList.Add($"--{nameof(Configuration.OverrideSupplierClassName)}={typeof(T).AssemblyQualifiedName}");
        argsList.Add($"--{nameof(Configuration.ExpectedChecksum)}={expectedChecksum}");
        Program.Main(argsList.ToArray());
    }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) {
        string[]? newArgs = args;
        //if (OperatingSystem.IsLinux()) {
        //    newArgs = new string[] { "-e", "/mnt/c/Jeux/ABWFR/DUNE_CD/C/DNCDPRG.EXE", "-f", "-m", "/mnt/c/mt32-rom-data", "-a", "MID330" };
        //}
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(newArgs, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
        ((IDisposable)Logger).Dispose();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}