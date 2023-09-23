﻿namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Shared.Interfaces;

using System;

public partial class PerformanceViewModel : ViewModelBase, IEmulatorDebugger {
    private State? _state;
    private readonly IPerformanceMeasurer? _performanceMeasurer;
    
    [ObservableProperty]
    private double _averageInstructionsPerSecond;

    public PerformanceViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public PerformanceViewModel(IUIDispatcherTimer uiDispatcherTimer, IDebuggableComponent programExecutor, IPerformanceMeasurer performanceMeasurer) {
        programExecutor.Accept(this);
        _performanceMeasurer = performanceMeasurer;
        uiDispatcherTimer.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.MaxValue, UpdatePerformanceInfo);
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

    public void VisitMainMemory(IMemory memory) {
    }
    
    public void VisitCpuState(State state) => _state = state;
    
    public void VisitVgaRenderer(IVgaRenderer vgaRenderer) {
    }

    public void VisitVideoState(IVideoState videoState) {
    }

    public void VisitDacPalette(ArgbPalette argbPalette) {
    }

    public void VisitDacRegisters(DacRegisters dacRegisters) {
    }

    public void VisitVgaCard(VgaCard vgaCard) {
    }

    public void VisitCpu(Cpu cpu) {
    }
}
