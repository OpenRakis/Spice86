namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Shared.Interfaces;

using System;

public partial class PerformanceViewModel : ViewModelBase, IInternalDebugger {
    private State? _state;
    private readonly IPerformanceMeasurer? _performanceMeasurer;
    private readonly IPauseStatus? _pauseStatus;

    [ObservableProperty]
    private double _averageInstructionsPerSecond;

    public PerformanceViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public PerformanceViewModel(IUIDispatcherTimerFactory iuiDispatcherTimerFactory, IDebuggableComponent programExecutor, IPerformanceMeasurer performanceMeasurer, IPauseStatus pauseStatus) {
        _pauseStatus = pauseStatus;
        programExecutor.Accept(this);
        _performanceMeasurer = performanceMeasurer;
        iuiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.MaxValue, UpdatePerformanceInfo);
    }

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_state is null || _performanceMeasurer is null || _pauseStatus is { IsPaused: true }) {
            return;
        }

        InstructionsExecuted = _state.Cycles;
        _performanceMeasurer.UpdateValue(_state.Cycles);
        AverageInstructionsPerSecond = _performanceMeasurer.AverageValuePerSecond;
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        if(_state is null) {
            _state = component as State;
        }
    }

    [ObservableProperty]
    private double _instructionsExecuted;

}
