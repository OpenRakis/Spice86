namespace Spice86.Tests.Devices;

using FluentAssertions;
using Spice86.Core.Emulator.Devices.Cmos;
using Xunit;

/// <summary>
/// Unit tests for the CmosRegisters class.
/// </summary>
public class CmosRegistersTests {
    [Fact]
    public void RegisterIndexer_WithValidIndex_ReturnsCorrectValue() {
        // Arrange
        var registers = new CmosRegisters();
        const byte testValue = 0x42;
        const int index = 10;

        // Act
        registers[index] = testValue;
        byte result = registers[index];

        // Assert
        result.Should().Be(testValue);
    }

    [Fact]
    public void RegisterIndexer_WithNegativeIndex_ThrowsArgumentOutOfRangeException() {
        // Arrange
        var registers = new CmosRegisters();

        // Act
        Action act = () => _ = registers[-1];

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*CMOS register index must be between 0 and 63*");
    }

    [Fact]
    public void RegisterIndexer_WithIndexGreaterThan63_ThrowsArgumentOutOfRangeException() {
        // Arrange
        var registers = new CmosRegisters();

        // Act
        Action act = () => _ = registers[64];

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*CMOS register index must be between 0 and 63*");
    }

    [Fact]
    public void NmiEnabled_DefaultsToFalse() {
        // Arrange & Act
        var registers = new CmosRegisters();

        // Assert
        registers.NmiEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsBcdMode_DefaultsToFalse() {
        // Arrange & Act
        var registers = new CmosRegisters();

        // Assert
        registers.IsBcdMode.Should().BeFalse();
    }

    [Fact]
    public void CurrentRegister_CanBeSetAndRead() {
        // Arrange
        var registers = new CmosRegisters();
        const byte testRegister = 0x0A;

        // Act
        registers.CurrentRegister = testRegister;

        // Assert
        registers.CurrentRegister.Should().Be(testRegister);
    }

    [Fact]
    public void TimerState_IsInitializedAndAccessible() {
        // Arrange
        var registers = new CmosRegisters();

        // Act & Assert
        registers.Timer.Should().NotBeNull();
        registers.Timer.Enabled.Should().BeFalse();
        registers.Timer.Divider.Should().Be(0);
        registers.Timer.Delay.Should().Be(0);
        registers.Timer.Acknowledged.Should().BeFalse();
    }

    [Fact]
    public void LastEventState_IsInitializedAndAccessible() {
        // Arrange
        var registers = new CmosRegisters();

        // Act & Assert
        registers.Last.Should().NotBeNull();
        registers.Last.Timer.Should().Be(0);
        registers.Last.Ended.Should().Be(0);
        registers.Last.Alarm.Should().Be(0);
    }

    [Fact]
    public void TimerState_EnabledFlag_CanBeModified() {
        // Arrange
        var registers = new CmosRegisters();

        // Act
        registers.Timer.Enabled = true;

        // Assert
        registers.Timer.Enabled.Should().BeTrue();
    }

    [Fact]
    public void TimerState_DividerValue_CanBeModified() {
        // Arrange
        var registers = new CmosRegisters();
        const byte testDivider = 6;

        // Act
        registers.Timer.Divider = testDivider;

        // Assert
        registers.Timer.Divider.Should().Be(testDivider);
    }

    [Fact]
    public void LastEventState_TimestampsCanBeModified() {
        // Arrange
        var registers = new CmosRegisters();
        const double testTime = 1234.5;

        // Act
        registers.Last.Timer = testTime;
        registers.Last.Ended = testTime;
        registers.Last.Alarm = testTime;

        // Assert
        registers.Last.Timer.Should().Be(testTime);
        registers.Last.Ended.Should().Be(testTime);
        registers.Last.Alarm.Should().Be(testTime);
    }

    [Fact]
    public void RegisterCount_IsCorrect() {
        // Assert
        CmosRegisters.RegisterCount.Should().Be(64);
    }
}
