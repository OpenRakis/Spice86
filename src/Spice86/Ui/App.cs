namespace Spice86.UI;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Serilog;
using Serilog.Events;

using Spice86.CLI;
using Spice86.Emulator;

using System;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// GUI entry point.<br/>
/// Responsible for setting up AvaloniaUI, Serilog, and starting the exe from the configuration provided in the command line.
/// </summary>
public partial class App {

    private static readonly ILogger _logger = Log.Logger.ForContext<App>();

    private ProgramExecutor? _programExecutor;

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
        Start();
    }

    public void Start() {
        if(Design.IsDesignMode) {
            return;
        }
        Configuration? configuration = GenerateConfiguration();
        if (configuration == null) {
            Exit();
        }

        Gui gui = new();
        gui.SetResolution(320, 200, 0);
        gui.SetTitle($"{nameof(Spice86)} {configuration?.Exe}");
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