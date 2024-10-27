namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Shared.Interfaces;

using Xunit;

public class PicTests {
    private readonly Pic _pic;
    private const int InterruptVectorBase = 0x8;
    private const byte HighestPrioIrq = 0;
    private const byte HighPrioIrq = 1;
    private const byte LowPrioIrq = 3;

    private const byte RotatePriorityCommand = 0b1000_0000;
    private const byte NonSpecificEOICommand = 0b0010_0000;

    public PicTests() {
        var loggerMock = Substitute.For<ILoggerService>();
        _pic = new Pic(loggerMock);
    }

    [Fact]
    public void FreshPicHasNoPendingRequests() {
        // Act
        bool result = _pic.HasPendingRequest();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void UninitializedPicHasNoPendingRequests() {
        // Arrange
        _pic.InterruptRequest(HighestPrioIrq);

        // Act
        bool result = _pic.HasPendingRequest();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void InitializedPicHasNoPendingRequests() {
        // Arrange
        InitializePic();

        // Act
        bool result = _pic.HasPendingRequest();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void RequestingIrqFromInitializedPicMakesItHavePendingRequests() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(HighestPrioIrq);

        // Act
        bool result = _pic.HasPendingRequest();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void PendingIrq0ReturnsInt8AndClearsPendingRequest() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(HighestPrioIrq);

        // Act
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(ExpectedVectorNumberFromIrq(HighestPrioIrq));
        _pic.HasPendingRequest().Should().BeFalse();
    }

    [Fact]
    public void SecondLowerPriorityIrqDoesNotInterruptPendingIrq() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(HighPrioIrq);
        _pic.InterruptRequest(LowPrioIrq);

        // Act
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(ExpectedVectorNumberFromIrq(HighPrioIrq));
        _pic.HasPendingRequest().Should().BeTrue();
    }

    [Fact]
    public void SecondHigherPriorityIrqPrecedesPendingIrq() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(LowPrioIrq);
        _pic.InterruptRequest(HighPrioIrq);

        // Act
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(ExpectedVectorNumberFromIrq(HighPrioIrq));
        _pic.HasPendingRequest().Should().BeTrue();
    }

    [Fact]
    public void MaskedIrqIsIgnored() {
        // Arrange
        InitializePic();
        _pic.ProcessDataWrite(1 << HighPrioIrq); // mask Highest IRQ
        _pic.InterruptRequest(LowPrioIrq);
        _pic.InterruptRequest(HighPrioIrq); // try to override Lowest IRQ by Highest

        // Act
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(ExpectedVectorNumberFromIrq(LowPrioIrq));
        _pic.HasPendingRequest().Should().BeFalse($"irq {LowPrioIrq} should be handled and irq {HighPrioIrq} should be ignored");
    }

    [Fact]
    public void DuplicateIrqIsIgnoredWhileStillInProgress() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(HighPrioIrq);
        _pic.ComputeVectorNumber();

        // Act
        _pic.InterruptRequest(HighPrioIrq);

