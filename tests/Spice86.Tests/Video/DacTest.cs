namespace Spice86.Tests.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;

using Xunit;

public class DacTest {
    [Fact]
    public void TestReadingDacReturns6BitData() {
        // Arrange
        var dac = new DacRegisters();

        // Act
        for (byte i = 0; i < byte.MaxValue; i++) {
            dac.DataRegister = i;
        }
        dac.IndexRegisterReadMode = 0;

        // Assert
        for (byte i = 0; i < byte.MaxValue; i++) {
            byte expected = (byte)(i & 0b00111111);
            byte result = dac.DataRegister;
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void WhiteStaysWhite() {
        // Arrange
        var dac = new DacRegisters();

        // Act
        dac.DataRegister = 0b111111;
        dac.DataRegister = 0b111111;
        dac.DataRegister = 0b111111;

        // Assert
        Assert.Equal(0xFFFFFFFF, dac.ArgbPalette[0]);
    }
}