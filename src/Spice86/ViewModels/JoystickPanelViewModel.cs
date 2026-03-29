namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Shared.Emulator.Joystick;
using Spice86.ViewModels.Services;

using System;

/// <summary>
/// ViewModel for the joystick test panel, displaying a visual representation of a classic
/// 2-button PC gameport joystick with a stick area and two fire buttons.
/// Supports input via host mouse (click/drag in stick area, click fire buttons)
/// and host keyboard (arrow keys for stick, Z/X for fire buttons).
/// </summary>
public partial class JoystickPanelViewModel : ViewModelBase {
    private readonly Action<JoystickStateEventArgs> _sendJoystickAState;
    private readonly Joystick _joystick;

    /// <summary>
    /// X axis position for joystick A, from 0.0 (left) to 1.0 (right).
    /// </summary>
    [ObservableProperty]
    private double _axisAX = 0.5;

    /// <summary>
    /// Y axis position for joystick A, from 0.0 (up) to 1.0 (down).
    /// </summary>
    [ObservableProperty]
    private double _axisAY = 0.5;

    /// <summary>
    /// Whether button 1 on joystick A is pressed.
    /// </summary>
    [ObservableProperty]
    private bool _buttonA1Pressed;

    /// <summary>
    /// Whether button 2 on joystick A is pressed.
    /// </summary>
    [ObservableProperty]
    private bool _buttonA2Pressed;

    /// <summary>
    /// Whether joystick A is enabled (connected to the emulated gameport).
    /// </summary>
    [ObservableProperty]
    private bool _joystickAEnabled = true;

    /// <summary>
    /// The last raw byte value read from the gameport (port 0x201), for diagnostic display.
    /// </summary>
    [ObservableProperty]
    private string _lastPortReadValue = "0xFF";

    /// <summary>
    /// Crosshair X position in the stick area (pixels from left).
    /// </summary>
    [ObservableProperty]
    private double _crosshairX;

    /// <summary>
    /// Crosshair Y position in the stick area (pixels from top).
    /// </summary>
    [ObservableProperty]
    private double _crosshairY;

    /// <summary>
    /// Human-readable description of axis bit 0 (Joystick A, X axis) status.
    /// </summary>
    [ObservableProperty]
    private string _axisBit0Status = "-";

    /// <summary>
    /// Human-readable description of axis bit 1 (Joystick A, Y axis) status.
    /// </summary>
    [ObservableProperty]
    private string _axisBit1Status = "-";

    /// <summary>
    /// Human-readable description of axis bit 2 (Joystick B, X axis) status.
    /// </summary>
    [ObservableProperty]
    private string _axisBit2Status = "-";

    /// <summary>
    /// Human-readable description of axis bit 3 (Joystick B, Y axis) status.
    /// </summary>
    [ObservableProperty]
    private string _axisBit3Status = "-";

    /// <summary>
    /// Human-readable description of button bit 4 (button A1) status.
    /// </summary>
    [ObservableProperty]
    private string _buttonBit4Status = "-";

    /// <summary>
    /// Human-readable description of button bit 5 (button A2) status.
    /// </summary>
    [ObservableProperty]
    private string _buttonBit5Status = "-";

    /// <summary>
    /// Human-readable description of button bit 6 (button B1) status.
    /// </summary>
    [ObservableProperty]
    private string _buttonBit6Status = "-";

    /// <summary>
    /// Human-readable description of button bit 7 (button B2) status.
    /// </summary>
    [ObservableProperty]
    private string _buttonBit7Status = "-";

    /// <summary>
    /// Size of the stick area for crosshair calculation.
    /// </summary>
    public const double StickAreaSize = 200.0;

    /// <summary>
    /// Size of the crosshair indicator.
    /// </summary>
    private const double CrosshairSize = 16.0;

    /// <summary>
    /// Step size for keyboard-driven stick movement per key press.
    /// </summary>
    private const double KeyboardStepSize = 0.05;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoystickPanelViewModel"/> class.
    /// </summary>
    /// <param name="sendJoystickAState">Action to send joystick A state changes to the emulator.</param>
    /// <param name="joystick">The emulated joystick device for reading port state.</param>
    public JoystickPanelViewModel(Action<JoystickStateEventArgs> sendJoystickAState, Joystick joystick) {
        _sendJoystickAState = sendJoystickAState;
        _joystick = joystick;

        UpdateCrosshairPosition();

        DispatcherTimerStarter.StartNewDispatcherTimer(
            TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Background,
            OnTimerTick);
    }

