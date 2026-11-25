namespace Spice86.Tests.Emulator.Devices;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.VM;

using Xunit;

/// <summary>
/// Checks <see cref="Machine.CaptureDeviceState" /> and <see cref="Machine.RestoreDeviceState" /> on a whole
/// machine: capture, change the devices through the machine's ports, restore, compare with the capture.
/// </summary>
public sealed class MachineDeviceStateTests {
    private const ushort PrimaryPicDataPort = 0x21;
    private const ushort PitChannel0Port = 0x40;
    private const ushort PitControlPort = 0x43;
    private const byte Channel0RateGenerator = 0x34;

    [Fact]
    public void RestoreAfterChangesThroughPortsGivesTheCapturedDeviceState() {
        // Arrange
        using Spice86Creator creator = new("externalint", enablePit: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        MachineDeviceState captured = machine.CaptureDeviceState();

        // Act
        ChangePicAndPitThroughPorts(machine);
        MachineDeviceState changed = machine.CaptureDeviceState();
        machine.RestoreDeviceState(captured);

        // Assert: the controllers are equal. The timer's times follow the machine's clock, which moved on
        // between capture and restore, so the timer is compared without them.
        MachineDeviceState restored = machine.CaptureDeviceState();
        changed.Pic.Should().NotBe(captured.Pic);
        restored.Pic.Should().Be(captured.Pic);
        WithoutTimes(changed.Pit).Should().NotBe(WithoutTimes(captured.Pit));
        WithoutTimes(restored.Pit).Should().Be(WithoutTimes(captured.Pit));
    }

    [Fact]
    public void CaptureOfOneDeviceLeavesTheOtherOutAndRestoreLeavesItAsItIs() {
        // Arrange
        using Spice86Creator creator = new("externalint", enablePit: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        MachineDeviceState picOnly = machine.CaptureDeviceState(MachineDevices.Pic);
        DualPicState picBefore = machine.DualPic.CaptureState();

        // Act
        ChangePicAndPitThroughPorts(machine);
        PitTimerState pitAfterChange = machine.Timer.CaptureState();
        machine.RestoreDeviceState(picOnly);

        // Assert: the controllers are back, the timer keeps the change
        picOnly.Pit.Should().BeNull();
        picOnly.Dos.Should().BeNull();
        picOnly.Pic.Should().NotBeNull();
        machine.DualPic.CaptureState().Should().Be(picBefore);
        machine.Timer.CaptureState().Channel0.Should().Be(pitAfterChange.Channel0);
    }

    [Fact]
    public void CaptureOfNoDeviceHoldsNothing() {
        // Arrange
        using Spice86Creator creator = new("externalint", enablePit: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();

        // Act
        MachineDeviceState none = dependencyInjection.Machine.CaptureDeviceState(MachineDevices.None);

        // Assert
        none.Pic.Should().BeNull();
        none.Pit.Should().BeNull();
        none.Dos.Should().BeNull();
    }

    private static void ChangePicAndPitThroughPorts(Machine machine) {
        machine.IoPortDispatcher.WriteByte(PrimaryPicDataPort, 0xA5);
        machine.IoPortDispatcher.WriteByte(PitControlPort, Channel0RateGenerator);
        machine.IoPortDispatcher.WriteByte(PitChannel0Port, 0x34);
        machine.IoPortDispatcher.WriteByte(PitChannel0Port, 0x12);
    }

    /// <summary>
    /// The timer state with every time set to zero, for comparisons across a clock that moved.
    /// </summary>
    private static PitTimerState WithoutTimes(PitTimerState? state) {
        PitTimerState value = state ?? throw new InvalidOperationException("The timer state was not captured.");
        return value with {
            Channel0 = value.Channel0 with { Start = 0 },
            Channel1 = value.Channel1 with { Start = 0 },
            Channel2 = value.Channel2 with { Start = 0 },
            CapturedAtMs = 0,
            Channel0EventDelayMs = null
        };
    }
}
