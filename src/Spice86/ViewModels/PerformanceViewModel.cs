namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Interfaces;

using System;

public partial class PerformanceViewModel : ViewModelBase {
    private readonly DispatcherTimer? _timer;
    private readonly State? _state;
    private readonly IPerformanceMeasurer? _performanceMeasurer;
    
    [ObservableProperty]
    private double _averageInstructionsPerSecond;

    public PerformanceViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public PerformanceViewModel(State state, IPerformanceMeasurer performanceMeasurer) {
        _state = state;
        _performanceMeasurer = performanceMeasurer;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.MaxValue, UpdatePerformanceInfo);
        _timer.Start();
    }

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_state is null || _performanceMeasurer is null) {
            return;
        }

        InstructionsExecuted = _state.Cycles;
        _performanceMeasurer.UpdateValue(_state.Cycles);
        InstructionsPerSecond = _performanceMeasurer.ValuePerSecond;
        AverageInstructionsPerSecond = _performanceMeasurer.AverageValuePerSecond;
    }

    [ObservableProperty]
    private double _instructionsExecuted;

    [ObservableProperty]
    private double _instructionsPerSecond = -1;
}
