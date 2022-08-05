namespace Spice86.UI.ViewModels;

using ReactiveUI;

using Spice86.Core.Emulator.VM;

using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

public partial class WPFPerformanceViewModel : ReactiveObject {
    private readonly DispatcherTimer? _timer;
    private readonly Machine? _machine;
    private readonly WPFMainWindowViewModel? _mainViewModel;
    private Dictionary<uint, long> _framesRendered = new();


    public WPFPerformanceViewModel() {
        if (!DesignerProperties.GetIsInDesignMode(Application.Current.MainWindow)) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public WPFPerformanceViewModel(Machine machine, WPFMainWindowViewModel mainViewModel) {
        _mainViewModel = mainViewModel;
        _machine = machine;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Normal, UpdatePerformanceInfo, Application.Current.Dispatcher);
        _timer.Start();
    }


    private DateTimeOffset _lastUpdateTime;

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_machine is null) {
            return;
        }
        if (DateTimeOffset.Now - _lastUpdateTime >= TimeSpan.FromSeconds(1)) {
            if (_lastUpdateTime != DateTimeOffset.MinValue) {
                InstructionsPerSecond = _machine.Cpu.State.Cycles - InstructionsExecuted;
                if (_mainViewModel is not null) {
                    FramesPerSecond = _mainViewModel.VideoBuffers
                        .Select(x => x.FramesRendered - _framesRendered
                            .GetValueOrDefault(x.Address))
                                .Average(x => x);
                    VideoBuffersLastFrameRenderTime = _mainViewModel.VideoBuffers.Average(x => x.LastFrameRenderTime);

                }
            }
            _lastUpdateTime = DateTimeOffset.Now;
        }
        InstructionsExecuted = _machine.Cpu.State.Cycles;
        if (_mainViewModel is not null) {
            _framesRendered = new(_mainViewModel.VideoBuffers.Select(x => new KeyValuePair<uint, long>(x.Address, x.FramesRendered)));
        }
    }

    private long _instructionsExecuted;

    public long InstructionsExecuted {
        get => _instructionsExecuted;
        set => this.RaiseAndSetIfChanged(ref _instructionsExecuted, value);
    }

    public long InstructionsPerSecond {
        get => _instructionsPerSecond;
        set => this.RaiseAndSetIfChanged(ref _instructionsPerSecond, value);
    }

    private long _instructionsPerSecond = -1;

    public double FramesPerSecond {
        get => _framesPerSecond;
        set => this.RaiseAndSetIfChanged(ref _framesPerSecond, value);
    }
    public double VideoBuffersLastFrameRenderTime {
        get => _videoBuffersLastFrameRenderTime;
        private set => this.RaiseAndSetIfChanged(ref _videoBuffersLastFrameRenderTime, value);
    }

    private double _framesPerSecond = -1;
    private double _videoBuffersLastFrameRenderTime = -1;
}
