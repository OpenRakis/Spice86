namespace Spice86.UI.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Emulator.VM;

public partial class WPFPerformanceViewModel : ObservableObject {
    private readonly DispatcherTimer? _timer;
    private readonly Machine? _machine;

    public WPFPerformanceViewModel() {
        if (!DesignerProperties.GetIsInDesignMode(Application.Current.MainWindow)) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public WPFPerformanceViewModel(Machine machine) {
        _machine = machine;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdatePerformanceInfo, Application.Current.Dispatcher);
        _timer.Start();
    }


    private DateTimeOffset _lastUpdateTime;

    private void UpdatePerformanceInfo(object? sender, EventArgs e) {
        if (_machine is null) {
            return;
        }
        if (DateTimeOffset.Now - _lastUpdateTime >= TimeSpan.FromSeconds(1)) {
            if (_lastUpdateTime != DateTimeOffset.MinValue) {
                InstructionsPerSecond = _machine.Cpu.State.Cycles - InstructionsExecuted;
            }
            _lastUpdateTime = DateTimeOffset.Now;
        }
        InstructionsExecuted = _machine.Cpu.State.Cycles;
    }

    [ObservableProperty]
    private long _instructionsExecuted;

    [ObservableProperty]
    private long _instructionsPerSecond = -1;

}
