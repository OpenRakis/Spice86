namespace Spice86.Tests.Emulator.Devices.Input.Joystick;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick;

using Xunit;

public sealed class GameportTimerTests {

    [Fact]
    public void DisarmedTimer_ReportsAllAxesInactive() {
        GameportTimer timer = new();

        timer.IsStickAxActive(0).Should().BeFalse();
        timer.IsStickAyActive(0).Should().BeFalse();
        timer.IsStickBxActive(0).Should().BeFalse();
        timer.IsStickByActive(0).Should().BeFalse();
        timer.IsInsideArmedWindow(0).Should().BeFalse();
    }

    [Fact]
    public void Arm_AtCentre_ProducesDeadlineMatchingDosboxStagingFormula() {
        // DOSBox-staging formula: now + (axis + 1) * scalar + offset.
        // For a centred (axis = 0) stick this is now + scalar + offset.
        GameportTimer timer = new();
        VirtualStickState centred = new(0f, 0f, 0f, 0f, 0,
            JoystickHatDirection.Centered, true);
        VirtualJoystickState state = new(centred, VirtualStickState.Disconnected);

        timer.Arm(state, nowMs: 100.0);

        double expectedX = 100.0 + GameportConstants.DefaultAxisXScalar + GameportConstants.DefaultAxisOffsetMs;
        double expectedY = 100.0 + GameportConstants.DefaultAxisYScalar + GameportConstants.DefaultAxisOffsetMs;
        timer.IsStickAxActive(expectedX - 0.001).Should().BeTrue();
        timer.IsStickAxActive(expectedX + 0.001).Should().BeFalse();
        timer.IsStickAyActive(expectedY - 0.001).Should().BeTrue();
        timer.IsStickAyActive(expectedY + 0.001).Should().BeFalse();
    }

    [Fact]
    public void Arm_FullRight_HasLongerXDeadlineThanFullLeft() {
        // axis = +1.0 -> (1+1)*scalar = 2*scalar; axis = -1.0 -> 0.
        GameportTimer timer = new();
        VirtualStickState fullRight = new(1f, 0f, 0f, 0f, 0,
            JoystickHatDirection.Centered, true);
        VirtualStickState fullLeft = new(-1f, 0f, 0f, 0f, 0,
            JoystickHatDirection.Centered, true);

        timer.Arm(new VirtualJoystickState(fullRight, VirtualStickState.Disconnected), 0);
        // At 2*XScalar - epsilon, X must still be active.
        timer.IsStickAxActive(2.0 * GameportConstants.DefaultAxisXScalar).Should().BeTrue();

        timer.Arm(new VirtualJoystickState(fullLeft, VirtualStickState.Disconnected), 0);
        // For full-left, deadline is just OffsetMs -> already passed at 1ms.
        timer.IsStickAxActive(1.0).Should().BeFalse();
    }

    [Fact]
    public void IsInsideArmedWindow_ExpiresAfterLegacyTimeout() {
        GameportTimer timer = new();
        timer.Arm(VirtualJoystickState.Disconnected, nowMs: 0);

        timer.IsInsideArmedWindow(GameportConstants.LegacyResetTimeoutTicks - 0.5).Should().BeTrue();
        timer.IsInsideArmedWindow(GameportConstants.LegacyResetTimeoutTicks + 0.5).Should().BeFalse();
    }

    [Fact]
    public void Disarm_DeactivatesAllAxes() {
        GameportTimer timer = new();
        VirtualStickState fullRight = new(1f, 1f, 1f, 1f, 0,
            JoystickHatDirection.Centered, true);
        timer.Arm(new VirtualJoystickState(fullRight, fullRight), 0);

        timer.Disarm();

        timer.IsStickAxActive(0).Should().BeFalse();
        timer.IsStickAyActive(0).Should().BeFalse();
        timer.IsStickBxActive(0).Should().BeFalse();
        timer.IsStickByActive(0).Should().BeFalse();
        timer.IsInsideArmedWindow(0).Should().BeFalse();
    }
}
