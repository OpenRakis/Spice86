namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU;
using Spice86.Tests.CpuTests.SingleStepTests;

using Xunit;

public class AluTests {
    
    [Theory]
    [InlineData(0b0011110000000000, 0b0010000000000001, 0, 0b0011110000000000, true, true)] // result is same as dest, flags unaffected
    [InlineData(0b0000000000000001, 0b0000000000000000, 1, 0b0000000000000010, false, false)] // shift one bit 
    [InlineData(0b0000000000000001, 0b1000000000000000, 1, 0b0000000000000011, false, false)] // shift in a 1 from the source
    [InlineData(0b0000000000000001, 0b1000000000000000, 2, 0b0000000000000110, false, false)] // shift more than 1 position
    [InlineData(0b0010000000000010, 0b0100000000000000, 3, 0b0000000000010010, true, false)] // last shifted bit is 1
    [InlineData(0b0000100000000000, 0b0000000000000000, 4, 0b1000000000000000, false, true)] // last shifted bit is 0 and sign changed
    [InlineData(0b1000000000000000, 0b0000000000000001, 5, 0b0000000000000000, false, true)] // last shifted bit is 0 and sign changed  
    [InlineData(0b1111110000000000, 0b1000000000000001, 6, 0b0000000000100000, true, true)] // last shifted bit is 1 and sign changed 
    [InlineData(0b0011110000000000, 0b0010000000000001, 16, 0b0010000000000001, false, false)] // complete shift
    [InlineData(0b0011110000000000, 0b0010000000000001, 17, 0b0100000000000010, true, true)] // count > size is undefined
    public void TestShld16(ushort destination, ushort source, byte count, ushort expected, bool cf, bool of) {
        // Arrange
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = true,
            OverflowFlag = true
        };
        var alu = new Alu16(state);

