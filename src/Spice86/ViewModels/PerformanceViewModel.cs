namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Infrastructure;
using Spice86.Shared.Diagnostics;

using System;

public partial class PerformanceViewModel : ViewModelBase {
    private readonly PerformanceMeasurer _performanceMeasurer;
    private readonly State _state;

    [ObservableProperty]
    private double _averageInstructionsPerSecond;

    private bool _isPaused;
    
    public PerformanceViewModel(State state, IPauseHandler pauseHandler, IUIDispatcher uiDispatcher) {
        pauseHandler.Pausing += () => uiDispatcher.Post(() => _isPaused = true);
        pauseHandler.Resumed += () => uiDispatcher.Post(() => _isPaused = false);
        _state = state;
        _isPaused = pauseHandler.IsPaused;
        _performanceMeasurer = new PerformanceMeasurer();
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.MaxValue, UpdatePerformanceInfo);
    }

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_isPaused) {
            return;
        }

        _performanceMeasurer.UpdateValue(_state.Cycles);
        InstructionsExecuted = _state.Cycles;
        AverageInstructionsPerSecond = _performanceMeasurer.AverageValuePerSecond;
        InstructionsPerMillisecond = _performanceMeasurer.ValuePerMillisecond;
    }

    [ObservableProperty]
    private double _instructionsPerMillisecond;

    [ObservableProperty]
    private double _instructionsExecuted;
}
