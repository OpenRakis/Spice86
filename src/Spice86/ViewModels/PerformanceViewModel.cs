
namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.VM;
using Spice86.Models;

using System;

public partial class PerformanceViewModel : ObservableObject {
    private readonly DispatcherTimer? _timer;
    private readonly MainWindowViewModel? _mainViewModel;
    private readonly Machine? _machine;

    private Dictionary<uint, long> _framesRendered = new();

    private DateTimeOffset _lastUpdateTime;

    [ObservableProperty]
    private AvaloniaList<Measurement> _cpuHistoryDataPoints = new();

    [ObservableProperty]
    private double _averageInstructionsPerSecond;

    private long _instructionsPerSecondSampleNumber;

    private const int CpuHistoryTimeSpanInMinutes = 10;

    private DateTimeOffset _cpuHistoryFirstUpdate;

    private DateTimeOffset _cpuHistoryLastUpdate;

    public PerformanceViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public PerformanceViewModel(Machine machine, MainWindowViewModel mainViewModel) {
        _mainViewModel = mainViewModel;
        _machine = machine;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Normal, UpdatePerformanceInfo);
        _timer.Start();
    }
    
    private static double ApproxRollingAverage(double currentAverage, double instructionsPerSecond, long instructionsPerSecondSampleNumber) {

        currentAverage -= currentAverage / instructionsPerSecondSampleNumber;
        currentAverage += instructionsPerSecond / instructionsPerSecondSampleNumber;

        return currentAverage;
    }

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_machine is null) {
            return;
        }
        if (DateTimeOffset.Now - _lastUpdateTime >= TimeSpan.FromSeconds(1)) {
            if (_lastUpdateTime != DateTimeOffset.MinValue) {
                InstructionsPerSecond = _machine.Cpu.State.Cycles - InstructionsExecuted;
                if (double.IsNaN(AverageInstructionsPerSecond)) {
                    AverageInstructionsPerSecond = InstructionsPerSecond;
                }
                AverageInstructionsPerSecond = ApproxRollingAverage(AverageInstructionsPerSecond, InstructionsPerSecond,
                    _instructionsPerSecondSampleNumber++);
                if(_mainViewModel?.VideoBuffers.Count > 0) {
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
        UpdateCpuHistory(_lastUpdateTime);
        if(_mainViewModel is not null) {
            _framesRendered = new(_mainViewModel.VideoBuffers.Select(x => new KeyValuePair<uint, long>(x.Address, x.FramesRendered)));
        }
    }

    private void UpdateCpuHistory(DateTimeOffset timeOfUpdate) {
        if(_mainViewModel?.IsPaused is true ||
            InstructionsPerSecond <= 0) {
            return;
        }

        if(CpuHistoryDataPoints.Count is 0) {
            _cpuHistoryFirstUpdate = timeOfUpdate;
        }

        TimeSpan cpuHistoryTimeSpan = _cpuHistoryLastUpdate - _cpuHistoryFirstUpdate;
        if(cpuHistoryTimeSpan > TimeSpan.FromMinutes(CpuHistoryTimeSpanInMinutes)) {
            CpuHistoryDataPoints.Clear();
        }

        CpuHistoryDataPoints.Add(new Measurement()
        {
            Time = CpuHistoryDataPoints.Count + 1,
            Value = InstructionsPerSecond
        });

        _cpuHistoryLastUpdate = timeOfUpdate;
        // Notify AvaloniaUI to make the added points show up.
        OnPropertyChanged(nameof(CpuHistoryDataPoints));
    }

    [ObservableProperty]
    private long _instructionsExecuted;

    [ObservableProperty]
    private long _instructionsPerSecond = -1;

    [ObservableProperty]
    private double _framesPerSecond = -1;

    [ObservableProperty]
    private double _videoBuffersLastFrameRenderTime;
}
