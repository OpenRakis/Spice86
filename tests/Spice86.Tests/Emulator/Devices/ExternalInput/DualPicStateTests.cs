namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;

using Xunit;

/// <summary>
/// Checks <see cref="DualPic.CaptureState" /> and <see cref="DualPic.RestoreState" />: capture, change the
/// controllers through their ports, restore, and compare state and behaviour with the capture.
/// </summary>
public sealed class DualPicStateTests {
    private const ushort PrimaryCommandPort = 0x20;
    private const ushort PrimaryDataPort = 0x21;
    private const ushort SecondaryCommandPort = 0xA0;
    private const ushort SecondaryDataPort = 0xA1;

    private const byte Icw1WithIcw4 = 0b0001_0001;
    private const byte Icw4Intel8086Mode = 0b0000_0001;
    private const byte ReadInServiceRegister = 0b0000_1011;
    private const byte ReadRequestRegister = 0b0000_1010;
    private const byte NonSpecificEoi = 0b0010_0000;
    private const byte RotateOnNonSpecificEoi = 0b1010_0000;

    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly DualPic _pic;

    public DualPicStateTests() {
        ILogger logger = Substitute.For<ILogger>();
        State state = new(CpuModel.INTEL_80286);
        _ioPortDispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), state, logger, false);
        _pic = new DualPic(_ioPortDispatcher, state, logger, false);
    }

    [Fact]
    public void CaptureChangesNothing() {
        // Arrange
        ProgramBothControllers();
        _pic.ActivateIrq(1);
        PicSnapshot primaryBefore = _pic.GetPicSnapshot(DualPic.PicController.Primary);
        PicSnapshot secondaryBefore = _pic.GetPicSnapshot(DualPic.PicController.Secondary);

        // Act
        DualPicState first = _pic.CaptureState();
        DualPicState second = _pic.CaptureState();

        // Assert
        second.Should().Be(first);
        _pic.GetPicSnapshot(DualPic.PicController.Primary).Should().Be(primaryBefore);
        _pic.GetPicSnapshot(DualPic.PicController.Secondary).Should().Be(secondaryBefore);
        _pic.IrqCheck.Should().BeTrue();
    }

    [Fact]
    public void CapturedStateHoldsTheProgrammedRegisters() {
        // Arrange
        ProgramBothControllers();
        _ioPortDispatcher.WriteByte(PrimaryDataPort, 0b1111_1000);
        _pic.ActivateIrq(1);

        // Act
        DualPicState state = _pic.CaptureState();

        // Assert
        state.Primary.InterruptVectorBase.Should().Be(0x50);
        state.Secondary.InterruptVectorBase.Should().Be(0x58);
        state.Primary.InterruptMaskRegister.Should().Be(0b1111_1000);
        state.Primary.InterruptRequestRegister.Should().Be(0b0000_0010);
        state.Primary.ActiveIrqLine.Should().Be(8);
        state.Primary.CurrentIcwIndex.Should().Be(0);
        state.IrqCheck.Should().BeTrue();
    }

    [Fact]
    public void RestoreAfterChangesThroughPortsGivesTheCapturedState() {
        // Arrange: reprogrammed vectors, a mask, one line in service, one latched, a rotated priority and the
        // in-service register selected for command port reads
        ProgramBothControllers();
        _ioPortDispatcher.WriteByte(PrimaryDataPort, 0b1010_0000);
        _pic.ActivateIrq(3);
        _pic.ComputeVectorNumber();
        _ioPortDispatcher.WriteByte(PrimaryCommandPort, RotateOnNonSpecificEoi);
        _pic.ActivateIrq(4);
        _pic.ComputeVectorNumber();
        _pic.ActivateIrq(1);
        _pic.SetIrqMask(10, false);
        _pic.ActivateIrq(10);
        _ioPortDispatcher.WriteByte(PrimaryCommandPort, ReadInServiceRegister);
        DualPicState captured = _pic.CaptureState();
        PicSnapshot primarySnapshot = _pic.GetPicSnapshot(DualPic.PicController.Primary);
        PicSnapshot secondarySnapshot = _pic.GetPicSnapshot(DualPic.PicController.Secondary);

        // Act: change every part of the state through the ports and the lines, then restore
        _pic.Reset();
        _ioPortDispatcher.WriteByte(PrimaryCommandPort, Icw1WithIcw4);
        _ioPortDispatcher.WriteByte(PrimaryDataPort, 0x08);
        _pic.ActivateIrq(6);
        _pic.RestoreState(captured);

        // Assert
        _pic.CaptureState().Should().Be(captured);
        _pic.GetPicSnapshot(DualPic.PicController.Primary).Should().Be(primarySnapshot);
        _pic.GetPicSnapshot(DualPic.PicController.Secondary).Should().Be(secondarySnapshot);
        _pic.IrqCheck.Should().Be(captured.IrqCheck);
        _ioPortDispatcher.ReadByte(PrimaryCommandPort).Should().Be(captured.Primary.InServiceRegister);
    }

    [Fact]
    public void RestoredRequestIsDeliveredAgain() {
        // Arrange: a latched timer request, captured before the CPU takes it
        _pic.ActivateIrq(0);
        DualPicState captured = _pic.CaptureState();
        byte? firstVector = _pic.ComputeVectorNumber();
        _ioPortDispatcher.WriteByte(PrimaryCommandPort, NonSpecificEoi);
        _pic.DeactivateIrq(0);
        _pic.ComputeVectorNumber().Should().BeNull();

        // Act
        _pic.RestoreState(captured);
        byte? secondVector = _pic.ComputeVectorNumber();

        // Assert
        firstVector.Should().Be(0x08);
        secondVector.Should().Be(0x08);
    }

    [Fact]
    public void RestoredInitializationSequenceContinuesWhereItWasCaptured() {
        // Arrange: captured after ICW1, before ICW2
        _ioPortDispatcher.WriteByte(PrimaryCommandPort, Icw1WithIcw4);
        DualPicState captured = _pic.CaptureState();
        _ioPortDispatcher.WriteByte(PrimaryDataPort, 0x20);
        _ioPortDispatcher.WriteByte(PrimaryDataPort, 0x04);
        _ioPortDispatcher.WriteByte(PrimaryDataPort, Icw4Intel8086Mode);

        // Act: after the restore the next data port write is ICW2 again
        _pic.RestoreState(captured);
        _ioPortDispatcher.WriteByte(PrimaryDataPort, 0x60);

        // Assert
        captured.Primary.CurrentIcwIndex.Should().Be(1);
        _pic.GetPicSnapshot(DualPic.PicController.Primary).InterruptVectorBase.Should().Be(0x60);
    }

    [Fact]
    public void RestoredCommandPortReadSelectionIsKept() {
        // Arrange: a line in service, and the in-service register selected for command port reads
        _pic.ActivateIrq(1);
        _pic.ComputeVectorNumber();
        _ioPortDispatcher.WriteByte(PrimaryCommandPort, ReadInServiceRegister);
        DualPicState captured = _pic.CaptureState();
        _ioPortDispatcher.WriteByte(PrimaryCommandPort, ReadRequestRegister);

        // Act
        _pic.RestoreState(captured);

        // Assert
        _ioPortDispatcher.ReadByte(PrimaryCommandPort).Should().Be(0b0000_0010);
    }

    [Fact]
    public void RestoreRaisesIrqMaskChangedForChangedLinesOnly() {
        // Arrange
        DualPicState captured = _pic.CaptureState();
        _pic.SetIrqMask(0, true);
        _pic.SetIrqMask(12, false);
        List<(byte Irq, bool Masked)> changes = new();
        _pic.IrqMaskChanged += (irq, masked) => changes.Add((irq, masked));

        // Act
        _pic.RestoreState(captured);

        // Assert
        changes.Should().Equal((0, false), (12, true));
    }

    /// <summary>
    /// Runs the four-word initialization sequence on both controllers, with vector bases 0x50 and 0x58.
    /// </summary>
    private void ProgramBothControllers() {
        _ioPortDispatcher.WriteByte(PrimaryCommandPort, Icw1WithIcw4);
        _ioPortDispatcher.WriteByte(PrimaryDataPort, 0x50);
        _ioPortDispatcher.WriteByte(PrimaryDataPort, 0x04);
        _ioPortDispatcher.WriteByte(PrimaryDataPort, Icw4Intel8086Mode);
        _ioPortDispatcher.WriteByte(SecondaryCommandPort, Icw1WithIcw4);
        _ioPortDispatcher.WriteByte(SecondaryDataPort, 0x58);
        _ioPortDispatcher.WriteByte(SecondaryDataPort, 0x02);
        _ioPortDispatcher.WriteByte(SecondaryDataPort, Icw4Intel8086Mode);
    }
}
