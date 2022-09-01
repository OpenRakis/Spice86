namespace Spice86.AvaloniaUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.VM;

using System;
using System.Linq;

public partial class PerformanceViewModel : ObservableObject {
    private readonly DispatcherTimer? _timer;
    private readonly MainWindowViewModel? _mainViewModel;
    private readonly Machine? _machine;

    public PerformanceViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public PerformanceViewModel(Machine machine, MainWindowViewModel mainViewModel) {
        _mainViewModel = mainViewModel;
        _machine = machine;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Normal, UpdatePerformanceInfo);
        _timer.Start();
    }

    private Dictionary<uint, long> _framesRendered = new();

    private DateTimeOffset _lastUpdateTime;

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_machine is null) {
            return;
        }
        if (DateTimeOffset.Now - _lastUpdateTime >= TimeSpan.FromSeconds(1)) {
            if (_lastUpdateTime != DateTimeOffset.MinValue) {
                InstructionsPerSecond = _machine.Cpu.State.Cycles - InstructionsExecuted;
                if(_mainViewModel is not null) {
                    FramesPerSecond = _mainViewModel.VideoBuffers
                        .Select(x => x.FramesRendered - _framesRendered
                            .GetValueOrDefault(x.Address))
                                .Average(x => x);
                    VideoBuffersLastFrameRenderTime = _mainViewModel.VideoBuffers.Average(x => x.LastFrameRenderTimeMs);

                }
            }
            _lastUpdateTime = DateTimeOffset.Now;
        }
        InstructionsExecuted = _machine.Cpu.State.Cycles;
        if(_mainViewModel is not null) {
            _framesRendered = new(_mainViewModel.VideoBuffers.Select(x => new KeyValuePair<uint, long>(x.Address, x.FramesRendered)));
        }
    }

    [ObservableProperty]
    private long _instructionsExecuted;

    [ObservableProperty]
    private long _instructionsPerSecond = -1;

    [ObservableProperty]
    private double _framesPerSecond = -1;

    [ObservableProperty]
    private double _videoBuffersLastFrameRenderTime = 0;

}
