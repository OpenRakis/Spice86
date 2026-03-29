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
/// 2-button PC gameport joystick. Shows real-time axis positions and button states,
/// and allows manual input via sliders and buttons for testing without a physical controller.
/// </summary>
public partial class JoystickPanelViewModel : ViewModelBase {
    private readonly Action<JoystickStateEventArgs> _sendJoystickAState;

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
    /// Crosshair X position in the stick area, calculated from AxisAX. Range: 0 to (area width - indicator size).
    /// </summary>
    [ObservableProperty]
    private double _crosshairX;

    /// <summary>
    /// Crosshair Y position in the stick area, calculated from AxisAY. Range: 0 to (area height - indicator size).
    /// </summary>
    [ObservableProperty]
    private double _crosshairY;

    /// <summary>
    /// Size of the stick area for crosshair calculation.
    /// </summary>
    private const double StickAreaSize = 200.0;

    /// <summary>
    /// Size of the crosshair indicator.
    /// </summary>
    private const double CrosshairSize = 16.0;

    private readonly Joystick _joystick;

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
}
