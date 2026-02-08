namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Interfaces;

using Xunit;

public class PicTests {
    private const int InterruptVectorBase = 0x8;
    private const byte HighestPrioIrq = 0;
    private const byte HighPrioIrq = 1;
    private const byte LowPrioIrq = 3;

    private const byte RotatePriorityCommand = 0b1000_0000;
    private const byte NonSpecificEoiCommand = 0b0010_0000;

    private const ushort PrimaryCommandPort = 0x20;
    private const ushort PrimaryDataPort = 0x21;
    private readonly IOPortDispatcher _ioPortDispatcher;

    private readonly DualPic _pic;

    public PicTests() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        State state = new(CpuModel.INTEL_80286);
        _ioPortDispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), state, logger, false, new NullCyclesLimiter());
        _pic = new DualPic(_ioPortDispatcher, state, logger, false);
    }

    [Fact]
    public void FreshPicHasNoPendingRequests() {
        bool result = HasPendingRequest();
        result.Should().BeFalse();
    }

    [Fact]
    public void BootstrappedPicQueuesIrqBeforeInitialization() {
        ActivateIrq(HighestPrioIrq);

        bool result = HasPendingRequest();

        result.Should().BeTrue();
    }

    [Fact]
    public void InitializedPicHasNoPendingRequests() {
        InitializePic();

        bool result = HasPendingRequest();

        result.Should().BeFalse();
    }

    [Fact]
    public void RequestingIrqFromInitializedPicMakesItHavePendingRequests() {
        InitializePic();
        ActivateIrq(HighestPrioIrq);

        bool result = HasPendingRequest();

        result.Should().BeTrue();
    }

    [Fact]
    public void PendingIrq0ReturnsInt8AndClearsPendingRequest() {
        InitializePic();
        ActivateIrq(HighestPrioIrq);

        byte? result = _pic.ComputeVectorNumber();

        result.Should().Be(ExpectedVectorNumberFromIrq(HighestPrioIrq));
        HasPendingRequest().Should().BeFalse();
    }

    [Fact]
    public void SecondLowerPriorityIrqDoesNotInterruptPendingIrq() {
        InitializePic();
        ActivateIrq(HighPrioIrq);
        ActivateIrq(LowPrioIrq);

        byte? result = _pic.ComputeVectorNumber();

        result.Should().Be(ExpectedVectorNumberFromIrq(HighPrioIrq));
        HasPendingRequest().Should().BeTrue();
    }

    [Fact]
    public void SecondHigherPriorityIrqPrecedesPendingIrq() {
        InitializePic();
        ActivateIrq(LowPrioIrq);
        ActivateIrq(HighPrioIrq);

        byte? result = _pic.ComputeVectorNumber();

        result.Should().Be(ExpectedVectorNumberFromIrq(HighPrioIrq));
        HasPendingRequest().Should().BeTrue();
    }

    [Fact]
    public void MaskedIrqIsIgnored() {
        InitializePic();
        WriteData(1 << HighPrioIrq);
        ActivateIrq(LowPrioIrq);
        ActivateIrq(HighPrioIrq);

        byte? result = _pic.ComputeVectorNumber();

        result.Should().Be(ExpectedVectorNumberFromIrq(LowPrioIrq));
        HasPendingRequest().Should()
            .BeFalse($"irq {LowPrioIrq} should be handled and irq {HighPrioIrq} should be ignored");
    }

    [Fact]
    public void DuplicateIrqIsIgnoredWhileStillInProgress() {
        InitializePic();
        ActivateIrq(HighPrioIrq);
        _pic.ComputeVectorNumber();

        ActivateIrq(HighPrioIrq);

        HasPendingRequest().Should()
            .BeFalse($"irq {HighPrioIrq} should be in service and a new irq {HighPrioIrq} should be ignored");
    }

    [Fact]
    public void SpecialMaskModeAllowsLowerPriorityInterrupts() {
        InitializePic();
        WriteCommand(0b01101000);
        ActivateIrq(HighPrioIrq);
        _pic.ComputeVectorNumber();

        ActivateIrq(LowPrioIrq);
        byte? result = _pic.ComputeVectorNumber();

        result.Should().Be(ExpectedVectorNumberFromIrq(LowPrioIrq));
        AssertInServiceRegister((1 << LowPrioIrq) | (1 << HighPrioIrq));
    }

    [Fact]
    public void InServiceRegisterContainsIrqInService() {
        InitializePic();
        ActivateIrq(HighPrioIrq);

        _pic.ComputeVectorNumber();

        AssertInServiceRegister(1 << HighPrioIrq);
    }

    [Fact]
    public void AcknowledgingAnInterruptShouldTakeItOutOfService() {
        InitializePic();
        ActivateIrq(HighPrioIrq);
        _pic.ComputeVectorNumber();

        WriteCommand(NonSpecificEoiCommand);

        HasPendingRequest().Should().BeFalse();
    }

    [Fact]
    public void InterruptRequestRegisterContainsRequestedIrq() {
        InitializePic();

        ActivateIrq(HighPrioIrq);

        AssertInterruptRequestRegister(1 << HighPrioIrq);
    }

    [Fact]
    public void InterruptRequestRegisterContainsAllRequestedIrqs() {
        InitializePic();

        for (byte irq = 0; irq < 8; irq++) {
            ActivateIrq(irq);
        }

        AssertInterruptRequestRegister(0b1111_1111);
    }

    [Fact]
    public void TakingAnInterruptIntoServiceRemovesTheRequest() {
        InitializePic();
        ActivateIrq(HighPrioIrq);

        _pic.ComputeVectorNumber();

        AssertInterruptRequestRegister(0);
    }

    [Fact]
    public void AHigherPriorityIrqIsServicedIfOnlyLowerIrqIsInService() {
        InitializePic();

        ActivateIrq(LowPrioIrq);
        byte? vector1 = _pic.ComputeVectorNumber();
        ActivateIrq(HighPrioIrq);
        byte? vector2 = _pic.ComputeVectorNumber();

        vector1.Should().Be(ExpectedVectorNumberFromIrq(LowPrioIrq));
        vector2.Should().Be(ExpectedVectorNumberFromIrq(HighPrioIrq));
        AssertInterruptRequestRegister(0);
        AssertInServiceRegister((1 << HighPrioIrq) | (1 << LowPrioIrq));
    }

    [Fact]
    public void ALowerPriorityIrqIsNotServicedWhenHigherPriorityIrqIsInService() {
        InitializePic();

        ActivateIrq(HighPrioIrq);
        byte? vector1 = _pic.ComputeVectorNumber();
        ActivateIrq(LowPrioIrq);
        byte? vector2 = _pic.ComputeVectorNumber();

        vector1.Should().Be(ExpectedVectorNumberFromIrq(HighPrioIrq));
        vector2.Should().BeNull();
        AssertInterruptRequestRegister(1 << LowPrioIrq);
        AssertInServiceRegister(1 << HighPrioIrq);
    }

    [Fact]
    public void EndOfInterruptCommandShouldClearInServiceRegister() {
        InitializePic();
        ActivateIrq(HighPrioIrq);
        _pic.ComputeVectorNumber();

        WriteCommand(NonSpecificEoiCommand);

        HasPendingRequest().Should().BeFalse();
    }

    [Fact]
    public void EndOfInterruptCommandShouldOnlyClearHighestPriorityInServiceRegister() {
        InitializePic();
        ActivateIrq(LowPrioIrq);
        _pic.ComputeVectorNumber();
        ActivateIrq(HighPrioIrq);
        _pic.ComputeVectorNumber();

        WriteCommand(NonSpecificEoiCommand);

        AssertInServiceRegister(1 << LowPrioIrq);
    }

    [Fact]
    public void AutomaticRotationChangesPriority() {
        InitializePic();
        WriteCommand(RotatePriorityCommand);
        ActivateIrq(0);
        ActivateIrq(1);

        byte? result = _pic.ComputeVectorNumber();

        result.Should().Be(ExpectedVectorNumberFromIrq(1));
    }

    [Fact]
    public void EoiBeforeAutomaticRotation() {
        InitializePic();
        ActivateIrq(1);
        _pic.ComputeVectorNumber();
        ActivateIrq(0);
        _pic.ComputeVectorNumber();

        WriteCommand(NonSpecificEoiCommand | RotatePriorityCommand);

        AssertInServiceRegister(1 << 1);
    }

    [Fact]
    public void AutomaticRotationAtEndOfInterruptLowersLastServiceDevicePriority() {
        InitializePic();
        ActivateIrq(6);
        _pic.ComputeVectorNumber();
        ActivateIrq(4);
        _pic.ComputeVectorNumber();

        WriteCommand(NonSpecificEoiCommand | RotatePriorityCommand | 0x5);
        ActivateIrq(4);
        byte? result = _pic.ComputeVectorNumber();

        AssertInterruptRequestRegister(1 << 4);
        AssertInServiceRegister(1 << 6);
        result.Should().BeNull("irq 6 should still be in service and have a higher priority than irq 4");
    }

    private void AssertInServiceRegister(byte expected) {
        WriteCommand(0b0000_1011);
        byte result = ReadCommand();
        result.Should().Be(expected);
    }

    private void AssertInterruptRequestRegister(byte expected) {
        WriteCommand(0b0000_1010);
        byte result = ReadCommand();
        result.Should().Be(expected);
    }

    private bool HasPendingRequest() {
        PicSnapshot snapshot = _pic.GetPicSnapshot(DualPic.PicController.Primary);
        byte pending = (byte)(snapshot.InterruptRequestRegister &
                              snapshot.InterruptMaskRegisterInverted &
                              snapshot.InServiceRegisterInverted);
        return pending != 0;
    }

    private static byte ExpectedVectorNumberFromIrq(byte irq) {
        return (byte)(InterruptVectorBase + irq);
    }

    private void InitializePic() {
        WriteCommand(0b0001_0001);
        WriteData(InterruptVectorBase);
        WriteData(0x00);
        WriteData(0b0000_0001);
    }

    private void ActivateIrq(byte irq) {
        _pic.ActivateIrq(irq);
    }

    private void WriteCommand(byte value) {
        _ioPortDispatcher.WriteByte(PrimaryCommandPort, value);
    }

    private void WriteData(byte value) {
        _ioPortDispatcher.WriteByte(PrimaryDataPort, value);
    }

    private byte ReadCommand() {
        return _ioPortDispatcher.ReadByte(PrimaryCommandPort);
    }
}