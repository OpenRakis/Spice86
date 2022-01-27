namespace Spice86.UI.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;

using ReactiveUI;

using Serilog;

using Spice86.CLI;
using Spice86.Emulator;
using Spice86.Emulator.Devices.Video;
using Spice86.UI.EventArgs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// GUI of the emulator.<br/>
/// <ul>
/// <li>Displays the content of the video ram (when the emulator requests it)</li>
/// <li>Communicates keyboard and mouse events to the emulator</li>
/// </ul>
/// </summary>
public class MainWindowViewModel : ViewModelBase, IVideoKeyboardMouseIO, IDisposable {
    private bool _leftButtonClicked;
    private bool _rightButtonClicked;
    private static readonly ILogger _logger = Log.Logger.ForContext<MainWindowViewModel>();
    private readonly Configuration? _configuration;
    private bool _disposedValue;
    private ProgramExecutor? _programExecutor;

    private Thread? _drawThread;

    private int height = 1;

    private List<Key> keysPressed = new();

    private Key? lastKeyCode = null;


    private int mainCanvasScale = 4;

    private int mouseX;

    private int mouseY;

    private Action? onKeyPressedEvent;

    private Action? onKeyReleasedEvent;

    private int width = 1;

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
        this.NextFrame += OnNextFrame;
        _drawThread = new Thread(DrawOnDedicatedThread);
        _drawThread.Start();
    }

    private void OnNextFrame(FrameEventArgs e) {
        Interlocked.Exchange(ref this.frame, e);
        this.nextFrame.Set();
    }

    private long _frameNumber = 0;

    public long FrameNumber {
        get { return _frameNumber; }
        set => this.RaiseAndSetIfChanged(ref _frameNumber, value);
    }

    public string? MainTitle { get; private set; }

    private AvaloniaList<VideoBufferViewModel> _videoBuffers = new();

    public AvaloniaList<VideoBufferViewModel> VideoBuffers {
        get => _videoBuffers;
        set => this.RaiseAndSetIfChanged(ref _videoBuffers, value);
    }

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight) {
        VideoBufferViewModel videoBuffer = new VideoBufferViewModel(this, bufferWidth, bufferHeight, scale, address, VideoBuffers.Count);
        VideoBuffers.Add(videoBuffer);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private readonly AutoResetEvent nextFrame = new AutoResetEvent(false);
    private void DrawOnDedicatedThread() {
        while (true) {
            nextFrame.WaitOne(1000);
            if (frame == null) {
                continue;
            }
            foreach (VideoBufferViewModel videoBuffer in frame.SortedBuffers) {
                {
                    videoBuffer.Draw(frame.Memory, frame.Palette);
                }
            }

        }
    }

    public void Draw(byte[] memory, Rgb[] palette) {
        FrameNumber++;
        this.NextFrame?.Invoke(new FrameEventArgs(memory, palette, FrameNumber, SortedBuffers()));
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

    public int GetHeight() {
        return height;
    }

    public Key? GetLastKeyCode() {
        return lastKeyCode;
    }

    public int GetMouseX() {
        return mouseX;
    }

    public int GetMouseY() {
        return mouseY;
    }

    public IDictionary<uint, VideoBufferViewModel> GetVideoBuffers() {
        return VideoBuffers.ToDictionary(x => x.Address, x => x);
    }

    public int GetWidth() {
        return width;
    }

    public bool IsKeyPressed(Key keyCode) {
        return keysPressed.Contains(keyCode);
    }

    public bool IsLeftButtonClicked() {
        return _leftButtonClicked;
    }

    public bool IsRightButtonClicked() {
        return _rightButtonClicked;
    }

    public void RemoveBuffer(uint address) {
        VideoBuffers.Remove(VideoBuffers.First(x => x.Address == address));
    }

    public void SetMouseX(int mouseX) {
        this.mouseX = mouseX;
    }

    public void SetMouseY(int mouseY) {
        this.mouseY = mouseY;
    }

    public void SetOnKeyPressedEvent(Action onKeyPressedEvent) {
        this.onKeyPressedEvent = onKeyPressedEvent;
    }

    public void SetOnKeyReleasedEvent(Action onKeyReleasedEvent) {
        this.onKeyReleasedEvent = onKeyReleasedEvent;
    }

    public void SetResolution(int width, int height, uint address) {
        this.width = width;
        this.height = height;
        AddBuffer(address, mainCanvasScale, width, height);
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                foreach (VideoBufferViewModel buffer in VideoBuffers) {
                    buffer.Dispose();
                }
            }
            _disposedValue = true;
        }
    }

    public void Exit() {
        if (Design.IsDesignMode) {
            return;
        }
        _programExecutor?.Dispose();
        Environment.Exit(0);
    }

    private Configuration? GenerateConfiguration() {
        return new CommandLineParser().ParseCommandLine(Environment.GetCommandLineArgs());
    }

    public void OnKeyPressed(KeyEventArgs @event) {
        Key keyCode = @event.Key;
        if (!keysPressed.Contains(keyCode)) {
            _logger.Information("Key pressed {@KeyPressed}", keyCode);
            keysPressed.Add(keyCode);
            this.lastKeyCode = keyCode;
            RunOnKeyEvent(this.onKeyPressedEvent);
        }
    }

    public void OnKeyReleased(KeyEventArgs @event) {
        this.lastKeyCode = @event.Key;
        _logger.Information("Key released {@LastKeyCode}", lastKeyCode);
        keysPressed.Remove(lastKeyCode.Value);
        RunOnKeyEvent(this.onKeyReleasedEvent);
    }

    /// <summary>
    /// async void in the only case where an exception won't be silenced and crash the process : an event handler.
    /// See: https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md
    /// </summary>
    public async void OnMainWindowOpened(object? sender, EventArgs e) {
        if (sender is Window) {
            await StartMachineAsync(_configuration);
        }
    }

    private void RunOnKeyEvent(Action? runnable) {
        if (runnable != null) {
            runnable.Invoke();
        }
    }

    private IEnumerable<VideoBufferViewModel> SortedBuffers() {
        return VideoBuffers.OrderBy(x => x.Address).Select(x => x);
    }

    private event NextFrameEventHandler? NextFrame;
    private FrameEventArgs? frame;

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
}