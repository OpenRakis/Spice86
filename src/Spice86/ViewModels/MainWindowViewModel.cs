namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Emulator.Dos;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using System;
using System.ComponentModel;
using System.Threading.Tasks;

/// <summary>
/// Shell view model for the main window. Aggregates focused sub view models
/// (<see cref="EmulatorDisplayViewModel"/>, <see cref="EmulatorSession"/>,
/// <see cref="PerformanceViewModel"/>, <see cref="DrivesMenuViewModel"/>,
/// <see cref="McpStatusViewModel"/>) and owns the shell-level concerns:
/// pause state, log level, cycles limiter, time multiplier, title, and emulator-startup orchestration.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelWithErrorDialog, IDisposable {
    private readonly ILoggerService _loggerService;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly IPauseHandler _pauseHandler;
    private readonly ITimeMultiplier _pit;
    private readonly ICyclesLimiter _cyclesLimiter;
    private readonly PerformanceViewModel _performanceViewModel;
    private readonly ICurrentProcessNameProvider _currentProcessNameProvider;
    private bool _disposed;

    /// <summary>Display-surface child view model that owns the bitmap, cursor, and input dispatch.</summary>
    public EmulatorDisplayViewModel Display { get; }

    /// <summary>Lifecycle child view model that owns the emulator worker thread and run status.</summary>
    public EmulatorSession Session { get; }

    /// <summary>Performance child view model surfaced to XAML through <see cref="PerformanceViewModel"/>.</summary>
    public PerformanceViewModel PerformanceViewModel => _performanceViewModel;

    /// <summary>MCP status child view model surfaced to XAML.</summary>
    public McpStatusViewModel McpStatusViewModel { get; }

    /// <summary>Drives menu child view model surfaced to XAML, or <c>null</c> when no drive provider is available.</summary>
    public DrivesMenuViewModel? DrivesMenuViewModel { get; }

    /// <summary>Disc swapper used to advance drives to the next disc image on Ctrl-F4.</summary>
    public IDiscSwapper? DiscSwapper { get; }

    [ObservableProperty] private Configuration _configuration;

    [ObservableProperty] private string? _mainTitle;

    [ObservableProperty] private bool _isMouseCaptured;

    [ObservableProperty] private string _mouseCaptureHint = string.Empty;

    [ObservableProperty] private string _currentLogLevel = "";

    [ObservableProperty] private bool _showCyclesLimitingUI;

    [ObservableProperty] private bool _showHttp;

    [ObservableProperty] private int? _httpApiPort;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(DumpEmulatorStateToFileCommand))]
    private bool _isEmulatorRunning;

    private int? _targetCyclesPerMs;
    public int? TargetCyclesPerMs {
        get => _cyclesLimiter.TargetCpuCyclesPerMs;
        set {
            if (SetProperty(ref _targetCyclesPerMs, value)) {
                _cyclesLimiter.TargetCpuCyclesPerMs = value ?? 100;
            }
        }
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

    /// <summary>Fired when this view model is being disposed so the composition root can tear down.</summary>
    internal event Action? Disposing;

    public MainWindowViewModel(
        EmulatorDisplayViewModel display,
        EmulatorSession session,
        PerformanceViewModel performanceViewModel,
        McpStatusViewModel mcpStatusViewModel,
        DrivesMenuViewModel? drivesMenuViewModel,
        IDiscSwapper? discSwapper,
        Configuration configuration,
        IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard,
        ILoggerService loggerService,
        IPauseHandler pauseHandler,
        ITimeMultiplier pit,
        ICyclesLimiter cyclesLimiter,
        IHostStorageProvider hostStorageProvider,
        ICurrentProcessNameProvider currentProcessNameProvider)
        : base(uiDispatcher, textClipboard) {
        Display = display;
        Session = session;
        _performanceViewModel = performanceViewModel;
        McpStatusViewModel = mcpStatusViewModel;
        DrivesMenuViewModel = drivesMenuViewModel;
        DiscSwapper = discSwapper;
        Configuration = configuration;
        _loggerService = loggerService;
        _hostStorageProvider = hostStorageProvider;
        _pauseHandler = pauseHandler;
        _pit = pit;
        _cyclesLimiter = cyclesLimiter;
        _currentProcessNameProvider = currentProcessNameProvider;

        TimeMultiplier = Configuration.TimeMultiplier;
        _targetCyclesPerMs = _cyclesLimiter.TargetCpuCyclesPerMs;
        ShowHttp = Configuration.HttpApiPort is not 0;
        HttpApiPort = Configuration.HttpApiPort;
        ShowCyclesLimitingUI = _cyclesLimiter.TargetCpuCyclesPerMs is not 0;

        IsPaused = _pauseHandler.IsPaused;
        _pauseHandler.Paused += OnPaused;
        _pauseHandler.Resumed += OnResumed;

        Session.PropertyChanged += OnSessionPropertyChanged;

        McpStatusViewModel.StartNetworkMonitoring();

        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => RefreshMainTitleWithInstructionsPerMs());
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(EmulatorSession.IsRunning)) {
            _uiDispatcher.Post(() => IsEmulatorRunning = Session.IsRunning);
        }
    }

    internal async Task<bool> StartEmulatorAsync() {
        if (string.IsNullOrWhiteSpace(Configuration.Exe) ||
            string.IsNullOrWhiteSpace(Configuration.CDrive)) {
            return false;
        }

        string asmOverrideStatus = Configuration switch {
            { UseCodeOverrideOption: true, OverrideSupplier: not null } => "ASM code overrides: enabled.",
            { UseCodeOverride: false, OverrideSupplier: not null } =>
                "ASM code overrides: only functions names will be referenced.",
            _ => "ASM code overrides: none."
        };

        SetLogLevel(Configuration.SilencedLogs ? "Silent" : _loggerService.LogLevelSwitch.MinimumLevel.ToString());
        UpdateMainTitle(_performanceViewModel.InstructionsPerMillisecond);

        bool ok = await Session.StartAsync(asmOverrideStatus);
        if (!ok && Session.LastException is { } ex) {
            ShowError(ex);
        }
        return ok;
    }

    internal void OnMainWindowClosing() => Display.NotifyAppClosing();

    internal void UpdateMouseCaptureHint(bool isCaptured) {
        IsMouseCaptured = isCaptured;
        MouseCaptureHint = isCaptured
            ? " | Mouse captured (middle click to release)"
            : " | Mouse free (middle click to capture)";
        RefreshMainTitleWithInstructionsPerMs();
    }

    [RelayCommand] public void SetLogLevelToSilent() => SetLogLevel("Silent");
    [RelayCommand] public void SetLogLevelToVerbose() => SetLogLevel("Verbose");
    [RelayCommand] public void SetLogLevelToDebug() => SetLogLevel("Debug");
    [RelayCommand] public void SetLogLevelToInformation() => SetLogLevel("Information");
    [RelayCommand] public void SetLogLevelToWarning() => SetLogLevel("Warning");
    [RelayCommand] public void SetLogLevelToError() => SetLogLevel("Error");
    [RelayCommand] public void SetLogLevelToFatal() => SetLogLevel("Fatal");

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

    [RelayCommand(CanExecute = nameof(IsEmulatorRunning))]
    private async Task DumpEmulatorStateToFile() {
        await _hostStorageProvider.DumpEmulatorStateToFile();
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    public void Pause() => _pauseHandler.RequestPause("Pause button pressed in main window");

    [RelayCommand(CanExecute = nameof(CanPlay))]
    public void Play() => _pauseHandler.Resume();

    [RelayCommand]
    public void ResetTimeMultiplier() => TimeMultiplier = Configuration.TimeMultiplier;

    [RelayCommand]
    private async Task SaveBitmap() {
        if (Display.Bitmap is not null) {
            await _hostStorageProvider.SaveBitmapFile(Display.Bitmap);
        }
    }

    private bool CanPause() => IsEmulatorRunning && !IsPaused;
    private bool CanPlay() => IsEmulatorRunning && IsPaused;

    private void RefreshMainTitleWithInstructionsPerMs() {
        UpdateMainTitle(_performanceViewModel.InstructionsPerMillisecond);
    }

    private void UpdateMainTitle(double instructionsPerMillisecond) {
        string currentProgramName = _currentProcessNameProvider.CurrentProgramName;
        if (string.IsNullOrEmpty(currentProgramName)) {
            MainTitle = $"{nameof(Spice86)} {Configuration.Exe} - cycles/ms: {instructionsPerMillisecond,7:N0}{MouseCaptureHint}";
        } else {
            MainTitle = $"{nameof(Spice86)} {Configuration.Exe} - {currentProgramName} - cycles/ms: {instructionsPerMillisecond,7:N0}{MouseCaptureHint}";
        }
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

    private void OnResumed() => _uiDispatcher.Post(() => {
        IsPaused = false;
        RefreshMainTitleWithInstructionsPerMs();
    }, DispatcherPriority.Background);

    private void OnPaused() => _uiDispatcher.Post(() => {
        IsPaused = true;
        RefreshMainTitleWithInstructionsPerMs();
    }, DispatcherPriority.Background);

    public void Dispose() {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }
        _disposed = true;
        if (!disposing) {
            return;
        }

        _pauseHandler.Paused -= OnPaused;
        _pauseHandler.Resumed -= OnResumed;
        _pauseHandler.Dispose();
        Session.PropertyChanged -= OnSessionPropertyChanged;

        Display.Dispose();

        if (PlayCommand.CanExecute(null)) {
            PlayCommand.Execute(null);
        }
        IsEmulatorRunning = false;
        Disposing?.Invoke();

        Session.Dispose();
    }
}
