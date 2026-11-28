namespace Spice86.Tests.Emulator.Devices;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
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
    private const ushort DmaChannel1PagePort = 0x83;
    private const ushort KeyboardControllerCommandPort = 0x64;
    private const byte DisableA20 = 0xDD;
    private const byte EnableA20 = 0xDF;
    private const byte QueryA20Status = 2;
    private const byte AllocateExtendedMemoryBlock = 0x09;

    [Fact]
    public void RestoreAfterChangesThroughPortsGivesTheCapturedDeviceState() {
        // Arrange
        using Spice86Creator creator = new("externalint", enablePit: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        MachineDeviceState captured = machine.CaptureDeviceState();

        // Act
        ChangeDevicesThroughPorts(machine);
        MachineDeviceState changed = machine.CaptureDeviceState();
        machine.RestoreDeviceState(captured);

        // Assert: the controllers are equal. The timer's times follow the machine's clock, which moved on
        // between capture and restore, so the timer is compared without them.
        MachineDeviceState restored = machine.CaptureDeviceState();
        changed.Pic.Should().NotBe(captured.Pic);
        restored.Pic.Should().Be(captured.Pic);
        captured.Vga.Should().NotBeNull();
        changed.Dma.Should().NotBe(captured.Dma);
        restored.Dma.Should().Be(captured.Dma);
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
        ChangeDevicesThroughPorts(machine);
        PitTimerState pitAfterChange = machine.Timer.CaptureState();
        machine.RestoreDeviceState(picOnly);

        // Assert: the controllers are back, the timer keeps the change
        picOnly.Pit.Should().BeNull();
        picOnly.Vga.Should().BeNull();
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
        none.Vga.Should().BeNull();
        none.Dos.Should().BeNull();
        none.MouseDevice.Should().BeNull();
        none.MouseDriver.Should().BeNull();
        none.Dma.Should().BeNull();
        none.SoundBlaster.Should().BeNull();
        none.A20Gate.Should().BeNull();
        none.Xms.Should().BeNull();
        none.Ems.Should().BeNull();
    }

    [Fact]
    public void RestoreBringsBackTheA20LineAndTheXmsAndEmsAllocations() {
        // Arrange
        using Spice86Creator creator = new("externalint", enablePit: true, enableXms: true, enableEms: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        ExtendedMemoryManager xms = machine.Dos.Xms ?? throw new InvalidOperationException("XMS is off.");
        ExpandedMemoryManager ems = machine.Dos.Ems ?? throw new InvalidOperationException("EMS is off.");
        bool a20AtCapture = IsA20Enabled(machine);
        MachineDeviceState captured = machine.CaptureDeviceState();

        // Act
        ushort xmsHandle = AllocateXms(machine, xms);
        ushort emsHandle = ems.AllocatePageHandle(1, 0).dx;
        machine.IoPortDispatcher.WriteByte(KeyboardControllerCommandPort, a20AtCapture ? DisableA20 : EnableA20);
        bool a20AfterChange = IsA20Enabled(machine);
        machine.RestoreDeviceState(captured);

        // Assert
        a20AfterChange.Should().NotBe(a20AtCapture);
        IsA20Enabled(machine).Should().Be(a20AtCapture);
        AllocateXms(machine, xms).Should().Be(xmsHandle);
        ems.AllocatePageHandle(1, 0).dx.Should().Be(emsHandle);
    }

    [Fact]
    public void RestoreOfAnXmsStateIntoAMachineWithoutXmsThrows() {
        // Arrange
        using Spice86Creator withXms = new("externalint", enableXms: true);
        using Spice86DependencyInjection withXmsInjection = withXms.Create();
        MachineDeviceState captured = withXmsInjection.Machine.CaptureDeviceState(MachineDevices.Xms);
        using Spice86Creator withoutXms = new("externalint");
        using Spice86DependencyInjection withoutXmsInjection = withoutXms.Create();

        // Act
        Action restore = () => withoutXmsInjection.Machine.RestoreDeviceState(captured);

        // Assert
        captured.Xms.Should().NotBeNull();
        restore.Should().Throw<InvalidOperationException>();
    }

    private static bool IsA20Enabled(Machine machine) {
        return machine.SystemBiosInt15Handler.ToggleA20GateOrGetStatus(QueryA20Status).a20Enabled == 1;
    }

    private static ushort AllocateXms(Machine machine, ExtendedMemoryManager xms) {
        machine.CpuState.AH = AllocateExtendedMemoryBlock;
        machine.CpuState.DX = 1;
        xms.RunMultiplex();
        machine.CpuState.AX.Should().Be(1);
        return machine.CpuState.DX;
    }

    [Fact]
    public void RestoreOfTheDosMemberGivesBackTheDosState() {
        // Arrange
        using Spice86Creator creator = new("externalint", enablePit: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        MachineDeviceState dosOnly = machine.CaptureDeviceState(MachineDevices.Dos);
        ushort dtaSegment = machine.Dos.FileManager.DiskTransferAreaAddressSegment;
        ushort dtaOffset = machine.Dos.FileManager.DiskTransferAreaAddressOffset;

        // Act
        machine.Dos.FileManager.SetDiskTransferAreaAddress((ushort)(dtaSegment + 1), (ushort)(dtaOffset + 2));
        machine.RestoreDeviceState(dosOnly);

        // Assert
        dosOnly.Dos.Should().NotBeNull();
        dosOnly.Pic.Should().BeNull();
        machine.Dos.FileManager.DiskTransferAreaAddressSegment.Should().Be(dtaSegment);
        machine.Dos.FileManager.DiskTransferAreaAddressOffset.Should().Be(dtaOffset);
    }

    [Fact]
    public void RestoreGivesBackTheMousePositionAndRangesSetThroughTheDriver() {
        // Arrange
        using Spice86Creator creator = new("externalint", enablePit: true);
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        machine.MouseDriver.CurrentMaxX = 300;
        machine.MouseDriver.SetCursorPosition(150, 20);
        MachineDeviceState captured = machine.CaptureDeviceState(MachineDevices.Mouse);
        MouseStatusRecord statusAtCapture = machine.MouseDriver.CurrentMouseStatus;

        // Act
        machine.MouseDriver.Reset();
        machine.RestoreDeviceState(captured);

        // Assert
        captured.Pic.Should().BeNull();
        machine.MouseDriver.CurrentMouseStatus.Should().Be(statusAtCapture);
        machine.MouseDriver.CurrentMaxX.Should().Be(300);
    }

    private static void ChangeDevicesThroughPorts(Machine machine) {
        machine.IoPortDispatcher.WriteByte(PrimaryPicDataPort, 0xA5);
        machine.IoPortDispatcher.WriteByte(PitControlPort, Channel0RateGenerator);
        machine.IoPortDispatcher.WriteByte(PitChannel0Port, 0x34);
        machine.IoPortDispatcher.WriteByte(PitChannel0Port, 0x12);
        machine.IoPortDispatcher.WriteByte(DmaChannel1PagePort, 0x42);
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
