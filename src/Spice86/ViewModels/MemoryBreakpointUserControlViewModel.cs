namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// ViewModel for the MemoryBreakpointUserControl.
/// </summary>
public partial class MemoryBreakpointUserControlViewModel : ObservableObject {
    private bool _showValueCondition;
    private BreakPointType _selectedBreakpointType;
    private BreakPointType[]? _breakpointTypes;
    private string? _startAddress;
    private string? _endAddress;
    private string? _valueCondition;

    /// <summary>
    /// Gets or sets a value indicating whether the value condition field should be shown.
    /// </summary>
    public bool ShowValueCondition {
        get => _showValueCondition;
        set => SetProperty(ref _showValueCondition, value);
    }

    /// <summary>
    /// Gets or sets the selected breakpoint type.
    /// </summary>
    public BreakPointType SelectedBreakpointType {
        get => _selectedBreakpointType;
        set => SetProperty(ref _selectedBreakpointType, value);
    }

    /// <summary>
    /// Gets or sets the available breakpoint types.
    /// </summary>
    public BreakPointType[]? BreakpointTypes {
        get => _breakpointTypes;
        set => SetProperty(ref _breakpointTypes, value);
    }

    /// <summary>
    /// Gets or sets the start address.
    /// </summary>
    public string? StartAddress {
        get => _startAddress;
        set => SetProperty(ref _startAddress, value);
    }

    /// <summary>
    /// Gets or sets the end address.
    /// </summary>
    public string? EndAddress {
        get => _endAddress;
        set => SetProperty(ref _endAddress, value);
    }

    /// <summary>
    /// Gets or sets the value condition.
    /// </summary>
    public string? ValueCondition {
        get => _valueCondition;
        set => SetProperty(ref _valueCondition, value);
    }
}