        // Assert
        _pic.HasPendingRequest().Should().BeFalse($"irq {HighPrioIrq} should be in service and a new irq {HighPrioIrq} should be ignored");
    }

    [Fact]
    public void SpecialMaskModeAllowsLowerPriorityInterrupts() {
        // Arrange
        InitializePic();
        _pic.ProcessCommandWrite(0b01101000); // enable special mask mode
        _pic.InterruptRequest(HighPrioIrq);
        _pic.ComputeVectorNumber(); // Take irq 1 into service

        // Act
        _pic.InterruptRequest(LowPrioIrq);
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        // Special mask mode is more permissive and allows lowest priority IRQs to be in service at the same time as high priority IRQs.
        // This lets user service routine of an IRQ to be interrupted at will by any IRQ the developer wished. 
        result.Should().Be(ExpectedVectorNumberFromIrq(LowPrioIrq));
        AssertInServiceRegister(1 << LowPrioIrq | 1 << HighPrioIrq);
    }

    [Fact]
    public void InServiceRegisterContainsIrqInService() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(HighPrioIrq);

        // Act
        _pic.ComputeVectorNumber();

        // Assert
        AssertInServiceRegister(1 << HighPrioIrq);
    }

    [Fact]
    public void AcknowledgingAnInterruptShouldTakeItOutOfService() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(HighPrioIrq);
        _pic.ComputeVectorNumber();

        // Act
        _pic.ProcessCommandWrite(NonSpecificEOICommand); // OCW2, non specific EOI

        // Assert
        _pic.HasPendingRequest().Should().BeFalse();
    }

    [Fact]
    public void InterruptRequestRegisterContainsRequestedIrq() {
        // Arrange
        InitializePic();

        // Act
        _pic.InterruptRequest(HighPrioIrq);

        // Assert
        AssertInterruptRequestRegister(1 << HighPrioIrq);
    }

    [Fact]
    public void InterruptRequestRegisterContainsAllRequestedIrqs() {
        // Arrange
        InitializePic();

        // Act
        _pic.InterruptRequest(0);
        _pic.InterruptRequest(1);
        _pic.InterruptRequest(2);
        _pic.InterruptRequest(3);
        _pic.InterruptRequest(4);
        _pic.InterruptRequest(5);
        _pic.InterruptRequest(6);
        _pic.InterruptRequest(7);

        // Assert
        AssertInterruptRequestRegister(0b11111111);
    }

    [Fact]
    public void TakingAnInterruptIntoServiceRemovesTheRequest() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(HighPrioIrq);

        // Act
        _pic.ComputeVectorNumber();

        // Assert
        AssertInterruptRequestRegister(0b00000000);
    }

    [Fact]
    public void AHigherPriorityIrqIsServicedIfOnlyLowerIrqIsInService() {
        // Arrange
        InitializePic();

        // Act
        _pic.InterruptRequest(LowPrioIrq);
        byte? vector1 = _pic.ComputeVectorNumber();
        _pic.InterruptRequest(HighPrioIrq);
        byte? vector2 = _pic.ComputeVectorNumber();

        // Assert
        vector1.Should().Be(ExpectedVectorNumberFromIrq(LowPrioIrq));
        vector2.Should().Be(ExpectedVectorNumberFromIrq(HighPrioIrq));
        AssertInterruptRequestRegister(0b00000000);
        AssertInServiceRegister(1 << HighPrioIrq | 1 << LowPrioIrq);
    }

    [Fact]
    public void ALowerPriorityIrqIsNotServicedWhenHigherPriorityIrqIsInService() {
        // Arrange
        InitializePic();

        // Act
        _pic.InterruptRequest(HighPrioIrq);
        byte? vector1 = _pic.ComputeVectorNumber();
        _pic.InterruptRequest(LowPrioIrq);
        byte? vector2 = _pic.ComputeVectorNumber();

        // Assert
        vector1.Should().Be(ExpectedVectorNumberFromIrq(HighPrioIrq));
        vector2.Should().BeNull();
        AssertInterruptRequestRegister(1 << LowPrioIrq);
        AssertInServiceRegister(1 << HighPrioIrq);
    }

    [Fact]
    public void EndOfInterruptCommandShouldClearInServiceRegister() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(HighPrioIrq);
        _pic.ComputeVectorNumber();

        // Act
        _pic.ProcessCommandWrite(NonSpecificEOICommand); // OCW2, non specific EOI

        // Assert
        _pic.HasPendingRequest().Should().BeFalse();
    }

    [Fact]
    public void EndOfInterruptCommandShouldOnlyClearHighestPriorityInServiceRegister() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(LowPrioIrq);
        _pic.ComputeVectorNumber();
        _pic.InterruptRequest(HighPrioIrq);
        _pic.ComputeVectorNumber();

        // Act
        _pic.ProcessCommandWrite(NonSpecificEOICommand); // OCW2, non specific EOI, ACK highest priority IRQ

        // Assert
        AssertInServiceRegister(1 << LowPrioIrq);
    }

    [Fact]
    public void AutomaticRotationChangesPriority() {
        // Arrange
        InitializePic();
        // Lowest priority is IRQ0
        //  - Before, priority was 0,1,2,3,4,5,6,7
        //  - Now priority is 1,2,3,4,5,6,7,0
        _pic.ProcessCommandWrite(RotatePriorityCommand);
        _pic.InterruptRequest(0);
        _pic.InterruptRequest(1);
        // Act
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(ExpectedVectorNumberFromIrq(1), "IRQ1 is the highest priority now and not IRQ0");
    }

    [Fact]
    public void EoiBeforeAutomaticRotation() {
        // Arrange
        InitializePic();
        // Low prio Interrupt
        _pic.InterruptRequest(1);
        _pic.ComputeVectorNumber();
        // High prio Interrupt
        _pic.InterruptRequest(0);
        _pic.ComputeVectorNumber();
        // At this point both 0 and 1 are in service

        // Act
        // rotate on non-specific EOI:
        //  - EOI highest priority interrupt (so IRQ0)
        //  - So now Highest in service is 1 instead of 0
        _pic.ProcessCommandWrite(NonSpecificEOICommand|RotatePriorityCommand|0);

        // Assert
        // IRQ1 should still be in service but not IRQ0
        AssertInServiceRegister(1 << 1);
    }

    [Fact]
    public void AutomaticRotationAtEndOfInterruptLowersLastServiceDevicePriority() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(6);
        _pic.ComputeVectorNumber();
        _pic.InterruptRequest(4);
        _pic.ComputeVectorNumber();

        // Act
        // rotate on non-specific EOI, new lowest priority is 5, so highest is 6.
        _pic.ProcessCommandWrite(NonSpecificEOICommand | RotatePriorityCommand | 0x5);
        // irq 4 should now have lower priority than irq 6
        _pic.InterruptRequest(4);
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        // Request registered
        AssertInterruptRequestRegister(1 << 4);
        // IRQ6 is in service but not IRQ4
        AssertInServiceRegister(1 << 6);
        result.Should().BeNull("irq 6 should still be in service and have a higher priority than irq 4");
    }

    private void AssertInServiceRegister(byte expected) {
        _pic.ProcessCommandWrite(0b00001011);
        byte result = _pic.CommandRead();
        result.Should().Be(expected);
    }

    private void AssertInterruptRequestRegister(byte expected) {
        _pic.ProcessCommandWrite(0b00001010);
        byte result = _pic.CommandRead();
        result.Should().Be(expected);
    }

    private byte ExpectedVectorNumberFromIrq(byte irq) {
        return (byte)(InterruptVectorBase + irq);
    }

    private void InitializePic() {
        // ICW1
        _pic.ProcessCommandWrite(0b00010001); // ICW4 needed
        // ICW2
        _pic.ProcessDataWrite(InterruptVectorBase); // Interrupt vectors 0x08-0x0F
        // ICW3
        _pic.ProcessDataWrite(0x00); // No slaves
        // ICW4
        _pic.ProcessDataWrite(0b00000001); // 8086 mode
    }
}