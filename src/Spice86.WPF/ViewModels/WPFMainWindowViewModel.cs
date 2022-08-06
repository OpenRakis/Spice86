namespace Spice86.UI.ViewModels;

using Microsoft.Win32;

using Prism.Commands;

using ReactiveUI;

using Serilog;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Logging;
using Spice86.Shared;
using Spice86.Shared.Interfaces;
using Spice86.WPF;
using Spice86.WPF.Commands;
using Spice86.WPF.Converters;
using Spice86.WPF.Views;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

/// <summary>
/// GUI of the emulator.<br/>
/// <ul>
/// <li>Displays the content of the video ram (when the emulator requests it)</li>
/// <li>Communicates keyboard and mouse events to the emulator</li>
/// </ul>
/// </summary>
public partial class WPFMainWindowViewModel : ReactiveObject, IGui, IDisposable {
    private static readonly ILogger _logger = new Serilogger().Logger.ForContext<WPFMainWindowViewModel>();
    private Configuration? _configuration;
    private bool _disposedValue;
    private Thread? _emulatorThread;
    private bool _isSettingResolution = false;
    private PaletteWindow? _paletteWindow;
    private PerformanceWindow? _performanceWindow;

    public AsyncRelayCommand StartExecutableCommand { get; private set; }

    public DelegateCommand DumpEmulatorStateToFileCommand { get; private set; }

    public AsyncRelayCommand DebugExecutableCommand { get; private set; }

    public DelegateCommand ShowColorPaletteCommand { get; private set; }

    public DelegateCommand ShowPerformanceCommand { get; private set; }

    public DelegateCommand PauseCommand { get; private set; }

    public DelegateCommand ContinueCommand { get; private set; }

    public DelegateCommand ResetTimeMultiplierCommand { get; private set; }

    private static object _lock = new();

    public WPFMainWindowViewModel() {
        StartExecutableCommand = new(StartExecutable);
        DebugExecutableCommand = new(DebugExecutable);
        DumpEmulatorStateToFileCommand = new(DumpEmulatorStateToFile);
        ShowColorPaletteCommand = new(ShowColorPalette);
        PauseCommand = new(Pause);
        ContinueCommand = new(Continue);
        ShowPerformanceCommand = new(ShowPerformance);
        ResetTimeMultiplierCommand = new(ResetTimeMultiplier);
        BindingOperations.EnableCollectionSynchronization(_videoBuffers, _lock);
    }

    private Task DebugExecutable() {
        return StartNewExecutable(true);
    }

    internal void OnKeyUp(System.Windows.Input.KeyEventArgs e) => KeyUp?.Invoke(this, e);

    private ProgramExecutor? _programExecutor;
    private ObservableCollection<WPFVideoBufferViewModel> _videoBuffers = new();
    private ManualResetEvent _okayToContinueEvent = new(true);

    internal void OnKeyDown(System.Windows.Input.KeyEventArgs e) => KeyDown?.Invoke(this, e);

    private bool _isPaused = false;

    public bool IsPaused {
        get => _isPaused;
        set => this.RaiseAndSetIfChanged(ref _isPaused, value);
    }

    public event EventHandler<EventArgs>? KeyUp;
    public event EventHandler<EventArgs>? KeyDown;

    public void DumpEmulatorStateToFile() {
        if (_programExecutor is null || _configuration is null) {
            return;
        }
        var ofd = new OpenFileDialog() {
            Multiselect = false,
            CheckPathExists = true,
            Title = "Dump emulator state to directory...",
            InitialDirectory = _configuration.RecordedDataDirectory
        };
        if (Directory.Exists(_configuration.RecordedDataDirectory)) {
            ofd.InitialDirectory = _configuration.RecordedDataDirectory;
        }
        if(ofd.ShowDialog(App.Current.MainWindow) == true) {
            string? dir = Path.GetDirectoryName(ofd.FileName);
            if (string.IsNullOrWhiteSpace(dir)
            && !string.IsNullOrWhiteSpace(_configuration.RecordedDataDirectory)) {
                dir = _configuration.RecordedDataDirectory;
            }
            if (!string.IsNullOrWhiteSpace(dir)) {
                new RecorderDataWriter(dir, _programExecutor.Machine).DumpAll();
            }
        }
    }

