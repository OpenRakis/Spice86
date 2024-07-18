namespace Spice86.Tests.Video;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Video.Registers;

using Xunit;

public class DacTest {
    [Fact]
    public void TestReadingDacReturns6BitData() {
        // Arrange
        var dac = new DacRegisters(new());

        // Act
        for (byte i = 0; i < byte.MaxValue; i++) {
            dac.DataRegister = i;
        }
        dac.IndexRegisterReadMode = 0;

        // Assert
        for (byte i = 0; i < byte.MaxValue; i++) {
            byte expected = (byte)(i & 0b00111111);
            byte result = dac.DataRegister;
            result.Should().Be(expected, $"The same 6 low bits of 0x{i:X2} that were written should be read back");
        }
    }

    [Fact]
    public void WhiteStaysWhite() {
        // Arrange
        var dac = new DacRegisters(new());

        // Act
        dac.DataRegister = 0b111111;
        dac.DataRegister = 0b111111;
        dac.DataRegister = 0b111111;

        // Assert
        dac.ArgbPalette[0].Should().Be(0xFFFFFFFF);
    }
}