namespace Spice86.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;

using Key = Spice86.Shared.Emulator.Keyboard.Key;
using MouseButton = Spice86.Shared.Emulator.Mouse.MouseButton;
using Timer = System.Timers.Timer;

/// <inheritdoc cref="Spice86.Shared.Interfaces.IGui" />
public sealed partial class MainWindowViewModel : ViewModelBaseWithErrorDialog, IGui, IDisposable {
    private const double ScreenRefreshHz = 60;
    private readonly ILoggerService _loggerService;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly IAvaloniaKeyScanCodeConverter _avaloniaKeyScanCodeConverter;
    private readonly IPauseHandler _pauseHandler;
    private readonly bool _isGdbServerRunning;
    private readonly ITimeMultiplier _pit;

    [ObservableProperty]
    private bool _canUseInternalDebugger;
    

    [ObservableProperty]
    private Configuration _configuration;

    private bool _disposed;
    private bool _renderingTimerInitialized;
    private Thread? _emulatorThread;
    private bool _isSettingResolution;
    private bool _isAppClosing;

    private readonly Timer _drawTimer = new(1000.0 / ScreenRefreshHz);
    private readonly SemaphoreSlim? _drawingSemaphoreSlim = new(1, 1);

    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;
    public event EventHandler<UIRenderEventArgs>? RenderScreen;
    internal event EventHandler? CloseMainWindow;

    public MainWindowViewModel(
        ITimeMultiplier pit, IUIDispatcher uiDispatcher, IHostStorageProvider hostStorageProvider, ITextClipboard textClipboard,
        Configuration configuration, ILoggerService loggerService, IPauseHandler pauseHandler) : base(textClipboard) {
        _pit = pit;
        _avaloniaKeyScanCodeConverter = new AvaloniaKeyScanCodeConverter();
        Configuration = configuration;
        _loggerService = loggerService;
        _hostStorageProvider = hostStorageProvider;
        _uiDispatcher = uiDispatcher;
        _isGdbServerRunning = configuration.GdbPort is not null;
        _pauseHandler = pauseHandler;
        _pauseHandler.Pausing += OnPausing;
        _pauseHandler.Resumed += OnResumed;
    }
    
    private IProgramExecutor? _programExecutor;

    internal IProgramExecutor? ProgramExecutor {
        get => _programExecutor;
        set {
            _programExecutor = value;
            Dispatcher.UIThread.Post(() => CanUseInternalDebugger = value is not null && !_isGdbServerRunning);
        }
    }
    
    [RelayCommand]
    public void SetLogLevelToSilent() {
        SetLogLevel("Silent");
    }

    [RelayCommand]
    public void SetLogLevelToVerbose() {
        SetLogLevel("Verbose");
    }

    [RelayCommand]
    public void SetLogLevelToDebug() {
        SetLogLevel("Debug");
    }

    [RelayCommand]
    public void SetLogLevelToInformation() {
        SetLogLevel("Information");
    }

    [RelayCommand]
    public void SetLogLevelToWarning() {
        SetLogLevel("Warning");
    }

    [RelayCommand]
    public void SetLogLevelToError() {
        SetLogLevel("Error");
    }

    [RelayCommand]
    public void SetLogLevelToFatal() {
        SetLogLevel("Fatal");
    }
    
    internal void OnMainWindowClosing() => _isAppClosing = true;

    internal void OnKeyUp(KeyEventArgs e) {
        KeyUp?.Invoke(this,
            new KeyboardEventArgs((Key)e.Key,
                false,
                _avaloniaKeyScanCodeConverter.GetKeyReleasedScancode((Key)e.Key),
                _avaloniaKeyScanCodeConverter.GetAsciiCode(
                    _avaloniaKeyScanCodeConverter.GetKeyReleasedScancode((Key)e.Key))));
    }

    [RelayCommand]
    private async Task SaveBitmap() {
        if (Bitmap is not null) {
            await _hostStorageProvider.SaveBitmapFile(Bitmap).ConfigureAwait(false);
        }
    }

    private bool _showCursor;

    public bool ShowCursor {
        get => _showCursor;
        set {
            SetProperty(ref _showCursor, value);
            if (_showCursor) {
                Cursor?.Dispose();
                Cursor = Cursor.Default;
            } else {
                Cursor?.Dispose();
                Cursor = new Cursor(StandardCursorType.None);
            }
        }
    }

    private double _scale = 1;

    public double Scale {
        get => _scale;
        set => SetProperty(ref _scale, Math.Max(value, 1));
    }

