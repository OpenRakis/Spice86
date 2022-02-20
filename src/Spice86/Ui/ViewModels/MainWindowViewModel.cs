namespace Spice86.UI.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

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
    private Configuration? _configuration;
    private bool _disposedValue;
    private Thread? _emulatorThread;
    private bool _isSettingResolution = false;
    private readonly List<Key> _keysPressed = new();
    private Action? _onKeyPressedEvent;
    private Action? _onKeyReleasedEvent;
    private ProgramExecutor? _programExecutor;
    private AvaloniaList<VideoBufferViewModel> _videoBuffers = new();

    public MainWindowViewModel() {
        if (Design.IsDesignMode) {
            return;
        }
    }

    public void SetConfiguration(string[] args) {
        Configuration? configuration = GenerateConfiguration(args);
        _configuration = configuration;
        if (configuration is null) {
            Exit();
        }
        MainTitle = $"{nameof(Spice86)} {configuration?.Exe}";
    }

    public string? MainTitle { get; private set; }

    public AvaloniaList<VideoBufferViewModel> VideoBuffers {
        get => _videoBuffers;
        set => this.RaiseAndSetIfChanged(ref _videoBuffers, value);
    }

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false) {
        var videoBuffer = new VideoBufferViewModel(this, scale, bufferWidth, bufferHeight, address, VideoBuffers.Count, isPrimaryDisplay);
        Dispatcher.UIThread.Post(() => {
            VideoBuffers.Add(videoBuffer);
        }, DispatcherPriority.MaxValue);
        
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
            videoBuffer.Draw(memory, palette);
        }
    }

    public void Exit() {
        Dispatcher.UIThread.Post(() => {
            if (Design.IsDesignMode) {
                return;
            }
            this.Dispose();
        }, DispatcherPriority.MaxValue);
        Environment.Exit(0);
    }

    public int Height {get; private set;}

    public Key? LastKeyCode { get; private set; }

    public int MouseX { get; set; }

    public int MouseY { get; set; }

    public IDictionary<uint, VideoBufferViewModel> VideoBuffersAsDictionary => VideoBuffers.ToDictionary(x => x.Address, x => x);

    public int Width { get; private set; }

    public bool IsKeyPressed(Key keyCode) {
        return _keysPressed.Contains(keyCode);
    }

    public bool IsLeftButtonClicked { get; private set; }


    public bool IsRightButtonClicked { get; private set; }

    public void OnKeyPressed(KeyEventArgs @event) {
        Key keyCode = @event.Key;
        if (!_keysPressed.Contains(keyCode)) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Key pressed {@KeyPressed}", keyCode);
            }
            _keysPressed.Add(keyCode);
            LastKeyCode = keyCode;
            RunOnKeyEvent(this._onKeyPressedEvent);
        }
    }

    public void OnKeyReleased(KeyEventArgs @event) {
        LastKeyCode = @event.Key;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Key released {@LastKeyCode}", LastKeyCode);
        }
        _keysPressed.Remove(LastKeyCode.Value);
        RunOnKeyEvent(this._onKeyReleasedEvent);
    }

    public void OnMainWindowOpened(object? sender, EventArgs e) {
        if (sender is Window) {
            _emulatorThread = new Thread(RunMachine) {
                Name = "Emulator"
            };
            _emulatorThread.Start();
        }
    }

    public void OnMouseClick(PointerEventArgs @event, bool click) {
        if (@event.Pointer.IsPrimary) {
            IsLeftButtonClicked = click;
        }

        if (@event.Pointer.IsPrimary == false) {
            IsRightButtonClicked = click;
        }
    }

    public void OnMouseMoved(PointerEventArgs @event, Image image) {
        MouseX = (int)@event.GetPosition(image).X;
        MouseY = (int)@event.GetPosition(image).Y;
    }

    public void RemoveBuffer(uint address) {
        VideoBuffers.Remove(VideoBuffers.First(x => x.Address == address));
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
        Width = width;
        Height = height;
        AddBuffer(address, 1, width, height, true);
        _isSettingResolution = false;
    }

    private void DisposeBuffers() {
        foreach (VideoBufferViewModel buffer in VideoBuffers) {
        Dispatcher.UIThread.Post(() => {
            buffer.Dispose();
        }, DispatcherPriority.MaxValue);
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

    private static Configuration? GenerateConfiguration(string[] args) {
        return CommandLineParser.ParseCommandLine(args);
    }

    private static void RunOnKeyEvent(Action? runnable) {
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