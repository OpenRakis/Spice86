namespace Spice86.Tests.Emulator.Devices.Input.Joystick;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick;

using Xunit;

public sealed class NullJoystickInputTests {

    [Fact]
    public void GetCurrentState_AlwaysReturnsDisconnected() {
        NullJoystickInput input = new();

        input.GetCurrentState().Should().Be(VirtualJoystickState.Disconnected);
        input.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DisconnectedSnapshot_HasBothSticksDisconnected() {
        VirtualJoystickState state = VirtualJoystickState.Disconnected;

        state.StickA.IsConnected.Should().BeFalse();
        state.StickB.IsConnected.Should().BeFalse();
        state.StickA.X.Should().Be(0f);
        state.StickA.Hat.Should().Be(JoystickHatDirection.Centered);
    }
}