    [ObservableProperty]
    private Cursor? _cursor = Cursor.Default;

    [ObservableProperty]
    private WriteableBitmap? _bitmap;

    internal event Action? InvalidateBitmap;


    internal void OnKeyDown(KeyEventArgs e) {
        KeyDown?.Invoke(this,
            new KeyboardEventArgs((Key)e.Key,
                true,
                _avaloniaKeyScanCodeConverter.GetKeyPressedScancode((Key)e.Key),
                _avaloniaKeyScanCodeConverter.GetAsciiCode(
                    _avaloniaKeyScanCodeConverter.GetKeyPressedScancode((Key)e.Key))));
    }

    [ObservableProperty]
    private string _statusMessage = "Emulator: not started.";

    [ObservableProperty]
    private string _asmOverrideStatus = "ASM Overrides: not used.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool _isPaused;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public double MouseX { get; set; }

    public double MouseY { get; set; }
    
    public void OnMouseButtonDown(PointerPressedEventArgs @event, Image image) {
        Avalonia.Input.MouseButton mouseButton = @event.GetCurrentPoint(image).Properties.PointerUpdateKind.GetMouseButton();
        MouseButtonDown?.Invoke(this, new MouseButtonEventArgs((MouseButton)mouseButton, true));
    }

    public void OnMouseButtonUp(PointerReleasedEventArgs @event, Image image) {
        Avalonia.Input.MouseButton mouseButton = @event.GetCurrentPoint(image).Properties.PointerUpdateKind.GetMouseButton();
        MouseButtonUp?.Invoke(this, new MouseButtonEventArgs((MouseButton)mouseButton, false));
    }

    public void OnMouseMoved(PointerEventArgs @event, Image image) {
        if (image.Source is null) {
            return;
        }
        MouseX = @event.GetPosition(image).X / image.Source.Size.Width;
        MouseY = @event.GetPosition(image).Y / image.Source.Size.Height;
        MouseMoved?.Invoke(this, new MouseMoveEventArgs(MouseX, MouseY));
    }
    
    public void SetResolution(int width, int height) {
        _uiDispatcher.Post(() => {
            _isSettingResolution = true;
            Scale = 1;
            if (Width != width || Height != height) {
                Width = width;
                Height = height;
                if (_disposed) {
                    return;
                }
                _drawingSemaphoreSlim?.Wait();
                try {
                    Bitmap?.Dispose();
                    Bitmap = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                } finally {
                    if (!_disposed) {
                        _drawingSemaphoreSlim?.Release();
                    }
                }
            }
            _isSettingResolution = false;
            InitializeRenderingTimer();
        }, DispatcherPriority.MaxValue);
    }

    public void HideMouseCursor() => _uiDispatcher.Post(() => ShowCursor = false);

