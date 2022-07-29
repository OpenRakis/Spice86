namespace Spice86.UI.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Win32;

using Serilog;

using Spice86.CLI;
using Spice86.Emulator;
using Spice86.Emulator.Devices.Video;
using Spice86.Emulator.Function.Dump;
using Spice86.UI.Interfaces;
using Spice86.WPF;
using Spice86.WPF.Converters;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// GUI of the emulator.<br/>
/// <ul>
/// <li>Displays the content of the video ram (when the emulator requests it)</li>
/// <li>Communicates keyboard and mouse events to the emulator</li>
/// </ul>
/// </summary>
public partial class WPFMainWindowViewModel : ObservableObject, IGui, IDisposable {
    private static readonly ILogger _logger = Program.Logger.ForContext<WPFMainWindowViewModel>();
    private Configuration? _configuration;
    private bool _disposedValue;
    private bool _restartingEmulator = false;
    private Thread? _emulatorThread;
    private bool _isSettingResolution = false;
    //private PaletteWindow? _paletteWindow;
    //private PerformanceWindow? _performanceWindow;
    //private DebuggerWindow? _debuggerWindow;

    internal void OnKeyUp(KeyEventArgs e) => KeyUp?.Invoke(this, e);

    private ProgramExecutor? _programExecutor;
    private ObservableCollection<WPFVideoBufferViewModel> _videoBuffers = new();
    private ManualResetEvent _okayToContinueEvent = new(true);

    internal void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);

    [ObservableProperty] private bool _isPaused = false;

    public event EventHandler<EventArgs>? KeyUp;
    public event EventHandler<EventArgs>? KeyDown;

    [ICommand]
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
            var dir = Path.GetDirectoryName(ofd.FileName);
            if (string.IsNullOrWhiteSpace(dir)
            && !string.IsNullOrWhiteSpace(_configuration.RecordedDataDirectory)) {
                dir = _configuration.RecordedDataDirectory;
            }
            if (!string.IsNullOrWhiteSpace(dir)) {
                new RecorderDataWriter(dir, _programExecutor.Machine).DumpAll();
            }
        }
    }

    [ICommand]
    private void Pause() {
        if (_emulatorThread is not null) {
            _okayToContinueEvent.Reset();
            IsPaused = true;
        }
    }

    [ICommand]
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

    private void SetMainTitle() {
        MainTitle = $"{nameof(Spice86)} {_configuration?.Exe}";
    }

    [ObservableProperty]
    private string? _mainTitle;

    public ObservableCollection<WPFVideoBufferViewModel> VideoBuffers {
        get => _videoBuffers;
        set => this.SetProperty(ref _videoBuffers, value);
    }

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false) {
        var videoBuffer = new WPFVideoBufferViewModel(scale, bufferWidth, bufferHeight, address, VideoBuffers.Count, isPrimaryDisplay);
        Dispatcher.CurrentDispatcher.Invoke(() =>
                VideoBuffers.Add(videoBuffer)
            , DispatcherPriority.Render);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [ICommand]
    public Task DebugExecutableCommand() {
        return StartNewExecutable(true);
    }

    [ICommand]
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
            var file = ofd.FileName;
            if (!string.IsNullOrWhiteSpace(file) && _configuration is not null) {
                _configuration.Exe = file;
                _configuration.ExeArgs = "";
                _configuration.CDrive = Path.GetDirectoryName(_configuration.Exe);
                DisposeEmulator();
                SetMainTitle();
                _okayToContinueEvent = new(true);
                IsPaused = pauseOnStart;
                _restartingEmulator = true;
                _programExecutor?.Machine.ExitEmulationLoop();
                while (_emulatorThread?.IsAlive == true) {
                    await Dispatcher.Yield();
                }
                RunEmulator();
                _restartingEmulator = false;
            }
        }
    }

    private double _timeMultiplier = 1;

    public double TimeMultiplier {
        get => _timeMultiplier;
        set {
            this.SetProperty(ref _timeMultiplier, value);
            _programExecutor?.Machine.Timer.SetTimeMultiplier(_timeMultiplier);
        }
    }

    [ICommand]
    public void ShowDebugger() {
        //if (_debuggerWindow != null) {
        //    _debuggerWindow.Activate();
        //} else if (_programExecutor is not null) {
        //    _debuggerWindow = new DebuggerWindow() {
        //        DataContext = new DebuggerViewModel(
        //            _programExecutor.Machine)
        //    };
        //    _debuggerWindow.Closed += (s, e) => _debuggerWindow = null;
        //    _debuggerWindow.Show();
        //}
    }

    [ICommand]
    public void ShowPerformance() {
        //if (_performanceWindow != null) {
        //    _performanceWindow.Activate();
        //} else if (_programExecutor is not null) {
        //    _performanceWindow = new PerformanceWindow() {
        //        DataContext = new WPerformanceViewModel(
        //            _programExecutor.Machine)
        //    };
        //    _performanceWindow.Closed += (s, e) => _performanceWindow = null;
        //    _performanceWindow.Show();
        //}
    }

    [ICommand]
    public void ShowColorPalette() {
        //if (_paletteWindow != null) {
        //    _paletteWindow.Activate();
        //} else {
        //    _paletteWindow = new PaletteWindow(this);
        //    _paletteWindow.Closed += (s, e) => _paletteWindow = null;
        //    _paletteWindow.Show();
        //}
    }

    [ICommand]
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

    public IDictionary<uint, IVideoBufferViewModel> VideoBuffersAsDictionary =>
        (IDictionary<uint, IVideoBufferViewModel>)VideoBuffers
        .ToDictionary(x =>
            x.Address,
            x => x);

    public int Width { get; private set; }

    public bool IsLeftButtonClicked { get; private set; }

    public bool IsRightButtonClicked { get; private set; }

    public void OnMainWindowOpened(object? sender, EventArgs e) => RunEmulator();

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

    public void OnMouseClick(MouseEventArgs @event, bool click) {
        if (@event.MouseDevice.LeftButton == MouseButtonState.Pressed) {
            IsLeftButtonClicked = click;
        }

        if (@event.MouseDevice.RightButton == MouseButtonState.Pressed) {
            IsRightButtonClicked = click;
        }
    }

    public void OnMouseMoved(MouseEventArgs @event, UIElement image) {
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
        Dispatcher.CurrentDispatcher.Invoke(() => {
            for (int i = 0; i < VideoBuffers.Count; i++) {
                WPFVideoBufferViewModel buffer = VideoBuffers[i];
                buffer.Dispose();
            }
            _videoBuffers.Clear();
        }, DispatcherPriority.Render);
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
        Dispatcher.CurrentDispatcher.Invoke(() => {
            //_performanceWindow?.Close();
            //_debuggerWindow?.Close();
            //_paletteWindow?.Close();
        }, DispatcherPriority.Render);
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