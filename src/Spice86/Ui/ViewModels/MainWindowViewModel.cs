namespace Spice86.UI.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;

using ReactiveUI;

using Serilog;

using Spice86.CLI;
using Spice86.Emulator;
using Spice86.Emulator.Devices.Video;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

/// <summary>
/// GUI of the emulator.<br/>
/// <ul>
/// <li>Displays the content of the video ram (when the emulator requests it)</li>
/// <li>Communicates keyboard and mouse events to the emulator</li>
/// </ul>
/// </summary>
public class MainWindowViewModel : ViewModelBase, IVideoKeyboardMouseIO, IDisposable {
    private static readonly ILogger _logger = Log.Logger.ForContext<MainWindowViewModel>();
    private readonly Configuration? _configuration;
    private bool _disposedValue;
    private Thread? _emulatorThread;
    private int _height = 1;
    private bool _isSettingResolution = false;
    private List<Key> _keysPressed = new();
    private Key? _lastKeyCode = null;
    private bool _leftButtonClicked;
    private int _mouseX;
    private int _mouseY;
    private Action? _onKeyPressedEvent;
    private Action? _onKeyReleasedEvent;
    private ProgramExecutor? _programExecutor;
    private bool _rightButtonClicked;
    private AvaloniaList<VideoBufferViewModel> _videoBuffers = new();
    private int _width = 1;

    public MainWindowViewModel() {
        if (Design.IsDesignMode) {
            return;
        }
        Configuration? configuration = GenerateConfiguration();
        _configuration = configuration;
        if (configuration == null) {
            Exit();
        }
        MainTitle = $"{nameof(Spice86)} {configuration?.Exe}";
        SetResolution(320, 200, 0);
    }

    public string? MainTitle { get; private set; }

    public AvaloniaList<VideoBufferViewModel> VideoBuffers {
        get => _videoBuffers;
        set => this.RaiseAndSetIfChanged(ref _videoBuffers, value);
    }

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false) {
        VideoBufferViewModel videoBuffer = new VideoBufferViewModel(this, bufferWidth, bufferHeight, address, VideoBuffers.Count, isPrimaryDisplay);
        VideoBuffers.Add(videoBuffer);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public void Draw(byte[] memory, Rgb[] palette) {
        if (_disposedValue || _isSettingResolution) {
            return;
        }
        foreach (VideoBufferViewModel videoBuffer in SortedBuffers()) {
            {
                videoBuffer.Draw(memory, palette);
            }
        }
    }

    public void Exit() {
        if (Design.IsDesignMode) {
            return;
        }
        this.Dispose();
        Environment.Exit(0);
    }

    public int GetHeight() {
        return _height;
    }

    public Key? GetLastKeyCode() {
        return _lastKeyCode;
    }

    public int GetMouseX() {
        return _mouseX;
    }

    public int GetMouseY() {
        return _mouseY;
    }

    public IDictionary<uint, VideoBufferViewModel> GetVideoBuffers() {
        return VideoBuffers.ToDictionary(x => x.Address, x => x);
    }

    public int GetWidth() {
        return _width;
    }

    public bool IsKeyPressed(Key keyCode) {
        return _keysPressed.Contains(keyCode);
    }

    public bool IsLeftButtonClicked() {
        return _leftButtonClicked;
    }

    public bool IsRightButtonClicked() {
        return _rightButtonClicked;
    }

    public void OnKeyPressed(KeyEventArgs @event) {
        Key keyCode = @event.Key;
        if (!_keysPressed.Contains(keyCode)) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Key pressed {@KeyPressed}", keyCode);
            }
            _keysPressed.Add(keyCode);
            this._lastKeyCode = keyCode;
            RunOnKeyEvent(this._onKeyPressedEvent);
        }
    }

    public void OnKeyReleased(KeyEventArgs @event) {
        this._lastKeyCode = @event.Key;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Key released {@LastKeyCode}", _lastKeyCode);
        }
        _keysPressed.Remove(_lastKeyCode.Value);
        RunOnKeyEvent(this._onKeyReleasedEvent);
    }

    public void OnMainWindowOpened(object? sender, EventArgs e) {
        if (sender is Window) {
            _emulatorThread = new Thread(RunMachine);
            _emulatorThread.Name = "Emulator";
            _emulatorThread.Start();
        }
    }

    public void OnMouseClick(PointerEventArgs @event, bool click) {
        if (@event.Pointer.IsPrimary) {
            _leftButtonClicked = click;
        }

        if (@event.Pointer.IsPrimary == false) {
            _rightButtonClicked = click;
        }
    }

    public void OnMouseMoved(PointerEventArgs @event, Image image) {
        SetMouseX((int)@event.GetPosition(image).X);
        SetMouseY((int)@event.GetPosition(image).Y);
    }

    public void RemoveBuffer(uint address) {
        VideoBuffers.Remove(VideoBuffers.First(x => x.Address == address));
    }

    public void SetMouseX(int mouseX) {
        this._mouseX = mouseX;
    }

    public void SetMouseY(int mouseY) {
        this._mouseY = mouseY;
    }

    public void SetOnKeyPressedEvent(Action onKeyPressedEvent) {
        this._onKeyPressedEvent = onKeyPressedEvent;
    }

    public void SetOnKeyReleasedEvent(Action onKeyReleasedEvent) {
        this._onKeyReleasedEvent = onKeyReleasedEvent;
    }

    public void SetResolution(int width, int height, uint address) {
        _isSettingResolution = true;
        DisposeBuffers();
        VideoBuffers = new();
        this._width = width;
        this._height = height;
        AddBuffer(address, 1, width, height, true);
        _isSettingResolution = false;
    }

    private void DisposeBuffers() {
        foreach (VideoBufferViewModel buffer in VideoBuffers) {
            buffer.Dispose();
        }
        _videoBuffers.Clear();
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                DisposeBuffers();
                _programExecutor?.Dispose();
            }
            _disposedValue = true;
        }
    }

    private Configuration? GenerateConfiguration() {
        return new CommandLineParser().ParseCommandLine(Environment.GetCommandLineArgs());
    }

    private void RunOnKeyEvent(Action? runnable) {
        if (runnable != null) {
            runnable.Invoke();
        }
    }

    private IEnumerable<VideoBufferViewModel> SortedBuffers() {
        return VideoBuffers.OrderBy(x => x.Address).Select(x => x);
    }

    private void RunMachine() {
        try {
            _programExecutor = new ProgramExecutor(this, _configuration);
            _programExecutor.Run();
        } catch (Exception e) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error(e, "An error occurred during execution");
            }
        }
        Exit();
    }
}