    public void ShowMouseCursor() => _uiDispatcher.Post(() => ShowCursor = true);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(DumpEmulatorStateToFileCommand))]
    private bool _isEmulatorRunning;

    [RelayCommand(CanExecute = nameof(IsEmulatorRunning))]
    private async Task DumpEmulatorStateToFile() {
        if (ProgramExecutor is null) {
            return;
        }
        await _hostStorageProvider.DumpEmulatorStateToFile(Configuration, ProgramExecutor).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    public void Pause() {
        _pauseHandler.RequestPause("Pause button pressed in main window");
    }

    private void SetMainTitle() => MainTitle = $"{nameof(Spice86)} {Configuration.Exe}";

    [ObservableProperty]
    private string? _mainTitle;

    private double _timeMultiplier = 1;

    public double? TimeMultiplier {
        get => _timeMultiplier;
        set {
            if (value is null) {
                return;
            }

            SetProperty(ref _timeMultiplier, value.Value);
            _pit?.SetTimeMultiplier(value.Value);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    public void Play() {
        _pauseHandler.Resume();
    }

    [RelayCommand]
    public void ResetTimeMultiplier() => TimeMultiplier = Configuration.TimeMultiplier;

    private bool CanPause() {
        return IsEmulatorRunning && !IsPaused;
    }

    private bool CanPlay() {
        return IsEmulatorRunning && IsPaused;
    }

    private void InitializeRenderingTimer() {
        if (_renderingTimerInitialized) {
            return;
        }
        _renderingTimerInitialized = true;
        _drawTimer.Elapsed += (_, _) => DrawScreen();
        _drawTimer.Start();
    }

    private void DrawScreen() {
        if (_disposed || _isSettingResolution || _isAppClosing || Bitmap is null || RenderScreen is null) {
            return;
        }
        _drawingSemaphoreSlim?.Wait();
        try {
            using ILockedFramebuffer pixels = Bitmap.Lock();
            var uiRenderEventArgs = new UIRenderEventArgs(pixels.Address, pixels.RowBytes * pixels.Size.Height / 4);
            RenderScreen.Invoke(this, uiRenderEventArgs);
        } finally {
            if (!_disposed) {
                _drawingSemaphoreSlim?.Release();
            }
        }
        _uiDispatcher.Post(() => InvalidateBitmap?.Invoke(), DispatcherPriority.Render);
    }

    internal void StartEmulator() {
        if (string.IsNullOrWhiteSpace(Configuration.Exe) ||
            string.IsNullOrWhiteSpace(Configuration.CDrive)) {
            return;
        }
        StatusMessage = "Emulator starting...";
        AsmOverrideStatus = Configuration switch {
            {UseCodeOverrideOption: true, OverrideSupplier: not null} => "ASM code overrides: enabled.",
            {UseCodeOverride: false, OverrideSupplier: not null} =>
                "ASM code overrides: only functions names will be referenced.",
            _ => "ASM code overrides: none."
        };
        SetLogLevel(Configuration.SilencedLogs ? "Silent" : _loggerService.LogLevelSwitch.MinimumLevel.ToString());
        SetMainTitle();
        StartEmulatorThread();
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    
    private void Dispose(bool disposing) {
        if (!_disposed) {
            _disposed = true;
            if (disposing) {
                // Unsubscribe from PauseHandler events
                _pauseHandler.Pausing -= OnPausing;
                _pauseHandler.Resumed -= OnResumed;

                _drawTimer.Stop();
                _drawTimer.Dispose();

                // Dispose of UI-related resources in the UI thread
                _uiDispatcher.Post(() => {
                    Bitmap?.Dispose();
                    Cursor?.Dispose();
                }, DispatcherPriority.MaxValue);

                _drawingSemaphoreSlim?.Dispose();

                PlayCommand.Execute(null);
                IsEmulatorRunning = false;

                DisposeEmulator();
                if (_emulatorThread?.IsAlive == true) {
                    _emulatorThread.Join();
                }
            }
        }
    }

    private void OnResumed() {
        Dispatcher.UIThread.Invoke(() => IsPaused = false, DispatcherPriority.Normal);
    }

    private void OnPausing() {
        Dispatcher.UIThread.Invoke(() => IsPaused = true, DispatcherPriority.Normal);
    }
    
    [ObservableProperty]
    private string _currentLogLevel = "";

    private void DisposeEmulator() {
        _programExecutor?.Dispose();
    }

    private void SetLogLevel(string logLevel) {
        if (logLevel == "Silent") {
            CurrentLogLevel = logLevel;
            _loggerService.AreLogsSilenced = true;
            _loggerService.LogLevelSwitch.MinimumLevel = LogEventLevel.Fatal;
        } else {
            _loggerService.AreLogsSilenced = false;
            _loggerService.LogLevelSwitch.MinimumLevel = Enum.Parse<LogEventLevel>(logLevel);
            CurrentLogLevel = logLevel;
        }
    }

    private void StartEmulatorThread() {
        _emulatorThread = new Thread(EmulatorThread) {
            Name = "Emulator"
        };
        _emulatorThread.Start();
    }

    private void OnEmulatorErrorOccured(Exception e) {
        _uiDispatcher.Post(() => {
            StatusMessage = "Emulator crashed.";
            ShowError(e);
        });
    }

    private void EmulatorThread() {
        try {
            try {
                StartProgramExecutor();
            } catch (Exception e) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error(e, "An error occurred during execution");
                }
                OnEmulatorErrorOccured(e);
            }
        } finally {
            _uiDispatcher.Post(() => IsEmulatorRunning = false);
            _uiDispatcher.Post(() => StatusMessage = "Emulator: stopped.");
            _uiDispatcher.Post(() => AsmOverrideStatus = "");
        }
    }

    private void StartProgramExecutor() {
        if (ProgramExecutor is null) {
            return;
        }
        TimeMultiplier = Configuration.TimeMultiplier;
        _uiDispatcher.Post(() => IsEmulatorRunning = true);
        _uiDispatcher.Post(() => StatusMessage = "Emulator started.");
        ProgramExecutor.Run();
        _uiDispatcher.Post(() => CloseMainWindow?.Invoke(this, EventArgs.Empty));
    }
}