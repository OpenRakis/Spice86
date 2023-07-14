namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;

using System;

public partial class PerformanceViewModel : ViewModelBase {
    private readonly DispatcherTimer? _timer;
    private readonly State? _state;

    private DateTimeOffset _lastUpdateTime;


    [ObservableProperty]
    private double _averageInstructionsPerSecond;

    private long _instructionsPerSecondSampleNumber;

    public PerformanceViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public PerformanceViewModel(State state) {
        _state = state;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.MaxValue, UpdatePerformanceInfo);
        _timer.Start();
    }
    
    private static double ApproxRollingAverage(double currentAverage, double instructionsPerSecond, long instructionsPerSecondSampleNumber) {
        currentAverage -= currentAverage / instructionsPerSecondSampleNumber;
        currentAverage += instructionsPerSecond / instructionsPerSecondSampleNumber;

        return currentAverage;
    }

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_state is null) {
            return;
        }
        if (_lastUpdateTime != DateTimeOffset.MinValue) {
            InstructionsPerSecond = _state.Cycles - InstructionsExecuted;
            if (double.IsNaN(AverageInstructionsPerSecond)) {
                AverageInstructionsPerSecond = InstructionsPerSecond;
            }
            AverageInstructionsPerSecond = ApproxRollingAverage(AverageInstructionsPerSecond, InstructionsPerSecond,
                _instructionsPerSecondSampleNumber++);
        }
        _lastUpdateTime = DateTimeOffset.Now;
        InstructionsExecuted = _state.Cycles;
    }

    [ObservableProperty]
    private long _instructionsExecuted;

    [ObservableProperty]
    private long _instructionsPerSecond = -1;
}
