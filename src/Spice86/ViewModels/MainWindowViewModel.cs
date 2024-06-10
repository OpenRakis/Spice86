namespace Spice86.ViewModels;

using System.Threading;
using System.Diagnostics;

using Avalonia;

using Serilog.Events;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

using Key = Spice86.Shared.Emulator.Keyboard.Key;
using MouseButton = Spice86.Shared.Emulator.Mouse.MouseButton;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.InternalDebugger;

using Spice86.Interfaces;
using Spice86.Shared.Diagnostics;
using Spice86.Infrastructure;
using Spice86.Shared.Emulator.Video;

using Timer = System.Timers.Timer;

/// <inheritdoc cref="Spice86.Shared.Interfaces.IGui" />
public sealed partial class MainWindowViewModel : ViewModelBase, IPauseStatus, IGui, IDisposable {
    private const double ScreenRefreshHz = 60;
    private readonly ILoggerService _loggerService;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly IProgramExecutorFactory _programExecutorFactory;
    private readonly IUIDispatcherTimerFactory _uiDispatcherTimerFactory;
    private readonly IAvaloniaKeyScanCodeConverter? _avaloniaKeyScanCodeConverter;
    private IProgramExecutor? _programExecutor;
    private SoftwareMixer? _softwareMixer;
    private ITimeMultiplier? _pit;

    [ObservableProperty]
    private Configuration _configuration;
    
    [ObservableProperty]
    private DebugViewModel? _debugViewModel;
    
    private bool _disposed;
    private bool _renderingTimerInitialized;
    private Thread? _emulatorThread;
    private bool _isSettingResolution;
    private string _lastExecutableDirectory = string.Empty;
    private bool _closeAppOnEmulatorExit;
    private bool _isAppClosing;

    private static Action? _uiUpdateMethod;
    private readonly Timer _drawTimer = new(1000.0 / ScreenRefreshHz);
    private readonly SemaphoreSlim? _drawingSemaphoreSlim = new(1, 1);

    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;

    public MainWindowViewModel(IAvaloniaKeyScanCodeConverter avaloniaKeyScanCodeConverter, IProgramExecutorFactory programExecutorFactory, IUIDispatcher uiDispatcher, IHostStorageProvider hostStorageProvider, ITextClipboard textClipboard, IUIDispatcherTimerFactory uiDispatcherTimerFactory, Configuration configuration, ILoggerService loggerService) : base(textClipboard) {
        _avaloniaKeyScanCodeConverter = avaloniaKeyScanCodeConverter;
        Configuration = configuration;
        _programExecutorFactory = programExecutorFactory;
        _loggerService = loggerService;
        _hostStorageProvider = hostStorageProvider;
        _uiDispatcher = uiDispatcher;
        _uiDispatcherTimerFactory = uiDispatcherTimerFactory;
    }

    internal void OnMainWindowClosing() => _isAppClosing = true;

    internal void OnKeyUp(KeyEventArgs e) {
        if (_avaloniaKeyScanCodeConverter is null) {
            return;
        }
        KeyUp?.Invoke(this,
            new KeyboardEventArgs((Key)e.Key,
                false,
                _avaloniaKeyScanCodeConverter.GetKeyReleasedScancode((Key)e.Key),
                _avaloniaKeyScanCodeConverter.GetAsciiCode(
                    _avaloniaKeyScanCodeConverter.GetKeyReleasedScancode((Key)e.Key))));
    }

    [RelayCommand]
    public async Task SaveBitmap() {
        if (Bitmap is not null) {
            await _hostStorageProvider.SaveBitmapFile(Bitmap);
        }
    }
    
    private bool _showCursor = false;

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

    internal void OnKeyDown(KeyEventArgs e) {
        if (_avaloniaKeyScanCodeConverter is null) {
            return;
        }
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

    private bool _isPaused;
    
    public bool IsPaused {
        get => _isPaused;
        set {
            SetProperty(ref _isPaused, value);
            if (_softwareMixer is not null) {
                _softwareMixer.IsPaused = value;
            }
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartMostRecentlyUsedCommand))]
    private AvaloniaList<FileInfo> _mostRecentlyUsed = new();


