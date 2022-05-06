namespace Spice86.UI.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog;

using Spice86.CLI;
using Spice86.Emulator;
using Spice86.Emulator.Devices.Video;
using Spice86.UI.Views;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

/// <summary>
/// GUI of the emulator.<br/>
/// <ul>
/// <li>Displays the content of the video ram (when the emulator requests it)</li>
/// <li>Communicates keyboard and mouse events to the emulator</li>
/// </ul>
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IDisposable {
    private static readonly ILogger _logger = Program.Logger.ForContext<MainWindowViewModel>();
    private Configuration? _configuration;
    private bool _disposedValue;
    private Thread? _emulatorThread;
    private bool _isSettingResolution = false;
    private PaletteWindow? paletteWindow;

    internal void OnKeyUp(KeyEventArgs e) => KeyUp?.Invoke(this, e);

    private ProgramExecutor? _programExecutor;
    private AvaloniaList<VideoBufferViewModel> _videoBuffers = new();
    readonly ManualResetEvent _okayToContinueEvent = new(true);

    internal void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);

    [ObservableProperty] private bool _isPaused = false;

    public event EventHandler<KeyEventArgs>? KeyUp;
    public event EventHandler<KeyEventArgs>? KeyDown;

    [ICommand]
    private void Pause() {
        if (_emulatorThread is not null) {
            _okayToContinueEvent.Reset();
            IsPaused = true;
        }
    }

    [ICommand]
    private void Play() {
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
        }
        MainTitle = $"{nameof(Spice86)} {configuration?.Exe}";
    }

    public string? MainTitle { get; private set; }

    public AvaloniaList<VideoBufferViewModel> VideoBuffers {
        get => _videoBuffers;
        set => this.SetProperty(ref _videoBuffers, value);
    }

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false) {
        var videoBuffer = new VideoBufferViewModel(scale, bufferWidth, bufferHeight, address, VideoBuffers.Count, isPrimaryDisplay);
        Dispatcher.UIThread.Post(() => {
            VideoBuffers.Add(videoBuffer);
        }, DispatcherPriority.MaxValue);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
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
    public void ShowColorPalette() {

        if (this.paletteWindow != null) {
            this.paletteWindow.Activate();
        } else if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            this.paletteWindow = new PaletteWindow(desktop.MainWindow, this);
            this.paletteWindow.Closed += PaletteWindow_Closed;
            paletteWindow.Show();
        }
    }

    private void PaletteWindow_Closed(object? sender, EventArgs e) {
        if (this.paletteWindow != null) {
            this.paletteWindow.Closed -= this.PaletteWindow_Closed;
            this.paletteWindow = null;
        }
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

    public int Height { get; private set; }

    public int MouseX { get; set; }

    public int MouseY { get; set; }

    public IDictionary<uint, VideoBufferViewModel> VideoBuffersAsDictionary => VideoBuffers.ToDictionary(x => x.Address, x => x);

    public int Width { get; private set; }

    public bool IsLeftButtonClicked { get; private set; }

    public bool IsRightButtonClicked { get; private set; }

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
        Dispatcher.UIThread.Post(() => {
            for (int i = 0; i < VideoBuffers.Count; i++) {
                VideoBufferViewModel buffer = VideoBuffers[i];
                buffer.Dispose();
            }
            _videoBuffers.Clear();
        }, DispatcherPriority.MaxValue);
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

    private IEnumerable<VideoBufferViewModel> SortedBuffers() {
        return VideoBuffers.OrderBy(x => x.Address).Select(x => x);
    }

    private void RunMachine() {
        if (_configuration == null) {
            _logger.Error("No configuration available, cannot continue");
        } else {
            try {
                _okayToContinueEvent.Set();
                _programExecutor = new ProgramExecutor(this, _configuration);
                TimeMultiplier = _configuration.TimeMultiplier;
                _programExecutor.Run();
            } catch (Exception e) {
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _logger.Error(e, "An error occurred during execution");
                }
            }
        }
        Exit();
    }

    public void WaitOne() {
        _okayToContinueEvent.WaitOne();
    }
}