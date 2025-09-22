﻿namespace Spice86.ViewModels;

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
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using PhysicalKey = Spice86.Shared.Emulator.Keyboard.PhysicalKey;
using MouseButton = Spice86.Shared.Emulator.Mouse.MouseButton;
using Timer = System.Timers.Timer;

/// <inheritdoc cref="Spice86.Shared.Interfaces.IGuiVideoPresentation" />
public sealed partial class MainWindowViewModel : ViewModelWithErrorDialog, IGuiVideoPresentation,
    IGuiMouseEvents, IGuiKeyboardEvents, IDisposable {
    private const double ScreenRefreshHz = 60;
    private readonly ILoggerService _loggerService;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly IPauseHandler _pauseHandler;
    private readonly ITimeMultiplier _pit;
    private readonly ICyclesLimiter _cyclesLimiter;
    private readonly PerformanceViewModel _performanceViewModel;
    private readonly IExceptionHandler _exceptionHandler;

    private int _targetCyclesPerMs;

    public int TargetCyclesPerMs {
        get => _cyclesLimiter.TargetCpuCyclesPerMs;
        set {
            if(SetProperty(ref _targetCyclesPerMs, value)) {
                _cyclesLimiter.TargetCpuCyclesPerMs = value;
            }
        }
    }

    [RelayCommand]
    private void IncreaseTargetCycles() {
        _cyclesLimiter.IncreaseCycles();
        TargetCyclesPerMs = _cyclesLimiter.TargetCpuCyclesPerMs;
    }

    [RelayCommand]
    private void DecreaseTargetCycles() {
        _cyclesLimiter.DecreaseCycles();
        TargetCyclesPerMs = _cyclesLimiter.TargetCpuCyclesPerMs;
    }

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
        ITimeMultiplier pit, IUIDispatcher uiDispatcher,
        IHostStorageProvider hostStorageProvider, ITextClipboard textClipboard,
        Configuration configuration, ILoggerService loggerService,
        IPauseHandler pauseHandler, PerformanceViewModel performanceViewModel,
        IExceptionHandler exceptionHandler, ICyclesLimiter cyclesLimiter)
        : base(uiDispatcher, textClipboard) {
        _pit = pit;
        _performanceViewModel = performanceViewModel;
        _exceptionHandler = exceptionHandler;
        Configuration = configuration;
        _loggerService = loggerService;
        _hostStorageProvider = hostStorageProvider;
        _cyclesLimiter = cyclesLimiter;
        TargetCyclesPerMs = _cyclesLimiter.TargetCpuCyclesPerMs;
        _pauseHandler = pauseHandler;
        _pauseHandler.Paused += OnPaused;
        _pauseHandler.Resumed += OnResumed;
        TimeMultiplier = Configuration.TimeMultiplier;
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromSeconds(1.0 / 30.0),
            DispatcherPriority.Background,
            (_, _) => RefreshMainTitleWithInstructionsPerMs());
    }

    private void RefreshMainTitleWithInstructionsPerMs() {
        if (IsPaused) {
            return;
        }
        SetMainTitle(_performanceViewModel.InstructionsPerMillisecond);
    }

    [RelayCommand]
    public void SetLogLevelToSilent() => SetLogLevel("Silent");

    [RelayCommand]
    public void SetLogLevelToVerbose() => SetLogLevel("Verbose");

    [RelayCommand]
    public void SetLogLevelToDebug() => SetLogLevel("Debug");

    [RelayCommand]
    public void SetLogLevelToInformation() => SetLogLevel("Information");

    [RelayCommand]
    public void SetLogLevelToWarning() => SetLogLevel("Warning");

    [RelayCommand]
    public void SetLogLevelToError() => SetLogLevel("Error");

    [RelayCommand]
    public void SetLogLevelToFatal() => SetLogLevel("Fatal");

    internal void OnMainWindowClosing() => _isAppClosing = true;

    internal void OnKeyUp(KeyEventArgs e) {
        if (_pauseHandler.IsPaused) {
            return;
        }
        
        // Pass only the Avalonia Key - no scancode conversion here
        KeyUp?.Invoke(this, new KeyboardEventArgs((PhysicalKey)e.Key, IsPressed: false));
    }

    [RelayCommand]
    private async Task SaveBitmap() {
        if (Bitmap is not null) {
            await _hostStorageProvider.SaveBitmapFile(Bitmap);
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

    private double? _scale = 1;

    public double? Scale {
        get => _scale;
        set {
            ValidateRequiredPropertyIsNotNull(value);
            SetProperty(ref _scale, Math.Max(value ?? 0, 1));
        }
    }

    [ObservableProperty]
    private Cursor? _cursor = Cursor.Default;

    [ObservableProperty]
    private WriteableBitmap? _bitmap;

    internal event Action? InvalidateBitmap;

    internal void OnKeyDown(KeyEventArgs e) {
        if (_pauseHandler.IsPaused) {
            return;
        }
        
        // Pass only the Avalonia Key - no scancode conversion here
        KeyDown?.Invoke(this, new KeyboardEventArgs((PhysicalKey)e.PhysicalKey, IsPressed: true));
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
        if (_pauseHandler.IsPaused) {
            return;
        }
        Avalonia.Input.MouseButton mouseButton = @event.GetCurrentPoint(image).Properties.PointerUpdateKind.GetMouseButton();
        MouseButtonDown?.Invoke(this, new MouseButtonEventArgs((MouseButton)mouseButton, true));
    }

    public void OnMouseButtonUp(PointerReleasedEventArgs @event, Image image) {
        if(_pauseHandler.IsPaused) {
            return;
        }
        Avalonia.Input.MouseButton mouseButton = @event.GetCurrentPoint(image).Properties.PointerUpdateKind.GetMouseButton();
        MouseButtonUp?.Invoke(this, new MouseButtonEventArgs((MouseButton)mouseButton, false));
    }

    public void OnMouseMoved(PointerEventArgs @event, Image image) {
        if (image.Source is null || _pauseHandler.IsPaused) {
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
        }, DispatcherPriority.Background);
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
        await _hostStorageProvider.DumpEmulatorStateToFile();
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    public void Pause() => _pauseHandler.RequestPause("Pause button pressed in main window");

    private void SetMainTitle(double instructionsPerMillisecond) {
        MainTitle = $"{nameof(Spice86)} {Configuration.Exe} - cycles/ms: {instructionsPerMillisecond,7:N0}";
    }

    [ObservableProperty]
    private string? _mainTitle;

    private double? _timeMultiplier = 1;

    public double? TimeMultiplier {
        get => _timeMultiplier;
        set {
            if(IsPaused) {
                return;
            }
            ValidateRequiredPropertyIsNotNull(value);
            SetProperty(ref _timeMultiplier, value);
            if (value is not null) {
                _pit?.SetTimeMultiplier(value.Value);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    public void Play() => _pauseHandler.Resume();

    [RelayCommand]
    public void ResetTimeMultiplier() => TimeMultiplier = Configuration.TimeMultiplier;

    private bool CanPause() => IsEmulatorRunning && !IsPaused;

    private bool CanPlay() => IsEmulatorRunning && IsPaused;

    private void InitializeRenderingTimer() {
        if (_renderingTimerInitialized) {
            return;
        }
        _renderingTimerInitialized = true;
        _drawTimer.Elapsed += (_, _) => DrawScreen();
        _drawTimer.Start();
    }

    private void DrawScreen() {
        if (_disposed || _pauseHandler.IsPaused || _isSettingResolution ||
            _isAppClosing || Bitmap is null || RenderScreen is null) {
            return;
        }
        _drawingSemaphoreSlim?.Wait();
        try {
            using ILockedFramebuffer pixels = Bitmap.Lock();
            var uiRenderEventArgs = new UIRenderEventArgs(pixels.Address, pixels.RowBytes * pixels.Size.Height / 4);
            RenderScreen.Invoke(this, uiRenderEventArgs);
            _uiDispatcher.Post(() => InvalidateBitmap?.Invoke(), DispatcherPriority.Background);
        } finally {
            if (!_disposed) {
                _drawingSemaphoreSlim?.Release();
            }
        }
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
        SetMainTitle(_performanceViewModel.InstructionsPerMillisecond);
        StartEmulatorThread();
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    internal event Action? Disposing;
    
    private void Dispose(bool disposing) {
        if (!_disposed) {
            _disposed = true;
            if (disposing) {
                // Unsubscribe from PauseHandler events
                _pauseHandler.Paused -= OnPaused;
                _pauseHandler.Resumed -= OnResumed;
                _pauseHandler.Dispose();

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
                Disposing?.Invoke();

                if (_emulatorThread?.IsAlive == true) {
                    _emulatorThread.Join();
                }
            }
        }
    }

    private void OnResumed() => _uiDispatcher.Post(() => IsPaused = false, DispatcherPriority.Background);

    private void OnPaused() => _uiDispatcher.Post(() => IsPaused = true, DispatcherPriority.Background);

    [ObservableProperty]
    private string _currentLogLevel = "";
    
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
        _uiDispatcher.Post(() => IsEmulatorRunning = true);
        _uiDispatcher.Post(() => StatusMessage = "Emulator started.");
        _emulatorThread.Start();
    }

    private void OnEmulatorErrorOccured(Exception e) {
        _exceptionHandler.Handle(e);
        _uiDispatcher.Post(() => {
            StatusMessage = "Emulator crashed.";
            ShowError(e);
        });
    }

    public event Action? UserInterfaceInitialized;

    private void EmulatorThread() {
        try {
            UserInterfaceInitialized?.Invoke();
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Emulation exited. Closing main window...");
            }
            _uiDispatcher.Post(() => CloseMainWindow?.Invoke(this, EventArgs.Empty));
        } catch (Exception e) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(e, "An error occurred during execution");
            }
            OnEmulatorErrorOccured(e);
        } finally {
            _uiDispatcher.Post(() => IsEmulatorRunning = false);
            _uiDispatcher.Post(() => StatusMessage = "Emulator: stopped.");
            _uiDispatcher.Post(() => AsmOverrideStatus = "");
        }
    }
}