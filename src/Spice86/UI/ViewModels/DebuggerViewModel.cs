namespace Spice86.UI.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM;

public partial class DebuggerViewModel : ObservableObject {
    private DispatcherTimer? _timer;

    public DebuggerViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    [ObservableProperty]
    private AvaloniaList<Machine>? _machine = new();

    public DebuggerViewModel(Machine machine) {
        _machine = new(machine);
        _timer = new(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, UpdateMachine);
        _timer.Start();
    }

    public void UpdateMachine(object? sender, EventArgs e) {
        if (_machine is null) {
            return;
        }
        Machine = new(_machine);
    }
}
