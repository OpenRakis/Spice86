namespace Spice86.UI.ViewModels;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Serilog;

using Spice86.CLI;
using Spice86.Emulator;
using Spice86.Emulator.Devices.Video;
using Spice86.UI.Views;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class MainWindowViewModel : ViewModelBase, IVideoKeyboardMouseIO {
    private static readonly ILogger _logger = Log.Logger.ForContext<MainWindowViewModel>();
    private ProgramExecutor? _programExecutor;
    private readonly Configuration? _configuration;
    private readonly Gui _gui;

    public MainWindowViewModel(MainWindow window) {
        _gui = window.FindControl<Gui>(nameof(Gui));
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

    /// <summary>
    /// async void in the only case where an exception won't be silenced and crash the process : an event handler.
    /// See: https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md
    /// </summary>
    private async void OnMainWindowOpened(object? sender, EventArgs e) {
        if(sender is Window) { 
            await StartMachineAsync(_configuration);
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

    private async Task StartMachineAsync(Configuration? configuration) {
        await Task.Factory.StartNew(() => {
            try {
                _programExecutor = new ProgramExecutor(this, configuration);
                _programExecutor.Run();
            } catch (Exception e) {
                _logger.Error(e, "An error occurred during execution");
            }
            Exit();
        });
    }

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, Action<IInputElement>? canvasPostSetupAction) {
        Dispatcher.UIThread.Post(() => { _gui.AddBuffer(address, scale, bufferWidth, bufferHeight, canvasPostSetupAction); }, DispatcherPriority.MaxValue);

    }

    public void Draw(byte[] memory, Rgb[] palette) {
        Dispatcher.UIThread.Post(() => { _gui.Draw(memory, palette); }, DispatcherPriority.MaxValue);
    }

    public void RemoveBuffer(uint address) {
        Dispatcher.UIThread.Post(() => { _gui.RemoveBuffer(address); }, DispatcherPriority.MaxValue);

    }

    public void SetMouseX(int mouseX) {
        Dispatcher.UIThread.Post(() => { _gui.SetMouseX(mouseX); }, DispatcherPriority.MaxValue);
    }

    public void SetMouseY(int mouseY) {
        Dispatcher.UIThread.Post(() => { _gui.SetMouseY(mouseY); }, DispatcherPriority.MaxValue);
    }

    public void SetOnKeyPressedEvent(Action onKeyPressedEvent) {
        Dispatcher.UIThread.Post(() => { _gui.SetOnKeyPressedEvent(onKeyPressedEvent); }, DispatcherPriority.MaxValue);
    }

    public void SetOnKeyReleasedEvent(Action onKeyReleasedEvent) {
        Dispatcher.UIThread.Post(() => { _gui.SetOnKeyReleasedEvent(onKeyReleasedEvent); }, DispatcherPriority.MaxValue);
    }

    public void SetResolution(int width, int height, uint address) {
        Dispatcher.UIThread.Post(() => { _gui.SetResolution(width, height, address); }, DispatcherPriority.MaxValue);
    }

    // FIXME: Sync over Async below (see https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md )
    // Should probably use XAML binding and GetValue/SetValue instead (AvaloniaUI / ReactiveUI).
    public int GetHeight() {
        int value = Dispatcher.UIThread.InvokeAsync(() => _gui.GetHeight()).GetAwaiter().GetResult();
        return value;
    }

    public Key? GetLastKeyCode() {
        Key? value = Dispatcher.UIThread.InvokeAsync(() => _gui.GetLastKeyCode()).GetAwaiter().GetResult();
        return value;
    }

    public int GetMouseX() {
        int value = Dispatcher.UIThread.InvokeAsync(() => _gui.GetMouseX()).GetAwaiter().GetResult();
        return value;
    }

    public int GetMouseY() {
        int value = Dispatcher.UIThread.InvokeAsync(() => _gui.GetMouseY()).GetAwaiter().GetResult();
        return value;
    }

    public IDictionary<uint, VideoBuffer> GetVideoBuffers() {
        IDictionary<uint, VideoBuffer> value = Dispatcher.UIThread.InvokeAsync(() => _gui.GetVideoBuffers()).GetAwaiter().GetResult();
        return value;
    }

    public int GetWidth() {
        int value = Dispatcher.UIThread.InvokeAsync(() => _gui.GetWidth()).GetAwaiter().GetResult();
        return value;
    }

    public bool IsKeyPressed(Key keyCode) {
        bool value = Dispatcher.UIThread.InvokeAsync(() => _gui.IsKeyPressed(keyCode)).GetAwaiter().GetResult();
        return value;
    }

    public bool IsLeftButtonClicked() {
        bool value = Dispatcher.UIThread.InvokeAsync(() => _gui.IsLeftButtonClicked()).GetAwaiter().GetResult();
        return value;
    }

    public bool IsRightButtonClicked() {
        bool value = Dispatcher.UIThread.InvokeAsync(() => _gui.IsRightButtonClicked()).GetAwaiter().GetResult();
        return value;
    }

}