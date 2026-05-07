namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;

using System.Collections.ObjectModel;

/// <summary>
/// Editor ViewModel for a single <see cref="JoystickProfile"/>
/// inside a <see cref="JoystickMapperViewModel"/>.
/// </summary>
public sealed partial class JoystickProfileEditorViewModel : ViewModelBase {
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _deviceGuid = string.Empty;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private JoystickType _type = JoystickType.Auto;

    [ObservableProperty]
    private int _deadzonePercent = 10;

    [ObservableProperty]
    private bool _useCircularDeadzone;

    [ObservableProperty]
    private bool _swapStickBAxes;

    [ObservableProperty]
    private ObservableCollection<JoystickAxisMappingEditorViewModel> _axes = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveAxisCommand))]
    private JoystickAxisMappingEditorViewModel? _selectedAxis;

    [ObservableProperty]
    private ObservableCollection<JoystickButtonMappingEditorViewModel> _buttons = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveButtonCommand))]
    private JoystickButtonMappingEditorViewModel? _selectedButton;

    [ObservableProperty]
    private int _hatRawIndex;

    [ObservableProperty]
    private int _hatTargetStickIndex;

    [ObservableProperty]
    private bool _hatEnabled = true;

    [ObservableProperty]
    private bool _rumbleEnabled = true;

    [ObservableProperty]
    private double _rumbleAmplitudeScale = 1.0;

    [ObservableProperty]
    private bool _midiOnGameportEnabled;

    [ObservableProperty]
    private int? _mpu401BasePort;

    /// <summary>The available joystick personalities for binding.</summary>
    public static IReadOnlyList<JoystickType> JoystickTypes { get; } =
        Enum.GetValues<JoystickType>();

    /// <summary>The available virtual axes for binding.</summary>
    public static IReadOnlyList<VirtualAxis> VirtualAxes { get; } =
        Enum.GetValues<VirtualAxis>();

    /// <summary>The available virtual buttons for binding.</summary>
    public static IReadOnlyList<VirtualButton> VirtualButtons { get; } =
        Enum.GetValues<VirtualButton>();

    /// <summary>
    /// Initializes a new editor populated from <paramref name="profile"/>.
    /// </summary>
    public JoystickProfileEditorViewModel(JoystickProfile profile) {
        Name = profile.Name;
        DeviceGuid = profile.DeviceGuid;
        DeviceName = profile.DeviceName;
        Type = profile.Type;
        DeadzonePercent = profile.DeadzonePercent;
        UseCircularDeadzone = profile.UseCircularDeadzone;
        SwapStickBAxes = profile.SwapStickBAxes;
        foreach (AxisMapping axis in profile.Axes) {
            Axes.Add(new JoystickAxisMappingEditorViewModel(axis));
        }
        foreach (ButtonMapping button in profile.Buttons) {
            Buttons.Add(new JoystickButtonMappingEditorViewModel(button));
        }
        HatRawIndex = profile.Hat.RawHatIndex;
        HatTargetStickIndex = profile.Hat.TargetStickIndex;
        HatEnabled = profile.Hat.Enabled;
        RumbleEnabled = profile.Rumble.Enabled;
        RumbleAmplitudeScale = profile.Rumble.AmplitudeScale;
        MidiOnGameportEnabled = profile.MidiOnGameport.Enabled;
        Mpu401BasePort = profile.MidiOnGameport.Mpu401BasePort;
    }

    /// <summary>Adds a new empty axis binding row.</summary>
    [RelayCommand]
    public void AddAxis() {
        JoystickAxisMappingEditorViewModel editor = new(new AxisMapping());
        Axes.Add(editor);
        SelectedAxis = editor;
    }

    /// <summary>Removes the currently selected axis binding row.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveAxis))]
    public void RemoveAxis() {
        if (SelectedAxis is null) {
            return;
        }
        Axes.Remove(SelectedAxis);
        SelectedAxis = Axes.Count > 0 ? Axes[0] : null;
    }

    private bool CanRemoveAxis() => SelectedAxis is not null;

    /// <summary>Adds a new empty button binding row.</summary>
    [RelayCommand]
    public void AddButton() {
        JoystickButtonMappingEditorViewModel editor = new(new ButtonMapping());
        Buttons.Add(editor);
        SelectedButton = editor;
    }

    /// <summary>Removes the currently selected button binding row.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveButton))]
    public void RemoveButton() {
        if (SelectedButton is null) {
            return;
        }
        Buttons.Remove(SelectedButton);
        SelectedButton = Buttons.Count > 0 ? Buttons[0] : null;
    }

    private bool CanRemoveButton() => SelectedButton is not null;

    /// <summary>Materializes the editor state back into a <see cref="JoystickProfile"/>.</summary>
    public JoystickProfile ToProfile() {
        JoystickProfile profile = new() {
            Name = Name,
            DeviceGuid = DeviceGuid,
            DeviceName = DeviceName,
            Type = Type,
            DeadzonePercent = DeadzonePercent,
            UseCircularDeadzone = UseCircularDeadzone,
            SwapStickBAxes = SwapStickBAxes,
            Hat = new HatMapping {
                RawHatIndex = HatRawIndex,
                TargetStickIndex = HatTargetStickIndex,
                Enabled = HatEnabled,
            },
            Rumble = new RumbleMapping {
                Enabled = RumbleEnabled,
                AmplitudeScale = RumbleAmplitudeScale,
            },
            MidiOnGameport = new MidiOnGameportSettings {
                Enabled = MidiOnGameportEnabled,
                Mpu401BasePort = Mpu401BasePort,
            },
        };
        foreach (JoystickAxisMappingEditorViewModel editor in Axes) {
            profile.Axes.Add(editor.ToMapping());
        }
        foreach (JoystickButtonMappingEditorViewModel editor in Buttons) {
            profile.Buttons.Add(editor.ToMapping());
        }
        return profile;
    }

    /// <inheritdoc />
    public override string ToString() {
        return string.IsNullOrWhiteSpace(Name) ? "(unnamed profile)" : Name;
    }
}
