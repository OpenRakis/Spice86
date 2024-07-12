namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Messages;

using System;

public partial class PerformanceViewModel : ViewModelBase, IInternalDebugger {
    private State? _state;
    private readonly IPerformanceMeasurer _performanceMeasurer;
    private readonly IMessenger _messenger;
    private bool _isPaused;

    [ObservableProperty]
    private double _averageInstructionsPerSecond;
    
    public PerformanceViewModel(IMessenger messenger, IUIDispatcherTimerFactory uiDispatcherTimerFactory, IDebuggableComponent programExecutor, IPerformanceMeasurer performanceMeasurer) : base() {
        _messenger = messenger;
        _messenger.Register<PauseStatusChangedMessage>(this, (_, message) => _isPaused = message.IsPaused);
        programExecutor.Accept(this);
        _performanceMeasurer = performanceMeasurer;
        uiDispatcherTimerFactory.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.MaxValue, UpdatePerformanceInfo);
    }

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_state is null || _isPaused) {
            return;
        }

        InstructionsExecuted = _state.Cycles;
        _performanceMeasurer.UpdateValue(_state.Cycles);
        AverageInstructionsPerSecond = _performanceMeasurer.AverageValuePerSecond;
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        _state ??= component as State;
    }

    public bool NeedsToVisitEmulator => _state is null;

    [ObservableProperty]
    private double _instructionsExecuted;

}
