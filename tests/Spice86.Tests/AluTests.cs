namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU;

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

    [Fact]
    public void TestRcl16MatchesReference() {
        ushort[] values = [0x0000, 0x0001, 0x1234, 0x7FFF, 0x8000, 0xABCD, 0xFFFF];
        byte[] counts = [0, 1, 2, 3, 4, 8, 15, 16, 17, 18, 31];
        foreach (ushort value in values) {
            foreach (bool initialCf in new[] { false, true }) {
                foreach (bool initialOf in new[] { false, true }) {
                    foreach (byte count in counts) {
                        var state = new State(CpuModel.INTEL_80286) {
                            CarryFlag = initialCf,
                            OverflowFlag = initialOf
                        };
                        var alu = new Alu16(state);
                        ushort result = alu.Rcl(value, count);
                        (ushort expectedResult, bool expectedCf, bool expectedOf) =
                            ReferenceRcl16(value, initialCf, initialOf, count);
                        Assert.Equal(expectedResult, result);
                        Assert.Equal(expectedCf, state.CarryFlag);
                        Assert.Equal(expectedOf, state.OverflowFlag);
                    }
                }
            }
        }
    }

    [Fact]
    public void TestRcl32MatchesReference() {
        uint[] values = [0u, 1u, 0x12345678u, 0x7FFFFFFFu, 0x80000000u, 0xFEDCBA98u, 0xFFFFFFFFu];
        byte[] counts = [0, 1, 2, 3, 4, 8, 15, 16, 17, 18, 31];
        foreach (uint value in values) {
            foreach (bool initialCf in new[] { false, true }) {
                foreach (bool initialOf in new[] { false, true }) {
                    foreach (byte count in counts) {
                        var state = new State(CpuModel.INTEL_80286) {
                            CarryFlag = initialCf,
                            OverflowFlag = initialOf
                        };
                        var alu = new Alu32(state);
                        uint result = alu.Rcl(value, count);
                        (uint expectedResult, bool expectedCf, bool expectedOf) =
                            ReferenceRcl32(value, initialCf, initialOf, count);
                        Assert.Equal(expectedResult, result);
                        Assert.Equal(expectedCf, state.CarryFlag);
                        Assert.Equal(expectedOf, state.OverflowFlag);
                    }
                }
            }
        }
    }

    private static (ushort Result, bool Carry, bool Overflow) ReferenceRcl16(ushort value, bool initialCarry,
        bool initialOverflow, byte count) {
        int masked = count & 0x1F;
        int effective = masked % 17;
        if (effective == 0) {
            return (value, initialCarry, initialOverflow);
        }

        ushort result = value;
        bool carry = initialCarry;
        for (int i = 0; i < effective; i++) {
            bool newCarry = (result & 0x8000) != 0;
            result = (ushort)((result << 1) & 0xFFFF);
            if (carry) {
                result |= 0x0001;
            }

            carry = newCarry;
        }

        bool overflow = carry ^ ((result & 0x8000) != 0);
        return (result, carry, overflow);
    }

    private static (uint Result, bool Carry, bool Overflow) ReferenceRcl32(uint value, bool initialCarry,
        bool initialOverflow, byte count) {
        int masked = count & 0x1F;
        int effective = masked % 33;
        if (effective == 0) {
            return (value, initialCarry, initialOverflow);
        }

        uint result = value;
        bool carry = initialCarry;
        for (int i = 0; i < effective; i++) {
            bool newCarry = (result & 0x80000000) != 0;
            result <<= 1;
            if (carry) {
                result |= 0x00000001;
            }

            carry = newCarry;
        }

        bool overflow = carry ^ ((result & 0x80000000) != 0);
        return (result, carry, overflow);
    }

    [Theory]
    [InlineData((byte)0x10, (byte)0x10, (ushort)0x0100, true, true, true)]
    [InlineData((byte)0x02, (byte)0x03, (ushort)0x0006, false, false, false)]
    public void TestMul8SetsExpectedFlags(byte value1, byte value2, ushort expectedProduct, bool expectedCf, bool expectedOf, bool expectedZf) {
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = !expectedCf,
            OverflowFlag = !expectedOf,
            ZeroFlag = !expectedZf
        };
        var alu = new Alu8(state);

        ushort result = alu.Mul(value1, value2);

        Assert.Equal(expectedProduct, result);
        Assert.Equal(expectedCf, state.CarryFlag);
        Assert.Equal(expectedOf, state.OverflowFlag);
        Assert.Equal(expectedZf, state.ZeroFlag);
    }

    [Theory]
    [InlineData((ushort)0x8000, (ushort)0x0002, 0x00010000u, true, true, true)]
    [InlineData((ushort)0x1234, (ushort)0x0002, 0x00002468u, false, false, false)]
    public void TestMul16SetsExpectedFlags(ushort value1, ushort value2, uint expectedProduct, bool expectedCf, bool expectedOf, bool expectedZf) {
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = !expectedCf,
            OverflowFlag = !expectedOf,
            ZeroFlag = !expectedZf
        };
        var alu = new Alu16(state);

        uint result = alu.Mul(value1, value2);

        Assert.Equal(expectedProduct, result);
        Assert.Equal(expectedCf, state.CarryFlag);
        Assert.Equal(expectedOf, state.OverflowFlag);
        Assert.Equal(expectedZf, state.ZeroFlag);
    }

    [Theory]
    [InlineData(0x80000000u, 0x00000002u, 0x0000000100000000ul, true, true, true)]
    [InlineData(0x00000003u, 0x00000004u, 0x000000000000000cul, false, false, false)]
    public void TestMul32SetsExpectedFlags(uint value1, uint value2, ulong expectedProduct, bool expectedCf, bool expectedOf, bool expectedZf) {
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = !expectedCf,
            OverflowFlag = !expectedOf,
            ZeroFlag = !expectedZf
        };
        var alu = new Alu32(state);

        ulong result = alu.Mul(value1, value2);

        Assert.Equal(expectedProduct, result);
        Assert.Equal(expectedCf, state.CarryFlag);
        Assert.Equal(expectedOf, state.OverflowFlag);
        Assert.Equal(expectedZf, state.ZeroFlag);
    }

    [Fact]
    public void TestRcl8UsesNewCarryForOverflowComputation() {
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = true,
            OverflowFlag = true
        };
        var alu = new Alu8(state);

        byte result = alu.Rcl(0x00, 1);

        Assert.Equal(0x01, result);
        Assert.False(state.CarryFlag);
        bool expectedOf = state.CarryFlag ^ ((result & 0x80) != 0);
        Assert.Equal(expectedOf, state.OverflowFlag);
    }

    [Fact]
    public void TestRcr8RotatesThroughCarry() {
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = true
        };
        var alu = new Alu8(state);

        byte result = alu.Rcr(0x02, 1);

        Assert.Equal(0x81, result);
        Assert.False(state.CarryFlag);
    }

    [Fact]
    public void TestRol8SetsCarryAndOverflowFromResultBounds() {
        var state = new State(CpuModel.INTEL_80286) {
            CarryFlag = false
        };
        var alu = new Alu8(state);

        byte result = alu.Rol(0x81, 1);

        Assert.Equal(0x03, result);
        Assert.True(state.CarryFlag);
        bool expectedOf = ((result & 0x80) != 0) ^ ((result & 0x01) != 0);
        Assert.Equal(expectedOf, state.OverflowFlag);
    }

    [Fact]
    public void TestRor8SetsCarryFromShiftedBit() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu8(state);

        byte result = alu.Ror(0x01, 1);

        Assert.Equal(0x80, result);
        Assert.True(state.CarryFlag);
    }

    [Fact]
    public void TestShl8UpdatesCarryAndOverflow() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu8(state);

        byte result = alu.Shl(0x40, 1);

        Assert.Equal(0x80, result);
        Assert.False(state.CarryFlag);
        Assert.True(state.OverflowFlag);
    }

    [Fact]
    public void TestShr8SetsOverflowOnlyWhenCountIsOneAndMsbWasSet() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu8(state);

        byte result = alu.Shr(0x81, 1);

        Assert.Equal(0x40, result);
        Assert.True(state.CarryFlag);
        Assert.True(state.OverflowFlag);

        state = new State(CpuModel.INTEL_80286);
        alu = new Alu8(state);

        result = alu.Shr(0x81, 2);

        Assert.Equal(0x20, result);
        Assert.False(state.CarryFlag);
        Assert.False(state.OverflowFlag);
    }

    [Fact]
    public void TestSar8PerformsArithmeticShiftAndClearsOverflow() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu8(state);

        byte result = alu.Sar(0xFE, 1);

        Assert.Equal(0xFF, result);
        Assert.False(state.CarryFlag);
        Assert.False(state.OverflowFlag);
    }

    [Fact]
    public void TestShl16UpdatesFlagsUsingOriginalMsb() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu16(state);

        ushort result = alu.Shl(0x4000, 1);

        Assert.Equal(0x8000, result);
        Assert.False(state.CarryFlag);
        Assert.True(state.OverflowFlag);
    }

    [Fact]
    public void TestShr16OverflowDependsOnCount() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu16(state);

        ushort result = alu.Shr(0x8001, 1);

        Assert.Equal(0x4000, result);
        Assert.True(state.CarryFlag);
        Assert.True(state.OverflowFlag);

        state = new State(CpuModel.INTEL_80286);
        alu = new Alu16(state);

        result = alu.Shr(0x8001, 2);

        Assert.Equal(0x2000, result);
        Assert.False(state.CarryFlag);
        Assert.False(state.OverflowFlag);
    }

    [Fact]
    public void TestSar16ClearsOverflow() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu16(state);

        ushort result = alu.Sar(0xFF00, 1);

        Assert.Equal(0xFF80, result);
        Assert.False(state.CarryFlag);
        Assert.False(state.OverflowFlag);
    }

    [Fact]
    public void TestShl32UpdatesFlagsUsingOriginalMsb() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu32(state);

        uint result = alu.Shl(0x40000000u, 1);

        Assert.Equal(0x80000000u, result);
        Assert.False(state.CarryFlag);
        Assert.True(state.OverflowFlag);
    }

    [Fact]
    public void TestShr32OverflowDependsOnCount() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu32(state);

        uint result = alu.Shr(0x80000001u, 1);

        Assert.Equal(0x40000000u, result);
        Assert.True(state.CarryFlag);
        Assert.True(state.OverflowFlag);

        state = new State(CpuModel.INTEL_80286);
        alu = new Alu32(state);

        result = alu.Shr(0x80000001u, 2);

        Assert.Equal(0x20000000u, result);
        Assert.False(state.CarryFlag);
        Assert.False(state.OverflowFlag);
    }

    [Fact]
    public void TestSar32ClearsOverflow() {
        var state = new State(CpuModel.INTEL_80286);
        var alu = new Alu32(state);

        uint result = alu.Sar(0xFFFFFFF0u, 1);

        Assert.Equal(0xFFFFFFF8u, result);
        Assert.False(state.CarryFlag);
        Assert.False(state.OverflowFlag);
    }
}




