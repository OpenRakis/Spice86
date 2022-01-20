namespace Spice86.UI;

using Avalonia;

using Serilog;
using Serilog.Events;

using System;
using System.Collections.Generic;
using System.Linq;

using Spice86.Emulator;
using System.Threading.Tasks;
using Avalonia.Controls;

/// <summary>
/// GUI entry point.<br/>
/// Responsible for setting up java-fx and starting the exe from the configuration provided in the command line.
/// </summary>
public class Spice86Application : Application {

    private static readonly ILogger _logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Debug) // restricted... is Optional
        .CreateLogger();

    private ProgramExecutor? _programExecutor;

    public static void RunWithOverrides<T>(String[] args, T overrides, string expectedChecksum) where T : class, new() {
        var argsList = args.ToList();

        // Inject override
        argsList.Add($"--overrideSupplierClassName={overrides.GetType().FullName}");
        argsList.Add($"--expectedChecksum={expectedChecksum}");
        // TODO: Call Main
        //Main(argsList.ToArray(new string[argsList.Count]));
    }

    public void Start() {
        Configuration? configuration = GenerateConfiguration();
        if (configuration == null) {
            Exit();
        }

        Gui gui = new Gui();
        gui.SetResolution(320, 200, 0);
        gui.SetTitle($"Spice86: {configuration?.GetExe()}");
        gui.SetOnCloseRequest((x) => Exit());
        gui.SetOnShown((@event) => StartMachineAsync(gui, configuration));
        gui.Show();
    }

    private void Exit() {
        _programExecutor?.Dispose();
        Environment.Exit(0);
    }

    private Configuration? GenerateConfiguration() {
        return new CommandLineParser().ParseCommandLine(Environment.GetCommandLineArgs());
    }

    private async Task StartMachineAsync(Gui gui, Configuration? configuration) {
        await Task.Factory.StartNew(() =>
        {
            try {
                _programExecutor = new ProgramExecutor(gui, configuration);
                _programExecutor.Run();
            } catch (Exception e) {
                _logger.Error(e, "An error occurred during execution");
            }

            Exit();
        });
    }
}