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
using Spice86.Core.Emulator.Mcp;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Emulator.Dos;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Native;
using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using MouseButton = Spice86.Shared.Emulator.Mouse.MouseButton;

/// <inheritdoc cref="Spice86.Shared.Interfaces.IGuiVideoPresentation" />
public sealed partial class MainWindowViewModel : ViewModelWithErrorDialog, IGuiVideoPresentation,
    IGuiMouseEvents, IGuiKeyboardEvents, IGuiJoystickEvents, IDisposable {
    private readonly SharedMouseData _sharedMouseData;
    private const double ScreenRefreshHz = 60;
    private readonly ILoggerService _loggerService;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly IPauseHandler _pauseHandler;
    private readonly ITimeMultiplier _pit;
    private readonly ICyclesLimiter _cyclesLimiter;
    private readonly PerformanceViewModel _performanceViewModel;
    private readonly IExceptionHandler _exceptionHandler;
    private readonly ICurrentProcessNameProvider _currentProcessNameProvider;

    private McpStatusViewModel? _mcpStatusViewModel;

    public McpStatusViewModel? McpStatusViewModel {
        get => _mcpStatusViewModel;
        set => SetProperty(ref _mcpStatusViewModel, value);
    }

    private int? _targetCyclesPerMs;

    public int? TargetCyclesPerMs {
        get => _cyclesLimiter.TargetCpuCyclesPerMs;
        set {
            if (SetProperty(ref _targetCyclesPerMs, value)) {
                _cyclesLimiter.TargetCpuCyclesPerMs = value ?? 100;
            }
        }
    }

    [ObservableProperty] private bool _showCyclesLimitingUI;

    [ObservableProperty]
    private bool _showHttp;

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

    [ObservableProperty] private Configuration _configuration;

    private bool _disposed;
    private bool _renderingTimerInitialized;
    private Thread? _emulatorThread;
    private bool _isSettingResolution;
    private bool _isAppClosing;

    private DispatcherTimer? _drawTimer;
    private DispatcherTimer? _joystickPollTimer;
    private DispatcherTimer? _joystickRescanTimer;
    private SdlJoystickInput? _joystickInput;

    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;
    public event EventHandler<UIRenderEventArgs>? RenderScreen;
    /// <inheritdoc />
    public event EventHandler<JoystickAxisEventArgs>? JoystickAxisChanged;
    /// <inheritdoc />
    public event EventHandler<JoystickButtonEventArgs>? JoystickButtonChanged;
    /// <inheritdoc />
    public event EventHandler<JoystickHatEventArgs>? JoystickHatChanged;
    /// <inheritdoc />
    public event EventHandler<JoystickConnectionEventArgs>? JoystickConnectionChanged;
    internal event EventHandler? CloseMainWindow;

    public sealed class MainWindowViewModelDependencies {
        public required SharedMouseData SharedMouseData { get; init; }
        public required ITimeMultiplier Pit { get; init; }
        public required IUIDispatcher UiDispatcher { get; init; }
        public required IHostStorageProvider HostStorageProvider { get; init; }
        public required ITextClipboard TextClipboard { get; init; }
        public required Configuration Configuration { get; init; }
        public required ILoggerService LoggerService { get; init; }
        public required IPauseHandler PauseHandler { get; init; }
        public required PerformanceViewModel PerformanceViewModel { get; init; }
        public required IExceptionHandler ExceptionHandler { get; init; }
        public required ICyclesLimiter CyclesLimiter { get; init; }
        public required EmulatorMcpServices McpServices { get; init; }
        public required int McpPort { get; init; }
        public required ICurrentProcessNameProvider CurrentProcessNameProvider { get; init; }
    }

    public MainWindowViewModel(MainWindowViewModelDependencies dependencies)
        : base(dependencies.UiDispatcher, dependencies.TextClipboard) {
        _sharedMouseData = dependencies.SharedMouseData;
        _pit = dependencies.Pit;
        _performanceViewModel = dependencies.PerformanceViewModel;
        _exceptionHandler = dependencies.ExceptionHandler;
        Configuration = dependencies.Configuration;
        _loggerService = dependencies.LoggerService;
        _hostStorageProvider = dependencies.HostStorageProvider;
        _cyclesLimiter = dependencies.CyclesLimiter;
        _currentProcessNameProvider = dependencies.CurrentProcessNameProvider;
        TargetCyclesPerMs = _cyclesLimiter.TargetCpuCyclesPerMs;
        _pauseHandler = dependencies.PauseHandler;
        IsPaused = _pauseHandler.IsPaused;
        _pauseHandler.Paused += OnPaused;
        _pauseHandler.Resumed += OnResumed;
        TimeMultiplier = Configuration.TimeMultiplier;
        _targetCyclesPerMs = _cyclesLimiter.TargetCpuCyclesPerMs;
        ShowHttp = Configuration.HttpApiPort is not 0;
        HttpApiPort = Configuration.HttpApiPort;
        ShowCyclesLimitingUI = _cyclesLimiter.TargetCpuCyclesPerMs is not 0;
        McpStatusViewModel = new McpStatusViewModel(dependencies.McpServices, dependencies.McpPort);
        McpStatusViewModel.StartNetworkMonitoring();
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => RefreshMainTitleWithInstructionsPerMs());
    }

    private void RefreshMainTitleWithInstructionsPerMs() {
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

    private void InitializeJoystickInput() {
        if (_joystickInput is not null) {
            return;
        }

        SdlJoystickInput input = new SdlJoystickInput();
        bool initialized;
        try {
            initialized = input.TryInitialize();
        } catch (InvalidOperationException) {
            input.Dispose();
            return;
        }

        if (!initialized) {
            input.Dispose();
            return;
        }

        input.JoystickAxisChanged += OnSdlJoystickAxis;
        input.JoystickButtonChanged += OnSdlJoystickButton;
        input.JoystickHatChanged += OnSdlJoystickHat;
        input.JoystickConnectionChanged += OnSdlJoystickConnection;
        _joystickInput = input;

        // Poll axes/buttons/hats at the refresh rate the gameport timing
        // already uses; rescan attached devices once per second to pick up
        // hot-plug events without flooding SDL with enumeration calls.
        _joystickPollTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(1000.0 / ScreenRefreshHz),
            DispatcherPriority.Input,
            (_, _) => _joystickInput?.Poll());
        _joystickPollTimer.Start();

        _joystickRescanTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => _joystickInput?.RescanDevices());
        _joystickRescanTimer.Start();
    }

    private void OnSdlJoystickAxis(object? sender, JoystickAxisEventArgs e) =>
        JoystickAxisChanged?.Invoke(this, e);

    private void OnSdlJoystickButton(object? sender, JoystickButtonEventArgs e) =>
        JoystickButtonChanged?.Invoke(this, e);

    private void OnSdlJoystickHat(object? sender, JoystickHatEventArgs e) =>
        JoystickHatChanged?.Invoke(this, e);

    private void OnSdlJoystickConnection(object? sender, JoystickConnectionEventArgs e) =>
        JoystickConnectionChanged?.Invoke(this, e);

    internal void OnMainWindowClosing() => _isAppClosing = true;

    internal void OnKeyUp(KeyEventArgs e) {
        if (_pauseHandler.IsPaused) {
            return;
        }

        // Use PhysicalKey from Avalonia which represents the physical keyboard location
        KeyUp?.Invoke(this,
            new KeyboardEventArgs((Shared.Emulator.Keyboard.PhysicalKey)e.PhysicalKey, IsPressed: false));
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

    /// <summary>
    /// The aspect ratio correction factor for the current video mode.
    /// Controls vertical scaling: 1.0 = square pixels, 1.2 = DOS VGA correction.
    /// </summary>
    [ObservableProperty]
    private double _aspectRatioCorrectionFactor = 1.0;

    [ObservableProperty] private Cursor? _cursor = Cursor.Default;

    [ObservableProperty] private WriteableBitmap? _bitmap;

    internal event Action? InvalidateBitmap;

    internal void OnKeyDown(KeyEventArgs e) {
        if (_pauseHandler.IsPaused) {
            return;
        }

        // Use PhysicalKey from Avalonia which represents the physical keyboard location
        KeyDown?.Invoke(this,
            new KeyboardEventArgs((Shared.Emulator.Keyboard.PhysicalKey)e.PhysicalKey, IsPressed: true));
    }

    [ObservableProperty] private string _statusMessage = "Emulator: not started.";

    [ObservableProperty] private string _asmOverrideStatus = "ASM Overrides: not used.";

    [ObservableProperty] private string _emulatorMouseCursorInfo = "?";

    [ObservableProperty] private int? _httpApiPort;

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

        Avalonia.Input.MouseButton mouseButton =
            @event.GetCurrentPoint(image).Properties.PointerUpdateKind.GetMouseButton();
        MouseButtonDown?.Invoke(this, new MouseButtonEventArgs((MouseButton)mouseButton, true));
    }

    public void OnMouseButtonUp(PointerReleasedEventArgs @event, Image image) {
        if (_pauseHandler.IsPaused) {
            return;
        }

        Avalonia.Input.MouseButton mouseButton =
            @event.GetCurrentPoint(image).Properties.PointerUpdateKind.GetMouseButton();
        MouseButtonUp?.Invoke(this, new MouseButtonEventArgs((MouseButton)mouseButton, false));
    }

    public void OnMouseMoved(PointerEventArgs @event, Image image) {
        if (image.Source is null || _pauseHandler.IsPaused) {
            return;
        }

        MouseX = @event.GetPosition(image).X / image.Source.Size.Width;
        MouseY = @event.GetPosition(image).Y / image.Source.Size.Height;
        MouseMoved?.Invoke(this, new MouseMoveEventArgs(MouseX, MouseY));
        UpdateShownEmulatorMouseCursorPosition();
    }

    /// <summary>
    /// Called by the SDL capture backend when in relative mouse mode.
    /// Accepts pre-normalised coordinates (0..1 range) instead of Avalonia pointer event data.
    /// </summary>
    internal void OnMouseMovedNormalized(double x, double y) {
        if (_pauseHandler.IsPaused) {
            return;
        }

        MouseX = x;
        MouseY = y;
        MouseMoved?.Invoke(this, new MouseMoveEventArgs(x, y));
        UpdateShownEmulatorMouseCursorPosition();
    }

    public void SetResolution(int width, int height) {
        _uiDispatcher.Post(() => {
            _isSettingResolution = true;
            if (Width != width || Height != height) {
                Width = width;
                Height = height;
                if (_disposed) {
                    return;
                }

                Bitmap?.Dispose();
                Bitmap = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888,
                    AlphaFormat.Opaque);
            }

            _isSettingResolution = false;
            UpdateShownEmulatorMouseCursorPosition();
            InitializeRenderingTimer();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Called when the emulated video mode changes.
    /// Updates the aspect ratio correction factor for proper display scaling.
    /// </summary>
    public void OnVideoModeChanged(object? sender, VideoModeChangedEventArgs e) {
        _uiDispatcher.Post(() => {
            AspectRatioCorrectionFactor = e.AspectRatioCorrectionFactor;

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug(
                    "Video mode changed to {Width}x{Height}, aspect ratio correction factor: {Factor}",
                    e.NewMode.Width,
                    e.NewMode.Height,
                    e.AspectRatioCorrectionFactor);
            }
        });
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
        string currentProgramName = _currentProcessNameProvider.CurrentProgramName;
        if (string.IsNullOrEmpty(currentProgramName)) {
            MainTitle = $"{nameof(Spice86)} {Configuration.Exe} - cycles/ms: {instructionsPerMillisecond,7:N0}{MouseCaptureHint}";
        } else {
            MainTitle = $"{nameof(Spice86)} {Configuration.Exe} - {currentProgramName} - cycles/ms: {instructionsPerMillisecond,7:N0}{MouseCaptureHint}";
        }
    }

    [ObservableProperty] private string? _mainTitle;

    [ObservableProperty] private bool _isMouseCaptured;

    [ObservableProperty] private string _mouseCaptureHint = string.Empty;

    internal void UpdateMouseCaptureHint(bool isCaptured) {
        IsMouseCaptured = isCaptured;
        if (isCaptured) {
            MouseCaptureHint = " | Mouse captured (middle click to release)";
        } else {
            MouseCaptureHint = " | Mouse free (middle click to capture)";
        }
        RefreshMainTitleWithInstructionsPerMs();
    }

    private double? _timeMultiplier = 1;

    public double? TimeMultiplier {
        get => _timeMultiplier;
        set {
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
        _drawTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / ScreenRefreshHz),
            DispatcherPriority.Render, (_, _) => DrawScreen());
        _drawTimer.Start();
    }

    private void DrawScreen() {
        if (_disposed || _pauseHandler.IsPaused || _isSettingResolution ||
            _isAppClosing || Bitmap is null || RenderScreen is null) {
            return;
        }

        using ILockedFramebuffer pixels = Bitmap.Lock();
        UIRenderEventArgs uiRenderEventArgs = new UIRenderEventArgs(pixels.Address, pixels.RowBytes * pixels.Size.Height / 4);
        RenderScreen.Invoke(this, uiRenderEventArgs);
        InvalidateBitmap?.Invoke();
    }

    internal void StartEmulator() {
        if (string.IsNullOrWhiteSpace(Configuration.Exe) ||
            string.IsNullOrWhiteSpace(Configuration.CDrive)) {
            return;
        }

        StatusMessage = "Emulator starting...";
        AsmOverrideStatus = Configuration switch {
            { UseCodeOverrideOption: true, OverrideSupplier: not null } => "ASM code overrides: enabled.",
            { UseCodeOverride: false, OverrideSupplier: not null } =>
                "ASM code overrides: only functions names will be referenced.",
            _ => "ASM code overrides: none."
        };
        SetLogLevel(Configuration.SilencedLogs ? "Silent" : _loggerService.LogLevelSwitch.MinimumLevel.ToString());
        SetMainTitle(_performanceViewModel.InstructionsPerMillisecond);
        InitializeJoystickInput();
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

                _drawTimer?.Stop();
                _drawTimer = null;

                _joystickPollTimer?.Stop();
                _joystickPollTimer = null;
                _joystickRescanTimer?.Stop();
                _joystickRescanTimer = null;
                if (_joystickInput is not null) {
                    _joystickInput.JoystickAxisChanged -= OnSdlJoystickAxis;
                    _joystickInput.JoystickButtonChanged -= OnSdlJoystickButton;
                    _joystickInput.JoystickHatChanged -= OnSdlJoystickHat;
                    _joystickInput.JoystickConnectionChanged -= OnSdlJoystickConnection;
                    _joystickInput.Dispose();
                    _joystickInput = null;
                }

                // Dispose of UI-related resources in the UI thread
                _uiDispatcher.Post(() => {
                    Bitmap?.Dispose();
                    Cursor?.Dispose();
                }, DispatcherPriority.MaxValue);

                PlayCommand.Execute(null);
                IsEmulatorRunning = false;
                Disposing?.Invoke();

                if (_emulatorThread?.IsAlive == true) {
                    _emulatorThread.Join();
                }
            }
        }
    }

    private void OnResumed() => _uiDispatcher.Post(() => {
        IsPaused = false;
        RefreshMainTitleWithInstructionsPerMs();
    }, DispatcherPriority.Background);

    private void OnPaused() => _uiDispatcher.Post(() => {
        IsPaused = true;
        RefreshMainTitleWithInstructionsPerMs();
    }, DispatcherPriority.Background);

    [ObservableProperty] private string _currentLogLevel = "";

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

    private void UpdateShownEmulatorMouseCursorPosition() {
        MouseStatusRecord mouseDeviceStatus = _sharedMouseData.CurrentMouseStatus;
        EmulatorMouseCursorInfo = $"X: {mouseDeviceStatus.X} Y: {mouseDeviceStatus.Y}";
    }
}