    private void OnTimerTick(object? sender, EventArgs e) {
        UpdatePortReadDisplay();
    }

    private void UpdatePortReadDisplay() {
        byte portValue = _joystick.ReadByte(0x201);
        LastPortReadValue = $"0x{portValue:X2}";

        AxisBit0Status = (portValue & 0x01) != 0 ? "1 (running)" : "0 (expired)";
        AxisBit1Status = (portValue & 0x02) != 0 ? "1 (running)" : "0 (expired)";
        AxisBit2Status = (portValue & 0x04) != 0 ? "1 (running)" : "0 (expired)";
        AxisBit3Status = (portValue & 0x08) != 0 ? "1 (running)" : "0 (expired)";
        ButtonBit4Status = (portValue & 0x10) != 0 ? "1 (released)" : "0 (pressed)";
        ButtonBit5Status = (portValue & 0x20) != 0 ? "1 (released)" : "0 (pressed)";
        ButtonBit6Status = (portValue & 0x40) != 0 ? "1 (released)" : "0 (pressed)";
        ButtonBit7Status = (portValue & 0x80) != 0 ? "1 (released)" : "0 (pressed)";
    }

    /// <summary>
    /// Sends the current joystick A state to the emulator through the event pipeline.
    /// </summary>
    private void SendJoystickState() {
        if (!JoystickAEnabled) {
            return;
        }

        JoystickStateEventArgs state = new(AxisAX, AxisAY, ButtonA1Pressed, ButtonA2Pressed);
        _sendJoystickAState(state);
    }

    partial void OnAxisAXChanged(double value) {
        UpdateCrosshairPosition();
        SendJoystickState();
    }

    partial void OnAxisAYChanged(double value) {
        UpdateCrosshairPosition();
        SendJoystickState();
    }

    partial void OnButtonA1PressedChanged(bool value) => SendJoystickState();
    partial void OnButtonA2PressedChanged(bool value) => SendJoystickState();

    partial void OnJoystickAEnabledChanged(bool value) {
        if (value) {
            SendJoystickState();
        }
    }

    private void UpdateCrosshairPosition() {
        double maxOffset = StickAreaSize - CrosshairSize;
        CrosshairX = AxisAX * maxOffset;
        CrosshairY = AxisAY * maxOffset;
    }

    /// <summary>
    /// Centers both axes (0.5, 0.5).
    /// </summary>
    [RelayCommand]
    private void CenterStick() {
        AxisAX = 0.5;
        AxisAY = 0.5;
    }

    /// <summary>
    /// Called by the view when the user clicks or drags within the stick area.
    /// Converts pixel coordinates to axis values.
    /// </summary>
    /// <param name="x">X position in the stick area, in pixels.</param>
    /// <param name="y">Y position in the stick area, in pixels.</param>
    public void SetStickPositionFromMouse(double x, double y) {
        AxisAX = Math.Clamp(x / StickAreaSize, 0.0, 1.0);
        AxisAY = Math.Clamp(y / StickAreaSize, 0.0, 1.0);
    }

    /// <summary>
    /// Moves the stick left by one keyboard step.
    /// </summary>
    [RelayCommand]
    private void StickLeft() {
        AxisAX = Math.Clamp(AxisAX - KeyboardStepSize, 0.0, 1.0);
    }

    /// <summary>
    /// Moves the stick right by one keyboard step.
    /// </summary>
    [RelayCommand]
    private void StickRight() {
        AxisAX = Math.Clamp(AxisAX + KeyboardStepSize, 0.0, 1.0);
    }

    /// <summary>
    /// Moves the stick up by one keyboard step.
    /// </summary>
    [RelayCommand]
    private void StickUp() {
        AxisAY = Math.Clamp(AxisAY - KeyboardStepSize, 0.0, 1.0);
    }

    /// <summary>
    /// Moves the stick down by one keyboard step.
    /// </summary>
    [RelayCommand]
    private void StickDown() {
        AxisAY = Math.Clamp(AxisAY + KeyboardStepSize, 0.0, 1.0);
    }
}
