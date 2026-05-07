namespace Spice86.Tests.Emulator.Devices.Input.Joystick;

using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Deterministic <see cref="ITimeProvider"/> used by the joystick
/// tests. Time advances only when explicitly told to.
/// </summary>
internal sealed class FakeTimeProvider : ITimeProvider {
    private DateTime _now;

    public FakeTimeProvider(DateTime start) {
        _now = start;
    }

    public DateTime Now => _now;

    public void AdvanceMs(double milliseconds) {
        _now = _now.AddMilliseconds(milliseconds);
    }
}

/// <summary>
/// Fake <see cref="IGuiJoystickEvents"/> used by the joystick tests
/// to drive the Core <c>Gameport</c> device through the same
/// pathway as the production UI: the test calls a <c>Raise*</c>
/// helper, which fires the event handler synchronously (i.e. as if
/// <c>InputEventHub</c> had just dequeued the event onto the
/// emulator thread).
/// </summary>
internal sealed class FakeJoystickEventSource : IGuiJoystickEvents {
    public event EventHandler<JoystickAxisEventArgs>? JoystickAxisChanged;
    public event EventHandler<JoystickButtonEventArgs>? JoystickButtonChanged;
    public event EventHandler<JoystickHatEventArgs>? JoystickHatChanged;
    public event EventHandler<JoystickConnectionEventArgs>? JoystickConnectionChanged;

    public void RaiseConnect(int stickIndex, string name = "Test Stick", string guid = "") {
        JoystickConnectionChanged?.Invoke(this,
            new JoystickConnectionEventArgs(stickIndex, true, name, guid));
    }

    public void RaiseDisconnect(int stickIndex) {
        JoystickConnectionChanged?.Invoke(this,
            new JoystickConnectionEventArgs(stickIndex, false, string.Empty));
    }

    public void RaiseAxis(int stickIndex, JoystickAxis axis, float value) {
        JoystickAxisChanged?.Invoke(this,
            new JoystickAxisEventArgs(stickIndex, axis, value));
    }

    public void RaiseButton(int stickIndex, int buttonIndex, bool pressed) {
        JoystickButtonChanged?.Invoke(this,
            new JoystickButtonEventArgs(stickIndex, buttonIndex, pressed));
    }

    public void RaiseHat(int stickIndex, JoystickHatDirection direction) {
        JoystickHatChanged?.Invoke(this,
            new JoystickHatEventArgs(stickIndex, direction));
    }
}

