namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Shared.Emulator.Input.Joystick.Mapping;

/// <summary>Editor ViewModel for a single <see cref="ButtonMapping"/>.</summary>
public sealed partial class JoystickButtonMappingEditorViewModel : ViewModelBase {
    [ObservableProperty]
    private int _rawButtonIndex;

    [ObservableProperty]
    private VirtualButton _target = VirtualButton.None;

    [ObservableProperty]
    private bool _autoFire;

    /// <summary>The available virtual buttons for binding.</summary>
    public static IReadOnlyList<VirtualButton> VirtualButtons { get; } =
        Enum.GetValues<VirtualButton>();

    /// <summary>Initializes a new editor populated from <paramref name="mapping"/>.</summary>
    public JoystickButtonMappingEditorViewModel(ButtonMapping mapping) {
        RawButtonIndex = mapping.RawButtonIndex;
        Target = mapping.Target;
        AutoFire = mapping.AutoFire;
    }

    /// <summary>Materializes the editor state back into a <see cref="ButtonMapping"/>.</summary>
    public ButtonMapping ToMapping() {
        return new ButtonMapping {
            RawButtonIndex = RawButtonIndex,
            Target = Target,
            AutoFire = AutoFire,
        };
    }
}
