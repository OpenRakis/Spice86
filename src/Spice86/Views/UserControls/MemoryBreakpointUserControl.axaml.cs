namespace Spice86.Views.UserControls;

using Avalonia;
using Avalonia.Controls;
using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// UserControl for creating memory breakpoints with configurable fields.
/// </summary>
public partial class MemoryBreakpointUserControl : UserControl {
    /// <summary>
    /// Defines the ShowValueCondition property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowValueConditionProperty =
        AvaloniaProperty.Register<MemoryBreakpointUserControl, bool>(nameof(ShowValueCondition), defaultValue: false);

    /// <summary>
    /// Defines the SelectedBreakpointType property.
    /// </summary>
    public static readonly StyledProperty<BreakPointType> SelectedBreakpointTypeProperty =
        AvaloniaProperty.Register<MemoryBreakpointUserControl, BreakPointType>(nameof(SelectedBreakpointType), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the BreakpointTypes property.
    /// </summary>
    public static readonly StyledProperty<BreakPointType[]?> BreakpointTypesProperty =
        AvaloniaProperty.Register<MemoryBreakpointUserControl, BreakPointType[]?>(nameof(BreakpointTypes));

    /// <summary>
    /// Defines the StartAddress property.
    /// </summary>
    public static readonly StyledProperty<string?> StartAddressProperty =
        AvaloniaProperty.Register<MemoryBreakpointUserControl, string?>(nameof(StartAddress), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the EndAddress property.
    /// </summary>
    public static readonly StyledProperty<string?> EndAddressProperty =
        AvaloniaProperty.Register<MemoryBreakpointUserControl, string?>(nameof(EndAddress), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the ValueCondition property.
    /// </summary>
    public static readonly StyledProperty<string?> ValueConditionProperty =
        AvaloniaProperty.Register<MemoryBreakpointUserControl, string?>(nameof(ValueCondition), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Gets or sets a value indicating whether the value condition field should be shown.
    /// </summary>
    public bool ShowValueCondition {
        get => GetValue(ShowValueConditionProperty);
        set => SetValue(ShowValueConditionProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected breakpoint type.
    /// </summary>
    public BreakPointType SelectedBreakpointType {
        get => GetValue(SelectedBreakpointTypeProperty);
        set => SetValue(SelectedBreakpointTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets the available breakpoint types.
    /// </summary>
    public BreakPointType[]? BreakpointTypes {
        get => GetValue(BreakpointTypesProperty);
        set => SetValue(BreakpointTypesProperty, value);
    }

    /// <summary>
    /// Gets or sets the start address.
    /// </summary>
    public string? StartAddress {
        get => GetValue(StartAddressProperty);
        set => SetValue(StartAddressProperty, value);
    }

    /// <summary>
    /// Gets or sets the end address.
    /// </summary>
    public string? EndAddress {
        get => GetValue(EndAddressProperty);
        set => SetValue(EndAddressProperty, value);
    }

    /// <summary>
    /// Gets or sets the value condition.
    /// </summary>
    public string? ValueCondition {
        get => GetValue(ValueConditionProperty);
        set => SetValue(ValueConditionProperty, value);
    }

    public MemoryBreakpointUserControl() {
        InitializeComponent();
    }
}
