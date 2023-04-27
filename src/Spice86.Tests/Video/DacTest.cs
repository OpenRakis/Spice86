namespace Spice86.Tests.Video;

using FluentAssertions;

using Spice86.Aeon.Emulator.Video;

using System.Drawing;

using Xunit;

public class DacTest {
    [Fact]
    public void TestReadingDacReturns6BitData() {
        // Arrange
        var dac = new DacRegisters();
        
        // Act
        for (byte i = 0; i < byte.MaxValue; i++) {
            dac.Write(i);
        }
        
        // Assert
        for (byte i = 0; i < byte.MaxValue; i++) {
            byte expected = (byte)(i & 0b00111111);
            byte result = dac.Read();
            result.Should().Be(expected, $"The same 6 low bits of 0x{i:X2} that were written should be read back");
        }
    }

    [Fact]
    public void WhiteStaysWhite() {
        // Arrange
        var dac = new DacRegisters();
        
        // Act
        dac.Write(0b111111);
        dac.Write(0b111111);
        dac.Write(0b111111);
        
        // Assert
        dac.Palette[0].Should().Be(0xFFFFFF);
    }
}