    private void Pause() {
        if (_emulatorThread is not null) {
            _okayToContinueEvent.Reset();
            IsPaused = true;
        }
    }

    private void Continue() {
        if (_emulatorThread is not null) {
            _okayToContinueEvent.Set();
            IsPaused = false;
        }
    }

    public void SetConfiguration(string[] args) {
        Configuration? configuration = GenerateConfiguration(args);
        _configuration = configuration;
        if (configuration is null) {
            Exit();
            Environment.Exit(0);
        }
        SetMainTitle();
    }

    public string? MainTitle {
        get => _mainTitle;
        set => this.RaiseAndSetIfChanged(ref _mainTitle, value);
    }

    private void SetMainTitle() {
        MainTitle = $"{nameof(Spice86)} {_configuration?.Exe}";
    }

    private string? _mainTitle;

    public ObservableCollection<WPFVideoBufferViewModel> VideoBuffers {
        get => _videoBuffers;
        set => this.RaiseAndSetIfChanged(ref _videoBuffers, value);
    }

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false) {
        App.Current.Dispatcher.Invoke(() => {
            var videoBuffer = new WPFVideoBufferViewModel(scale, bufferWidth, bufferHeight, address, VideoBuffers.Count, isPrimaryDisplay);
            VideoBuffers.Add(videoBuffer);
        }, DispatcherPriority.Render);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public Task StartExecutable() {
        return StartNewExecutable();
    }

    private async Task StartNewExecutable(bool pauseOnStart = false) {
        OpenFileDialog? ofd = new OpenFileDialog() {
            Title = "Start Executable...",
            Multiselect = false,
            CheckFileExists = true,
            //Filters = new(){
            //    new FileDialogFilter() {
            //        Extensions = {"exe", "com" },
            //        Name = "DOS Executables"
            //    },
            //    new FileDialogFilter() {
            //        Extensions = { "*" },
            //        Name = "All Files"
            //    }
            //}
        };
        if(ofd.ShowDialog(App.Current.MainWindow) == true) {
            string? file = ofd.FileName;
            if (!string.IsNullOrWhiteSpace(file) && _configuration is not null) {
                _configuration.Exe = file;
                _configuration.ExeArgs = "";
                _configuration.CDrive = Path.GetDirectoryName(_configuration.Exe);
                App.Current.Dispatcher.Invoke(() => {
                    DisposeEmulator();
                });
                SetMainTitle();
                _okayToContinueEvent = new(true);
                IsPaused = pauseOnStart;
                _programExecutor?.Machine.ExitEmulationLoop();
                while (_emulatorThread?.IsAlive == true) {
                    await Dispatcher.Yield();
                }
                RunEmulator();
            }
        }
    }

    private double _timeMultiplier = 1;

    public double TimeMultiplier {
        get => _timeMultiplier;
        set {
            this.RaiseAndSetIfChanged(ref _timeMultiplier, Math.Min(1, value));
            _programExecutor?.Machine.Timer.SetTimeMultiplier(_timeMultiplier);
        }
    }

    public void ShowPerformance() {
        if (_performanceWindow != null) {
            _performanceWindow.Activate();
        } else if (_programExecutor is not null) {
            _performanceWindow = new PerformanceWindow() {
                DataContext = new WPFPerformanceViewModel(
                    _programExecutor.Machine, this)
            };
            _performanceWindow.Closed += (s, e) => _performanceWindow = null;
            _performanceWindow.Show();
        }
    }

    public void ShowColorPalette() {
        if (_paletteWindow != null) {
            _paletteWindow.Activate();
        } else {
            _paletteWindow = new PaletteWindow(this);
            _paletteWindow.Closed += (s, e) => _paletteWindow = null;
            _paletteWindow.Show();
        }
    }

    public void ResetTimeMultiplier() {
        TimeMultiplier = _configuration!.TimeMultiplier;
    }

    private Rgb[] _palette = Array.Empty<Rgb>();

    public ReadOnlyCollection<Rgb> Palette => Array.AsReadOnly(_palette);

    public void Draw(byte[] memory, Rgb[] palette) {
        if (_disposedValue || _isSettingResolution) {
            return;
        }
        _palette = palette;
        foreach (WPFVideoBufferViewModel videoBuffer in SortedBuffers()) {
            videoBuffer.Draw(memory, palette);
        }
    }

    public void Exit() {
        this.Dispose();
    }

    public int Height { get; private set; }

    public int MouseX { get; set; }

    public int MouseY { get; set; }

    public IDictionary<uint, IVideoBufferViewModel> VideoBuffersToDictionary =>
        VideoBuffers
        .ToDictionary(x =>
            x.Address,
            x => (IVideoBufferViewModel)x);

    public int Width { get; private set; }

    public bool IsLeftButtonClicked { get; private set; }

    public bool IsRightButtonClicked { get; private set; }

    public void OnMainWindowOpened(object? sender, EventArgs e) {
        RunEmulator();
    }

    private void RunEmulator() {
        if (_configuration is not null &&
            !string.IsNullOrWhiteSpace(_configuration.Exe) &&
            !string.IsNullOrWhiteSpace(_configuration.CDrive)) {
            _emulatorThread = new Thread(RunMachine) {
                Name = "Emulator"
            };
            _emulatorThread.Start();
        }
    }

    public void OnMouseClick(System.Windows.Input.MouseEventArgs @event, bool click) {
        if (@event.MouseDevice.LeftButton == System.Windows.Input.MouseButtonState.Pressed) {
            IsLeftButtonClicked = click;
        }

        if (@event.MouseDevice.RightButton == System.Windows.Input.MouseButtonState.Pressed) {
            IsRightButtonClicked = click;
        }
    }

    public void OnMouseMoved(System.Windows.Input.MouseEventArgs @event, UIElement image) {
        MouseX = (int)@event.GetPosition(image).X;
        MouseY = (int)@event.GetPosition(image).Y;
    }

    public void RemoveBuffer(uint address) {
        VideoBuffers.Remove(VideoBuffers.First(x => x.Address == address));
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
        for (int i = 0; i < VideoBuffers.Count; i++) {
            WPFVideoBufferViewModel buffer = VideoBuffers[i];
            buffer.Dispose();
        }
        _videoBuffers.Clear();
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                DisposeEmulator();
                if(_emulatorThread?.IsAlive == true) {
                    _emulatorThread.Join();
                }
            }
            _disposedValue = true;
        }
    }

    private void DisposeEmulator() {
        _performanceWindow?.Close();
        _paletteWindow?.Close();
        DisposeBuffers();
        _programExecutor?.Dispose();
        _okayToContinueEvent.Dispose();
    }

    private static Configuration? GenerateConfiguration(string[] args) {
        return CommandLineParser.ParseCommandLine(args);
    }

    private IEnumerable<WPFVideoBufferViewModel> SortedBuffers() {
        return VideoBuffers.OrderBy(x => x.Address).Select(x => x);
    }

    private void RunMachine() {
        if (_configuration is null) {
            _logger.Error("No configuration available, cannot continue");
        } else {
            try {
                _okayToContinueEvent.Set();
                _programExecutor = new ProgramExecutor(this, new WPFKeyScanCodeConverter(), _configuration); ;
                TimeMultiplier = _configuration.TimeMultiplier;
                _programExecutor.Run();
            } catch (Exception e) {
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _logger.Error(e, "An error occurred during execution");
                }
            }
        }
    }

    public void WaitOne() {
        _okayToContinueEvent.WaitOne(Timeout.Infinite);
    }
}