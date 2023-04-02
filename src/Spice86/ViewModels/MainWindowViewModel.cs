namespace Spice86.ViewModels;

using Serilog.Events;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MessageBox.Avalonia.BaseWindows.Base;
using MessageBox.Avalonia.Enums;

using Spice86;
using Spice86.Keyboard;
using Spice86.Views;
using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;

/// <inheritdoc />
public sealed partial class MainWindowViewModel : ObservableObject, IGui, IDisposable {
    private readonly ILoggerService _loggerService;
    private Configuration _configuration = new();
    private bool _disposed;
    private Thread? _emulatorThread;
    private bool _isSettingResolution = false;
    private DebugWindow? _debugWindow;
    private PaletteWindow? _paletteWindow;
    private PerformanceWindow? _performanceWindow;

    private bool _closeAppOnEmulatorExit = false;

    public bool PauseEmulatorOnStart { get; private set; } = false;

    internal void OnKeyUp(KeyEventArgs e) => KeyUp?.Invoke(this, e);

    private ProgramExecutor? _programExecutor;

    [ObservableProperty]
    private AvaloniaList<VideoBufferViewModel> _videoBuffers = new();
    private ManualResetEvent _okayToContinueEvent = new(true);

    internal void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);

    [ObservableProperty]
    private bool _isPaused;

    public event EventHandler<EventArgs>? KeyUp;
    public event EventHandler<EventArgs>? KeyDown;

    private bool _isMainWindowClosing;

    public MainWindowViewModel(ILoggerService loggerService) {
        _loggerService = loggerService;
        if (App.MainWindow is not null) {
            App.MainWindow.Closing += (s, e) => _isMainWindowClosing = true;
        }
    }

    public void PauseEmulationOnStart() {
        Pause();
        PauseEmulatorOnStart = false;
    }
    
    public void HideMouseCursor() {
        foreach (VideoBufferViewModel x in VideoBuffers) {
            x.ShowCursor = false;
        }
    }

    public void ShowMouseCursor() {
        foreach (VideoBufferViewModel x in VideoBuffers) {
            x.ShowCursor = true;
        }    }

    public bool IsEmulatorThreadPresent => _emulatorThread is not null;

    public bool IsProgramExecutorPresent => _programExecutor is not null;

    [RelayCommand(CanExecute = nameof(IsProgramExecutorPresent))]
    public async Task DumpEmulatorStateToFile() {
        if (_programExecutor is null) {
            return;
        }
        OpenFolderDialog ofd = new OpenFolderDialog() {
            Title = "Dump emulator state to directory...",
            Directory = _configuration.RecordedDataDirectory
        };
        if (Directory.Exists(_configuration.RecordedDataDirectory)) {
            ofd.Directory = _configuration.RecordedDataDirectory;
        }
        string? dir = _configuration.RecordedDataDirectory;
        if (App.MainWindow is not null) {
            dir = await ofd.ShowAsync(App.MainWindow);
        }
        if (string.IsNullOrWhiteSpace(dir)
        && !string.IsNullOrWhiteSpace(_configuration.RecordedDataDirectory)) {
            dir = _configuration.RecordedDataDirectory;
        }
        if (!string.IsNullOrWhiteSpace(dir)) {
            new RecorderDataWriter(dir, _programExecutor.Machine,
                _loggerService)
                    .DumpAll();
        }
    }

    [RelayCommand(CanExecute = nameof(IsEmulatorThreadPresent))]
    public void Pause() {
        if (_emulatorThread is null) {
            return;
        }

        _okayToContinueEvent.Reset();
        IsPaused = true;
    }

    [RelayCommand(CanExecute = nameof(IsEmulatorThreadPresent))]
    public void Play() {
        if (_emulatorThread is null) {
            return;
        }

        _okayToContinueEvent.Set();
        IsPaused = false;
    }

    public void SetConfiguration(string[] args) {
        _configuration = GenerateConfiguration(args);
        SetLogLevel(_configuration.SilencedLogs ? "Silent" : _loggerService.LogLevelSwitch.MinimumLevel.ToString());
        SetMainTitle();
    }

    private void SetMainTitle() {
        MainTitle = $"{nameof(Spice86)} {_configuration.Exe}";
    }

    [ObservableProperty]
    private string? _mainTitle;

    public void AddBuffer(IVideoCard videoCard, uint address, double scale, int bufferWidth, int bufferHeight,
        bool isPrimaryDisplay = false) {
        VideoBufferViewModel videoBuffer = new VideoBufferViewModel(videoCard, scale, bufferWidth, bufferHeight, address, VideoBuffers.Count, isPrimaryDisplay);
        Dispatcher.UIThread.Post(
            () => {
                if(!VideoBuffers.Any(x => x.Address == videoBuffer.Address)) {
                    VideoBuffers.Add(videoBuffer);
                }
            }
        );
    }

    [RelayCommand]
    public async Task DebugExecutableCommand() {
        _closeAppOnEmulatorExit = false;
        await StartNewExecutable();
        PauseEmulatorOnStart = true;
    }

    [RelayCommand]
    public async Task StartExecutable() {
        _closeAppOnEmulatorExit = false;
        await StartNewExecutable();
    }

    private async Task StartNewExecutable() {
        if (App.MainWindow is not null) {
            OpenFileDialog ofd = new OpenFileDialog() {
                Title = "Start Executable...",
                AllowMultiple = false,
                Filters = new(){
                    new FileDialogFilter() {
                        Extensions = {"exe", "com", "EXE", "COM" },
                        Name = "DOS Executables"
                    },
                    new FileDialogFilter() {
                        Extensions = { "*" },
                        Name = "All Files"
                    }
                }
            };
            string[]? files = await ofd.ShowAsync(App.MainWindow);
            if (files?.Any() == true) {
                _configuration.Exe = files[0];
                _configuration.ExeArgs = "";
                _configuration.CDrive = Path.GetDirectoryName(_configuration.Exe);
                Play();
                Dispatcher.UIThread.Post(() => DisposeEmulator(), DispatcherPriority.MaxValue);
                SetMainTitle();
                _okayToContinueEvent = new(true);
                _programExecutor?.Machine.ExitEmulationLoop();
                while (_emulatorThread?.IsAlive == true) {
                    Dispatcher.UIThread.RunJobs();
                }
                _closeAppOnEmulatorExit = false;
                RunEmulator();
            }
        }
    }

    private double _timeMultiplier = 1;

    public double TimeMultiplier {
        get => _timeMultiplier;
        set {
            SetProperty(ref _timeMultiplier, value);
            _programExecutor?.Machine.Timer.SetTimeMultiplier(_timeMultiplier);
        }
    }

    [RelayCommand(CanExecute = nameof(IsProgramExecutorPresent))]
    public async Task ShowPerformance() {
        if (_performanceWindow != null) {
            _performanceWindow.Activate();
        } else if (_programExecutor is not null) {
            _performanceWindow = new PerformanceWindow() {
                DataContext = new PerformanceViewModel(
                    _programExecutor.Machine, this)
            };
            _performanceWindow.Closed += (s, e) => _performanceWindow = null;
            _performanceWindow.Show();
        } else {
            await MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow("", "Please start a program first")
                .ShowDialog(App.MainWindow);
        }
    }

    [RelayCommand(CanExecute = nameof(IsProgramExecutorPresent))]
    public void ShowDebugWindow() {
        if (_debugWindow != null) {
            _debugWindow.Activate();
        } else if(_programExecutor is not null) {
            _debugWindow = new DebugWindow(_programExecutor.Machine);
            _debugWindow.Closed += (s, e) => _debugWindow = null;
            _debugWindow.Show();
        }
    }

    [RelayCommand(CanExecute = nameof(IsProgramExecutorPresent))]
    public void ShowColorPalette() {
        if (_paletteWindow != null) {
            _paletteWindow.Activate();
        } else if(_programExecutor is not null) {
            _paletteWindow = new PaletteWindow(new PaletteViewModel(_programExecutor.Machine));
            _paletteWindow.Closed += (s, e) => _paletteWindow = null;
            _paletteWindow.Show();
        }
    }

    [RelayCommand]
    public void ResetTimeMultiplier() {
        TimeMultiplier = _configuration.TimeMultiplier;
    }
    public void UpdateScreen() {
        if (_disposed || _isSettingResolution) {
            return;
        }
        foreach (VideoBufferViewModel videoBuffer in SortedBuffers()) {
            videoBuffer.Draw();
        }
    }

    public int Height { get; private set; }

    public int MouseX { get; set; }

    public int MouseY { get; set; }

    public IDictionary<uint, IVideoBufferViewModel> VideoBuffersToDictionary =>
        VideoBuffers
        .ToDictionary(static x =>
            x.Address,
            x => (IVideoBufferViewModel)x);

    public int Width { get; private set; }

    public bool IsLeftButtonClicked { get; private set; }

    public bool IsRightButtonClicked { get; private set; }
    public void OnMainWindowOpened(object? sender, EventArgs e) {
        if(RunEmulator()) {
            _closeAppOnEmulatorExit = true;
        }
    }

    private bool RunEmulator() {
        if (string.IsNullOrWhiteSpace(_configuration.Exe) ||
            string.IsNullOrWhiteSpace(_configuration.CDrive)) {
            return false;
        }

        RunMachine();
        return true;
    }

    public void OnMouseClick(PointerEventArgs @event, bool click) {
        if (@event.Pointer.IsPrimary) {
            IsLeftButtonClicked = click;
        }

        if (!@event.Pointer.IsPrimary) {
            IsRightButtonClicked = click;
        }
    }

    public void OnMouseMoved(PointerEventArgs @event, Image image) {
        MouseX = (int)@event.GetPosition(image).X;
        MouseY = (int)@event.GetPosition(image).Y;
    }

    public void RemoveBuffer(uint address) {
        VideoBufferViewModel videoBuffer = VideoBuffers.First(x => x.Address == address);
        videoBuffer.Dispose();
        VideoBuffers.Remove(videoBuffer);
    }

    public void SetResolution(int width, int height, uint address) {
        Dispatcher.UIThread.Post(() => {
            _isSettingResolution = true;
            DisposeBuffers();
            VideoBuffers = new();
            Width = width;
            Height = height;
            AddBuffer(_videoCard, address, 1, width, height, true);
            _isSettingResolution = false;
        }, DispatcherPriority.MaxValue);
    }

    private void DisposeBuffers() {
        for (int i = 0; i < VideoBuffers.Count; i++) {
            VideoBufferViewModel buffer = VideoBuffers[i];
            buffer.Dispose();
        }
        VideoBuffers.Clear();
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                PlayCommand.Execute(null);
                DisposeEmulator();
                _performanceWindow?.Close();
                _paletteWindow?.Close();
                _okayToContinueEvent.Set();
                if (_emulatorThread?.IsAlive == true) {
                    _emulatorThread.Join();
                }
                _okayToContinueEvent.Dispose();
            }
            _disposed = true;
        }
    }

    private void DisposeEmulator() {
        DisposeBuffers();
        _programExecutor?.Dispose();
    }

    private static Configuration GenerateConfiguration(string[] args) {
        return CommandLineParser.ParseCommandLine(args);
    }

    private IEnumerable<VideoBufferViewModel> SortedBuffers() {
        return VideoBuffers.OrderBy(static x => x.Address).Select(static x => x);
    }

    private async Task ShowEmulationErrorMessage(Exception e) {
        IMsBoxWindow<ButtonResult> errorMessage = MessageBox.Avalonia.MessageBoxManager
            .GetMessageBoxStandardWindow("An unhandled exception occured", e.GetBaseException().Message);
        if (!_disposed && !_isMainWindowClosing) {
            await errorMessage.ShowDialog(App.MainWindow);
        }
    }

    [ObservableProperty]
    private string _currentLogLevel = "";

    [RelayCommand]
    public void SetLogLevel(string logLevel) {
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
        ShowVideo = true;
        _emulatorThread = new Thread(MachineThread) {
            Name = "Emulator"
        };
        _emulatorThread.Start();
    }

    // We use async void, but thankfully this doesn't generate an exception.
    // So this is OK...
    private async void OnEmulatorErrorOccured(Exception e) {
        await Dispatcher.UIThread.InvokeAsync(async () => await ShowEmulationErrorMessage(e));
    }

    private event Action<Exception>? EmulatorErrorOccured;

    [ObservableProperty]
    private bool _showVideo = true;

    private IVideoCard _videoCard = null!;

    private void MachineThread() {
        try {
            if(!_disposed) {
                _okayToContinueEvent.Set();
            }
            _programExecutor = new ProgramExecutor(
                _loggerService,
                this, new AvaloniaKeyScanCodeConverter(), _configuration);
            TimeMultiplier = _configuration.TimeMultiplier;
            _videoCard = _programExecutor.Machine.VgaCard;
            _programExecutor.Run();
            Dispatcher.UIThread.Post(() => ShowVideo = false);
            if(_closeAppOnEmulatorExit) {
                Dispatcher.UIThread.Post(() => App.MainWindow?.Close());
            }
        } catch (Exception e) {
            e.Demystify();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error(e, "An error occurred during execution");
            }
            EmulatorErrorOccured += OnEmulatorErrorOccured;
            EmulatorErrorOccured?.Invoke(e);
            EmulatorErrorOccured -= OnEmulatorErrorOccured;
        }
    }

    /// <inheritdoc />
    public void WaitForContinue() {
        _okayToContinueEvent.WaitOne(Timeout.Infinite);
    }
}