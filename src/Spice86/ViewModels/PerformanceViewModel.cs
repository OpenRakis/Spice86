
namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels.Services;
using Spice86.Shared.Diagnostics;

using System;

public partial class PerformanceViewModel : ViewModelBase, IDisposable {
    private readonly State _state;

    private readonly PerformanceTracker _performanceTracker;

    private readonly DispatcherTimer _updateTimer;

    private readonly IPauseHandler _pauseHandler;

    private readonly Action _onPaused;

    private readonly Action _onResumed;

    private bool _disposed;

    [ObservableProperty] private double _averageInstructionsPerSecond;

    [ObservableProperty] private double _instructionsPerMillisecond;

    [ObservableProperty] private double _instructionsExecuted;

    public PerformanceViewModel(State state, IPauseHandler pauseHandler,
        IUIDispatcher uiDispatcher, PerformanceTracker performanceTracker) {
        _performanceTracker = performanceTracker;
        _pauseHandler = pauseHandler;
        _onPaused = () => uiDispatcher.Post(() => {
            _performanceTracker.OnPause();
            AverageInstructionsPerSecond = 0;
            InstructionsPerMillisecond = 0;
        });
        _onResumed = () => uiDispatcher.Post(() => {
            _performanceTracker.OnResume();
            UpdatePerformanceInfo();
        });
        _pauseHandler.Paused += _onPaused;
        _pauseHandler.Resumed += _onResumed;
        _state = state;

        _updateTimer = DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromSeconds(1),
            DispatcherPriority.Background, (_, _) => UpdatePerformanceInfo());
    }

    private void UpdatePerformanceInfo() {
        InstructionsExecuted = _state.Cycles;
        _performanceTracker.Update(_state.Cycles);
        AverageInstructionsPerSecond = _performanceTracker.InstructionsPerSecond;
        InstructionsPerMillisecond = _performanceTracker.InstructionsPerSecond / 1000;
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _pauseHandler.Paused -= _onPaused;
        _pauseHandler.Resumed -= _onResumed;
        _updateTimer.Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
