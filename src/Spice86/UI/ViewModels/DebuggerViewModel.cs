namespace Spice86.UI.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Emulator.VM;

using System;

public partial class DebuggerViewModel : ObservableObject {
    [ObservableProperty]
    private Machine? _machine;
    private readonly DispatcherTimer? _timer;

    public DebuggerViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public DebuggerViewModel(Machine machine) {
        _machine = machine;
        _timer = new(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateMachine);
        _timer.Start();
    }

    public void UpdateMachine(object? sender, EventArgs e) {
        if (_machine is null || !_machine.IsPaused) {
            return;
        }
        Machine? machine = _machine;
        Machine = null;
        Machine = machine;
        Memory = null;
        Memory = Convert.ToHexString(machine.Memory.Ram);
    }

    [ObservableProperty]
    private string? _memory;
}
