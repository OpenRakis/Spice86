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
/// Mutable <see cref="IGameportInputSource"/> stub for the joystick
/// tests. Tests set <see cref="Current"/> and assert what the
/// gameport observes.
/// </summary>
internal sealed class FakeJoystickInput : IGameportInputSource {
    public string DisplayName => "Fake (test)";

    public VirtualJoystickState Current { get; set; } =
        VirtualJoystickState.Disconnected;

    public VirtualJoystickState GetCurrentState() {
        return Current;
    }
}
