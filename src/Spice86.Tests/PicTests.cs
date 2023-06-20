namespace Spice86.Tests;

using FluentAssertions;

using Moq;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Shared.Interfaces;

using Xunit;

public class PicTests {
    private readonly Pic _pic;

    public PicTests() {
        var loggerMock = new Mock<ILoggerService>();
        loggerMock.Setup(logger => logger.IsEnabled(It.IsAny<LogEventLevel>()))
            .Returns(false);
        loggerMock.Setup(logger => logger.WithLogLevel(It.IsAny<LogEventLevel>()))
            .Returns(loggerMock.Object);

        _pic = new Pic(loggerMock.Object);
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
        _pic.InterruptRequest(0);

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
        _pic.InterruptRequest(0);

        // Act
        bool result = _pic.HasPendingRequest();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void PendingIrq0ReturnsInt8AndClearsPendingRequest() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(0);

        // Act
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(0x08);
        _pic.HasPendingRequest().Should().BeFalse();
    }

    [Fact]
    public void SecondLowerPriorityIrqDoesNotInterruptPendingIrq() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(0);
        _pic.InterruptRequest(1);

        // Act
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(0x08);
        _pic.HasPendingRequest().Should().BeTrue();
    }

    [Fact]
    public void SecondHigherPriorityIrqPrecedesPendingIrq() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(3);
        _pic.InterruptRequest(1);

        // Act
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(0x09);
        _pic.HasPendingRequest().Should().BeTrue();
    }

    [Fact]
    public void MaskedIrqIsIgnored() {
        // Arrange
        InitializePic();
        _pic.ProcessDataWrite(0b00000010); // mask irq 1
        _pic.InterruptRequest(3);
        _pic.InterruptRequest(1); // try to override irq 3

        // Act
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(0x0B);
        _pic.HasPendingRequest().Should().BeFalse("irq 3 should be handled and irq 1 should be ignored");
    }

    [Fact]
    public void DuplicateIrqIsIgnoredWhileStillInProgress() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(1);
        _pic.ComputeVectorNumber();

        // Act
        _pic.InterruptRequest(1);

        // Assert
        _pic.HasPendingRequest().Should().BeFalse("irq 1 should be in service and a new irq 1 should be ignored");
    }

    [Fact]
    public void SpecialMaskModeAllowsLowerPriorityInterrupts() {
        // Arrange
        InitializePic();
        _pic.ProcessCommandWrite(0b01101000); // enable special mask mode
        _pic.ProcessDataWrite(0b00001000); // mask irq 3
        _pic.InterruptRequest(1);
        _pic.ComputeVectorNumber(); // Take irq 1 into service

        // Act
        _pic.InterruptRequest(4);
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        result.Should().Be(0x0C);
        AssertInServiceRegister(0b00010010);
    }

    [Fact]
    public void InServiceRegisterContainsIrqInService() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(1);

        // Act
        _pic.ComputeVectorNumber();

        // Assert
        AssertInServiceRegister(0b00000010);
    }

    [Fact]
    public void AcknowledgingAnInterruptShouldTakeItOutOfService() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(1);
        _pic.ComputeVectorNumber();

        // Act
        _pic.ProcessCommandWrite(0b00100000); // OCW2, non specific EOI

        // Assert
        _pic.HasPendingRequest().Should().BeFalse();
    }

    [Fact]
    public void InterruptRequestRegisterContainsRequestedIrq() {
        // Arrange
        InitializePic();

        // Act
        _pic.InterruptRequest(1);

        // Assert
        AssertInterruptRequestRegister(0b00000010);
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
        _pic.InterruptRequest(1);

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
        _pic.InterruptRequest(5);
        byte? vector1 = _pic.ComputeVectorNumber();
        _pic.InterruptRequest(3);
        byte? vector2 = _pic.ComputeVectorNumber();

        // Assert
        vector1.Should().Be(0x0D);
        vector2.Should().Be(0x0B);
        AssertInterruptRequestRegister(0b00000000);
        AssertInServiceRegister(0b00101000);
    }

    [Fact]
    public void ALowerPriorityIrqIsNotServicedWhenHigherPriorityIrqIsInService() {
        // Arrange
        InitializePic();

        // Act
        _pic.InterruptRequest(3);
        byte? vector1 = _pic.ComputeVectorNumber();
        _pic.InterruptRequest(5);
        byte? vector2 = _pic.ComputeVectorNumber();

        // Assert
        vector1.Should().Be(0x0B);
        vector2.Should().BeNull();
        AssertInterruptRequestRegister(0b00100000);
        AssertInServiceRegister(0b00001000);
    }

    [Fact]
    public void EndOfInterruptCommandShouldClearInServiceRegister() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(1);
        _pic.ComputeVectorNumber();

        // Act
        _pic.ProcessCommandWrite(0b00100000); // OCW2, non specific EOI

        // Assert
        _pic.HasPendingRequest().Should().BeFalse();
    }

    [Fact]
    public void EndOfInterruptCommandShouldOnlyClearHighestPriorityInServiceRegister() {
        // Arrange
        InitializePic();
        _pic.InterruptRequest(3);
        _pic.ComputeVectorNumber();
        _pic.InterruptRequest(1);
        _pic.ComputeVectorNumber();

        // Act
        _pic.ProcessCommandWrite(0b00100000); // OCW2, non specific EOI

        // Assert
        AssertInServiceRegister(0b00001000);
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
        _pic.ProcessCommandWrite(0b10100000); // rotate on non-specific EOI
        // irq 4 should now have lower priority than irq 6
        _pic.InterruptRequest(4);
        byte? result = _pic.ComputeVectorNumber();

        // Assert
        AssertInterruptRequestRegister(0b00010000);
        AssertInServiceRegister(0b01000000);
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

    private void InitializePic() {
        // ICW1
        _pic.ProcessCommandWrite(0b00010001); // ICW4 needed
        // ICW2
        _pic.ProcessDataWrite(0x08); // Interrupt vectors 0x08-0x0F
        // ICW3
        _pic.ProcessDataWrite(0x00); // No slaves
        // ICW4
        _pic.ProcessDataWrite(0b00000001); // 8086 mode
    }
}