        // Act
        ushort result = alu.Shld(destination, source, count);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(cf, state.CarryFlag);
        Assert.Equal(of, state.OverflowFlag);
    }

    [Theory]
    [InlineData(0b00111100000000000000000000000000, 0b00100000000000000000000000000001, 0, 0b00111100000000000000000000000000, true, true)] // result is same as dest, flags unaffected
    [InlineData(0b00000000000000000000000000000001, 0b00000000000000000000000000000000, 1, 0b00000000000000000000000000000010, false, false)] // shift one bit 
    [InlineData(0b00000000000000000000000000000001, 0b10000000000000000000000000000000, 1, 0b00000000000000000000000000000011, false, false)] // shift in a 1 from the source
    [InlineData(0b00000000000000000000000000000001, 0b10000000000000000000000000000000, 2, 0b00000000000000000000000000000110, false, false)] // shift more than 1 position
    [InlineData(0b00100000000000100000000000000000, 0b01000000000000000000000000000000, 3, 0b00000000000100000000000000000010, true, false)] // last shifted bit is 1
    [InlineData(0b00001000000000000000000000000000, 0b00000000000000000000000000000000, 4, 0b10000000000000000000000000000000, false, true)] // last shifted bit is 0 and sign changed
    [InlineData(0b10000000000000000000000000000000, 0b00000000000000000000000000000001, 5, 0b00000000000000000000000000000000, false, true)] // last shifted bit is 0 and sign changed  
    [InlineData(0b11111100000000000000000000000000, 0b10000000000000010000000000000000, 6, 0b00000000000000000000000000100000, true, true)] // last shifted bit is 1 and sign changed 
    [InlineData(0b00110000000000000000110000000000, 0b00100000000000000000000000000001, 32, 0b00110000000000000000110000000000, true, true)] // only lowest 5 bits of count are used (so it's 0)
    public void TestShld32(uint destination, uint source, byte count, uint expected, bool cf, bool of) {
        // Arrange
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = true,
            OverflowFlag = true
        };
        var alu = new Alu32(state);

        // Act
        uint result = alu.Shld(destination, source, count);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(cf, state.CarryFlag);
        Assert.Equal(of, state.OverflowFlag);
    }

    [Theory]
    [InlineData(0b0011110000000000, 0b0010000000000001, 0, 0b0011110000000000, true,
        true)] // result is same as dest, flags unaffected
    [InlineData(0b0000000000000001, 0b0000000000000000, 1, 0b0000000000000000, true,
        false)] // shift one bit, last shifted bit (LSB of dest) is 1
    [InlineData(0b0000000000000001, 0b0000000000000001, 1, 0b1000000000000000, true,
        true)] // shift in a 1 from the source (LSB of source -> MSB of dest)
    [InlineData(0b0000000000000001, 0b0000000000000001, 2, 0b0100000000000000, false,
        false)] // shift more than 1 position
    [InlineData(0b0000000000000100, 0b0000000000000000, 3, 0b0000000000000000, true, false)] // last shifted bit is 1
    [InlineData(0b1000000000000000, 0b0000000000000000, 4, 0b0000100000000000, false,
        true)] // last shifted bit is 0 and sign changed (1 -> 0)
    [InlineData(0b1000000000000000, 0b0000000000000000, 5, 0b0000010000000000, false,
        true)] // last shifted bit is 0 and sign changed (1 -> 0)
    [InlineData(0b0000000000100000, 0b0000000000100000, 6, 0b1000000000000000, true,
        true)] // last shifted bit is 1 and sign changed (0 -> 1)
    [InlineData(0b0011110000000000, 0b0010000000000001, 16, 0b0010000000000001, false,
        false)] // complete shift: result == source
    [InlineData(0b0011110000000000, 0b0010000000000001, 17, 0b0001000000000000, true,
        false)] // count > size is undefined; lowest 5 bits used (17); verify CF and sign
    public void TestShrd16(ushort destination, ushort source, byte count, ushort expected, bool cf, bool of) {
        // Arrange
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = true,
            OverflowFlag = true
        };
        var alu = new Alu16(state);

        // Act
        ushort result = alu.Shrd(destination, source, count);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(cf, state.CarryFlag);
        Assert.Equal(of, state.OverflowFlag);
    }

    [Theory]
    [InlineData(0b00111100000000000000000000000000, 0b00100000000000000000000000000001, 0,
        0b00111100000000000000000000000000, true, true)] // result is same as dest, flags unaffected
    [InlineData(0b00000000000000000000000000000001, 0b00000000000000000000000000000000, 1,
        0b00000000000000000000000000000000, true, false)] // shift one bit, last shifted bit is 1
    [InlineData(0b00000000000000000000000000000001, 0b00000000000000000000000000000001, 1,
        0b10000000000000000000000000000000, true, true)] // shift in a 1 from the source (LSB of source -> MSB of dest)
    [InlineData(0b00000000000000000000000000000001, 0b00000000000000000000000000000001, 2,
        0b01000000000000000000000000000000, false, false)] // shift more than 1 position
    [InlineData(0b00000000000000000000000000000100, 0b00000000000000000000000000000000, 3,
        0b00000000000000000000000000000000, true, false)] // last shifted bit is 1
    [InlineData(0b10000000000000000000000000000000, 0b00000000000000000000000000000000, 4,
        0b00001000000000000000000000000000, false, true)] // last shifted bit is 0 and sign changed (1 -> 0)
    [InlineData(0b10000000000000000000000000000000, 0b00000000000000000000000000000000, 5,
        0b00000100000000000000000000000000, false, true)] // last shifted bit is 0 and sign changed (1 -> 0)
    [InlineData(0b00000000000000000000000000100000, 0b00000000000000000000000000100000, 6,
        0b10000000000000000000000000000000, true, true)] // last shifted bit is 1 and sign changed (0 -> 1)
    [InlineData(0b00110000000000000000110000000000, 0b00100000000000000000000000000001, 32,
        0b00110000000000000000110000000000, true, true)] // only lowest 5 bits of count are used (so it's 0)
    public void TestShrd32(uint destination, uint source, byte count, uint expected, bool cf, bool of) {
        // Arrange
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = true,
            OverflowFlag = true
        };
        var alu = new Alu32(state);

        // Act
        uint result = alu.Shrd(destination, source, count);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(cf, state.CarryFlag);
        Assert.Equal(of, state.OverflowFlag);
    }
}