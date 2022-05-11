﻿namespace Spice86.UI.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Emulator.VM;

public partial class DebuggerViewModel : ObservableObject {
    [ObservableProperty]
    private Machine? _machine;
    private DispatcherTimer? _timer;

    public DebuggerViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }



    public DebuggerViewModel(Machine machine) {
        _machine = machine;
        _timer = new(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, UpdateMachine);
        _timer.Start();
    }

    public void UpdateMachine(object? sender, EventArgs e) {
        Machine? machine = _machine;
        Machine = null;
        Machine = machine;
    }
}