    public int Width { get; private set; }

    public int Height { get; private set; }

    public void HideMouseCursor() => _uiDispatcher.Post(() => ShowCursor = false);

    public void ShowMouseCursor() => _uiDispatcher.Post(() => ShowCursor = true);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowPerformanceCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(DumpEmulatorStateToFileCommand))]
    private bool _isMachineRunning;

    [RelayCommand(CanExecute = nameof(IsMachineRunning))]
    public async Task DumpEmulatorStateToFile() {
        if (_programExecutor is not null) {
            await _hostStorageProvider.DumpEmulatorStateToFile(Configuration, _programExecutor);
        }
    }

    [RelayCommand(CanExecute = nameof(IsMachineRunning))]
    public void Pause() {
        if (_programExecutor is null) {
            return;
        }
        IsPaused = _programExecutor.IsPaused = true;
    }

    [RelayCommand(CanExecute = nameof(IsMachineRunning))]
    public void Play() {
        if (_programExecutor is null) {
            return;
        }

        IsPaused = _programExecutor.IsPaused = false;
    }

    private void SetMainTitle() => MainTitle = $"{nameof(Spice86)} {Configuration.Exe}";

    [ObservableProperty]
    private string? _mainTitle;

    [RelayCommand(CanExecute = nameof(CanStartMostRecentlyUsed))]
    public async Task StartMostRecentlyUsed(object? parameter) {
        int index = Convert.ToInt32(parameter);
        if (MostRecentlyUsed.Count > index) {
            await StartNewExecutable(MostRecentlyUsed[index].FullName);
        }
    }

    private bool CanStartMostRecentlyUsed() => MostRecentlyUsed.Count > 0;

    [RelayCommand]
    public async Task DebugExecutable() {
        _closeAppOnEmulatorExit = false;
        await StartNewExecutable();
        Pause();
    }

    [RelayCommand]
    public async Task StartExecutable(object? filePath) {
        _closeAppOnEmulatorExit = false;
        await StartNewExecutable(filePath as string);
    }

    private async Task StartNewExecutable(string? filePath = null) {
        if(!string.IsNullOrWhiteSpace(filePath) &&
            File.Exists(filePath)) {
            await RestartEmulatorWithNewProgram(filePath);
        }
        else if (_hostStorageProvider.CanOpen) {
            IStorageFile? file = await _hostStorageProvider.PickExecutableFile(_lastExecutableDirectory);
            if (file is not null) {
                filePath = file.Path.LocalPath;
                await RestartEmulatorWithNewProgram(filePath);
            }
        }
    }

    private async Task RestartEmulatorWithNewProgram(string filePath) {
        Configuration.Exe = filePath;
        Configuration.ExeArgs = "";
        Configuration.CDrive = Path.GetDirectoryName(Configuration.Exe);
        Configuration.UseCodeOverride = false;
        Play();
        await _uiDispatcher.InvokeAsync(DisposeEmulator, DispatcherPriority.MaxValue);
        IsMachineRunning = false;
        _closeAppOnEmulatorExit = false;
        RunEmulator();
    }

    private double _timeMultiplier = 1;

    public double? TimeMultiplier {
        get => _timeMultiplier;
        set {
            if (value is not null) {
                SetProperty(ref _timeMultiplier, value.Value);
                _pit?.SetTimeMultiplier(value.Value);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsMachineRunning))]
    public void ShowPerformance() => IsPerformanceVisible = !IsPerformanceVisible;
    
    [RelayCommand]
    public void ResetTimeMultiplier() => TimeMultiplier = Configuration.TimeMultiplier;

    private void InitializeRenderingTimer() {
        if (_renderingTimerInitialized) {
            return;
        }
        _renderingTimerInitialized = true;
        _drawTimer.Elapsed += (_, _) => DrawScreen();
        _drawTimer.Start();
    }

    private void DrawScreen() {
        if (_disposed || _isSettingResolution || _isAppClosing || _uiUpdateMethod is null || Bitmap is null || RenderScreen is null) {
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
        _uiDispatcher.Post(static () => _uiUpdateMethod.Invoke(), DispatcherPriority.Render);
    }

    public double MouseX { get; set; }
    public double MouseY { get; set; }

    public void OnMainWindowInitialized(Action uiUpdateMethod) {
        _uiUpdateMethod = uiUpdateMethod;
        if(RunEmulator()) {
            _closeAppOnEmulatorExit = true;
        }
    }

    [ObservableProperty]
    private string? _firstProgramName = "";

    [ObservableProperty]
    private string? _secondProgramName = "";

    [ObservableProperty]
    private string? _thirdProgramName = "";

    private void AddOrReplaceMostRecentlyUsed(string filePath) {
        if (MostRecentlyUsed.Any(x => x.FullName == filePath)) {
            return;
        }
        MostRecentlyUsed.Insert(0,new FileInfo(filePath));
        if (MostRecentlyUsed.Count > 3) {
            MostRecentlyUsed.RemoveAt(3);
        }
        FirstProgramName = MostRecentlyUsed.ElementAtOrDefault(0)?.Name;
        SecondProgramName = MostRecentlyUsed.ElementAtOrDefault(1)?.Name;
        ThirdProgramName = MostRecentlyUsed.ElementAtOrDefault(2)?.Name;
    }

    private bool RunEmulator() {
        if (string.IsNullOrWhiteSpace(Configuration.Exe) ||
            string.IsNullOrWhiteSpace(Configuration.CDrive)) {
            return false;
        }
        AddOrReplaceMostRecentlyUsed(Configuration.Exe);
        _lastExecutableDirectory = Configuration.CDrive;
        StatusMessage = "Emulator starting...";
        if (Configuration is {UseCodeOverrideOption: true, OverrideSupplier: not null}) {
            AsmOverrideStatus = "ASM code overrides: enabled.";
        } else if(Configuration is {UseCodeOverride: false, OverrideSupplier: not null}) {
            AsmOverrideStatus = "ASM code overrides: only functions names will be referenced.";
        } else {
            AsmOverrideStatus = "ASM code overrides: none.";
        }
        SetLogLevel(Configuration.SilencedLogs ? "Silent" : _loggerService.LogLevelSwitch.MinimumLevel.ToString());
        SetMainTitle();
        RunMachine();
        return true;
    }

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

    public void SetResolution(int width, int height) => _uiDispatcher.Post(() => {
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

    public event EventHandler<UIRenderEventArgs>? RenderScreen;

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            _disposed = true;
            if (disposing) {
                _drawTimer.Stop();
                _drawTimer.Dispose();
                _uiDispatcher.Post(() => {
                    Bitmap?.Dispose();
                    Cursor?.Dispose();
                }, DispatcherPriority.MaxValue);
                _drawingSemaphoreSlim?.Dispose();
                PlayCommand.Execute(null);
                IsMachineRunning = false;
                DisposeEmulator();
                if (_emulatorThread?.IsAlive == true) {
                    _emulatorThread.Join();
                }
            }
        }
    }

    private void DisposeEmulator() => _programExecutor?.Dispose();

    private bool _isInitLogLevelSet;

    private string _currentLogLevel = "";

    public string CurrentLogLevel {
        get {
            if (_isInitLogLevelSet) {
                return _currentLogLevel;
            }
            SetLogLevel(_loggerService.AreLogsSilenced ? "Silent" : _loggerService.LogLevelSwitch.MinimumLevel.ToString());
            _isInitLogLevelSet = true;
            return _currentLogLevel;
        }
        set => SetProperty(ref _currentLogLevel, value);
    }

    [RelayCommand] public void SetLogLevelToSilent() => SetLogLevel("Silent");
    [RelayCommand] public void SetLogLevelToVerbose() => SetLogLevel("Verbose");
    [RelayCommand] public void SetLogLevelToDebug() => SetLogLevel("Debug");
    [RelayCommand] public void SetLogLevelToInformation() => SetLogLevel("Information");
    [RelayCommand] public void SetLogLevelToWarning() => SetLogLevel("Warning");
    [RelayCommand] public void SetLogLevelToError() => SetLogLevel("Error");
    [RelayCommand] public void SetLogLevelToFatal() => SetLogLevel("Fatal");

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

    private void RunMachine() {
        _emulatorThread = new Thread(MachineThread) {
            Name = "Emulator"
        };
        _emulatorThread.Start();
    }

    private void OnEmulatorErrorOccured(Exception e) => _uiDispatcher.Post(() => {
        StatusMessage = "Emulator crashed.";
        ShowError(e);
    });

    private void MachineThread() {
        try {
            if (Debugger.IsAttached) {
                StartProgramExecutor();
            } else {
                try {
                    StartProgramExecutor();
                } catch (Exception e) {
                    e.Demystify();
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error(e, "An error occurred during execution");
                    }
                    OnEmulatorErrorOccured(e);
                }
            }
        }  finally {
            _uiDispatcher.Post(() => IsMachineRunning = false);
            _uiDispatcher.Post(() => StatusMessage = "Emulator: stopped.");
            _uiDispatcher.Post(() => AsmOverrideStatus = "");
        }
    }

    [ObservableProperty]
    private PerformanceViewModel? _performanceViewModel;

    [ObservableProperty]
    private bool _isPerformanceVisible;

    private void StartProgramExecutor() {
        (IProgramExecutor ProgramExecutor, SoftwareMixer? SoftwareMixer, ITimeMultiplier? Pit) viewModelEmulatorDependencies = CreateEmulator();
        _programExecutor = viewModelEmulatorDependencies.ProgramExecutor;
        _softwareMixer = viewModelEmulatorDependencies.SoftwareMixer;
        _pit = viewModelEmulatorDependencies.Pit;
        PerformanceViewModel = new(_uiDispatcherTimerFactory, _programExecutor, new PerformanceMeasurer(), this);
        DebugViewModel = new DebugViewModel(_hostStorageProvider, _uiDispatcherTimerFactory, this, _programExecutor, _textClipboard);
        TimeMultiplier = Configuration.TimeMultiplier;
        _uiDispatcher.Post(() => IsMachineRunning = true);
        _uiDispatcher.Post(() => StatusMessage = "Emulator started.");
        _programExecutor?.Run();
        if (_closeAppOnEmulatorExit) {
            _uiDispatcher.Post(() => CloseMainWindow?.Invoke(this, EventArgs.Empty));
        }
    }
    
    private sealed class ViewModelEmulatorDependenciesVisitor : IInternalDebugger {
        public SoftwareMixer? SoftwareMixer { get; private set; }
        public ITimeMultiplier? Pit { get; private set; }

        public void Visit<T>(T component) where T : IDebuggableComponent {
            SoftwareMixer ??= component as SoftwareMixer;
            Pit ??= component as ITimeMultiplier;
        }
        public bool NeedsToVisitEmulator => SoftwareMixer is null || Pit is null;
    }

    private (IProgramExecutor ProgramExecutor, SoftwareMixer? SoftwareMixer, ITimeMultiplier? Pit) CreateEmulator() {
        IProgramExecutor programExecutor = _programExecutorFactory.Create(this);
        ViewModelEmulatorDependenciesVisitor visitor = new();
        programExecutor.Accept(visitor);
        return (programExecutor, visitor.SoftwareMixer, visitor.Pit);
    }
    public event EventHandler? CloseMainWindow;
}