namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.Shared.Interfaces;

using System;

public partial class PerformanceViewModel : ViewModelBase {
    private readonly IPerformanceMeasurer _performanceMeasurer;
    private readonly State _state;

    [ObservableProperty]
    private double _averageInstructionsPerSecond;

    private bool _isPaused;
    
    public PerformanceViewModel(State state, IPauseHandler pauseHandler, IUIDispatcherTimerFactory uiDispatcherTimerFactory, IPerformanceMeasurer performanceMeasurer) {
        pauseHandler.Pausing += () => _isPaused = true;
        _state = state;
        _isPaused = pauseHandler.IsPaused;
        _performanceMeasurer = performanceMeasurer;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.MaxValue, UpdatePerformanceInfo);
    }

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_isPaused) {
            return;
        }

        InstructionsExecuted = _state.Cycles;
        _performanceMeasurer.UpdateValue(_state.Cycles);
        AverageInstructionsPerSecond = _performanceMeasurer.AverageValuePerSecond;
    }

    [ObservableProperty]
    private double _instructionsExecuted;
}
