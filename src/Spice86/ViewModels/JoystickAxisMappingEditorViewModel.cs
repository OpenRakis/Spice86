namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>Editor ViewModel for a single <see cref="AxisMapping"/>.</summary>
public sealed partial class JoystickAxisMappingEditorViewModel : ViewModelBase {
    [ObservableProperty]
    private int _rawAxisIndex;

    [ObservableProperty]
    private VirtualAxis _target = VirtualAxis.None;

    [ObservableProperty]
    private bool _invert;

    [ObservableProperty]
    private double _scale = 1.0;

    [ObservableProperty]
    private int? _deadzonePercent;

    /// <summary>The available virtual axes for binding.</summary>
    public static IReadOnlyList<VirtualAxis> VirtualAxes { get; } =
        Enum.GetValues<VirtualAxis>();

    /// <summary>Initializes a new editor populated from <paramref name="mapping"/>.</summary>
    public JoystickAxisMappingEditorViewModel(AxisMapping mapping) {
        RawAxisIndex = mapping.RawAxisIndex;
        Target = mapping.Target;
        Invert = mapping.Invert;
        Scale = mapping.Scale;
        DeadzonePercent = mapping.DeadzonePercent;
    }

    /// <summary>Materializes the editor state back into an <see cref="AxisMapping"/>.</summary>
    public AxisMapping ToMapping() {
        return new AxisMapping {
            RawAxisIndex = RawAxisIndex,
            Target = Target,
            Invert = Invert,
            Scale = Scale,
            DeadzonePercent = DeadzonePercent,
        };
    }
}
