namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Identifies the type of breakpoint a tab represents.
/// </summary>
public enum BreakpointTabType {
    Cycles,
    Memory,
    Execution,
    Interrupt,
    IoPort
}

public partial class BreakpointTypeTabItemViewModel : ViewModelBase {
    public BreakpointTabType TabType { get; init; }

    public string Header => TabType switch {
        BreakpointTabType.Cycles => "Cycles",
        BreakpointTabType.Memory => "Memory",
        BreakpointTabType.Execution => "Execution",
        BreakpointTabType.Interrupt => "Interrupt",
        BreakpointTabType.IoPort => "I/O Port",
        _ => TabType.ToString()
    };

    [ObservableProperty]
    private bool _isSelected;
}
