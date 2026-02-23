
namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels.Services;
using Spice86.Shared.Diagnostics;

using System;

public partial class PerformanceViewModel : ViewModelBase {
    private readonly State _state;

    private readonly PerformanceTracker _performanceTracker;

    [ObservableProperty] private double _averageInstructionsPerSecond;

    [ObservableProperty] private double _instructionsPerMillisecond;

    [ObservableProperty] private double _instructionsExecuted;

    public PerformanceViewModel(State state, IPauseHandler pauseHandler,
        IUIDispatcher uiDispatcher, PerformanceTracker performanceTracker) {
        _performanceTracker = performanceTracker;
        pauseHandler.Paused += () => uiDispatcher.Post(() => {
            _performanceTracker.OnPause();
            AverageInstructionsPerSecond = 0;
            InstructionsPerMillisecond = 0;
        });
        pauseHandler.Resumed += () => uiDispatcher.Post(() => {
            _performanceTracker.OnResume();
            UpdatePerformanceInfo();
        });
        _state = state;

        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromSeconds(1),
            DispatcherPriority.Background, (_, _) => UpdatePerformanceInfo());
    }

    private void UpdatePerformanceInfo() {
        InstructionsExecuted = _state.Cycles;
        _performanceTracker.Update(_state.Cycles);
        AverageInstructionsPerSecond = _performanceTracker.InstructionsPerSecond;
        InstructionsPerMillisecond = _performanceTracker.InstructionsPerSecond / 1000;
    }
}
