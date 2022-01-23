namespace Spice86.UI.ViewModels;

using Avalonia.Controls;

using Serilog;

using Spice86.CLI;
using Spice86.Emulator;
using Spice86.UI.Views;

using System;
using System.Threading.Tasks;

public class MainWindowViewModel : ViewModelBase {
    private static readonly ILogger _logger = Log.Logger.ForContext<MainWindowViewModel>();
    private ProgramExecutor? _programExecutor;
    private readonly Configuration? _configuration;

    public MainWindowViewModel(MainWindow window) {
        if (Design.IsDesignMode) {
            return;
        }
        Configuration? configuration = GenerateConfiguration();
        _configuration = configuration;
        if (configuration == null) {
            Exit();
        }
        MainTitle = $"{nameof(Spice86)} {configuration?.Exe}";
        window.Closed += (s, e) => Exit();

        window.Opened += OnMainWindowOpened;
    }

    private void OnMainWindowOpened(object? sender, EventArgs e) {
        if(sender is Window mainWindow) { 
            Gui gui = mainWindow.FindControl<Gui>("Gui");
            StartMachine(gui, _configuration);
        }
    }

    public string? MainTitle { get; private set; }

    internal static MainWindowViewModel Create(MainWindow mainWindow) {
        return new MainWindowViewModel(mainWindow);
    }

    private void Exit() {
        if (Design.IsDesignMode) {
            return;
        }
        _programExecutor?.Dispose();
        Environment.Exit(0);
    }

    private Configuration? GenerateConfiguration() {
        return new CommandLineParser().ParseCommandLine(Environment.GetCommandLineArgs());
    }

    private void StartMachine(Gui gui, Configuration? configuration) {
        try {
            _programExecutor = new ProgramExecutor(gui, configuration);
            _programExecutor.Run();
        } catch (Exception e) {
            _logger.Error(e, "An error occurred during execution");
        }
        Exit();
    }
}