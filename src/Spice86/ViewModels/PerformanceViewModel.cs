namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using System;

public partial class PerformanceViewModel : ViewModelBase {
    private readonly IPerformanceMeasureReader _cpuPerformanceReader;
    private readonly State _state;

    [ObservableProperty]
    private double _averageInstructionsPerSecond;

    private bool _isPaused;

    public PerformanceViewModel(State state, IPauseHandler pauseHandler,
        IUIDispatcher uiDispatcher, IPerformanceMeasureReader cpuPerfReader) {
        _cpuPerformanceReader = cpuPerfReader;
        pauseHandler.Paused += () => uiDispatcher.Post(() => _isPaused = true);
        pauseHandler.Resumed += () => uiDispatcher.Post(() => _isPaused = false);
        _state = state;
        _isPaused = pauseHandler.IsPaused;
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromSeconds(0.4),
            DispatcherPriority.Background, UpdatePerformanceInfo);
    }

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_isPaused) {
            return;
        }

        InstructionsExecuted = _state.Cycles;
        AverageInstructionsPerSecond = _cpuPerformanceReader.AverageValuePerSecond;
        InstructionsPerMillisecond = _cpuPerformanceReader.ValuePerMillisecond;
    }

    [ObservableProperty]
    private double _instructionsPerMillisecond;

    [ObservableProperty]
    private double _instructionsExecuted;
}