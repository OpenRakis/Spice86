namespace Spice86.Tests.Video;

using FluentAssertions;
using Spice86.Core.Emulator.Devices.Video;
using Xunit;

/// <summary>
/// TDD test suite for BitManipulationExtensions.ToBits() 
/// Validates correctness across all 256 byte values and prevents regressions during optimization.
/// </summary>
[Trait("Category", "Video")]
public class BitManipulationExtensionsTests {
    /// <summary>
    /// Tests that ToBits() returns correct array for byte 0x00 (all bits zero).
    /// </summary>
    [Fact]
    public void ToBits_ZeroByte_ReturnsAllFalse() {
        byte value = 0x00;
        bool[] bits = value.ToBits();

        bits.Should().HaveCount(8);
        bits.Should().AllSatisfy(b => b.Should().BeFalse());
    }

    /// <summary>
    /// Tests that ToBits() returns correct array for byte 0xFF (all bits set).
    /// </summary>
    [Fact]
    public void ToBits_MaxByte_ReturnsAllTrue() {
        byte value = 0xFF;
        bool[] bits = value.ToBits();

        bits.Should().HaveCount(8);
        bits.Should().AllSatisfy(b => b.Should().BeTrue());
    }

    /// <summary>
    /// Tests single bit set at position 0 (LSB).
    /// </summary>
    [Fact]
    public void ToBits_SingleBitLSB_CorrectPosition() {
        byte value = 0x01; // binary: 00000001
        bool[] bits = value.ToBits();

        bits.Should().HaveCount(8);
        bits[0].Should().BeTrue();
        for (int i = 1; i < 8; i++) {
            bits[i].Should().BeFalse();
        }
    }

    /// <summary>
    /// Tests single bit set at position 7 (MSB).
    /// </summary>
    [Fact]
    public void ToBits_SingleBitMSB_CorrectPosition() {
        byte value = 0x80; // binary: 10000000
        bool[] bits = value.ToBits();

        bits.Should().HaveCount(8);
        bits[7].Should().BeTrue();
        for (int i = 0; i < 7; i++) {
            bits[i].Should().BeFalse();
        }
    }

    /// <summary>
    /// Tests alternating bit pattern (0xAA = 10101010).
    /// </summary>
    [Fact]
    public void ToBits_AlternatingPattern_CorrectBits() {
        byte value = 0xAA; // binary: 10101010
        bool[] bits = value.ToBits();

        bits.Should().HaveCount(8);
        bits[0].Should().BeFalse();
        bits[1].Should().BeTrue();
        bits[2].Should().BeFalse();
        bits[3].Should().BeTrue();
        bits[4].Should().BeFalse();
        bits[5].Should().BeTrue();
        bits[6].Should().BeFalse();
        bits[7].Should().BeTrue();
    }

    /// <summary>
    /// Tests complementary alternating pattern (0x55 = 01010101).
    /// </summary>
    [Fact]
    public void ToBits_ComplementaryPattern_CorrectBits() {
        byte value = 0x55; // binary: 01010101
        bool[] bits = value.ToBits();

        bits.Should().HaveCount(8);
        bits[0].Should().BeTrue();
        bits[1].Should().BeFalse();
        bits[2].Should().BeTrue();
        bits[3].Should().BeFalse();
        bits[4].Should().BeTrue();
        bits[5].Should().BeFalse();
        bits[6].Should().BeTrue();
        bits[7].Should().BeFalse();
    }

    /// <summary>
    /// Tests arbitrary value 0x42 (binary: 01000010).
    /// </summary>
    [Fact]
    public void ToBits_ArbitraryValue_CorrectBits() {
        byte value = 0x42; // binary: 01000010
        bool[] bits = value.ToBits();

        bits.Should().HaveCount(8);
        bits[0].Should().BeFalse(); // bit 0
        bits[1].Should().BeTrue();  // bit 1
        bits[2].Should().BeFalse(); // bit 2
        bits[3].Should().BeFalse(); // bit 3
        bits[4].Should().BeFalse(); // bit 4
        bits[5].Should().BeFalse(); // bit 5
        bits[6].Should().BeTrue();  // bit 6
        bits[7].Should().BeFalse(); // bit 7
    }

    /// <summary>
    /// Tests all 256 possible byte values for correctness.
    /// Comprehensive validation that optimization doesn't break any value.
    /// </summary>
    [Fact]
    public void ToBits_AllByteValues_CorrectBits() {
        for (int byteValue = 0; byteValue <= 255; byteValue++) {
            byte value = (byte)byteValue;
            bool[] bits = value.ToBits();

            bits.Should().HaveCount(8);

            for (int bitIndex = 0; bitIndex < 8; bitIndex++) {
                bool expectedBit = (value & (1 << bitIndex)) != 0;
                bits[bitIndex].Should().Be(expectedBit, 
                    because: $"byte 0x{value:X2} bit {bitIndex} should be {expectedBit}");
            }
        }
    }

    /// <summary>
    /// Tests that returned array can be modified without affecting future calls.
    /// Ensures optimization doesn't return shared/cached arrays that could be mutated.
    /// </summary>
    [Fact]
    public void ToBits_ArrayIndependence_EachCallIndependent() {
        const byte value = 0x0F; // binary: 00001111

        bool[] bits1 = value.ToBits();
        bool[] bits2 = value.ToBits();

        // Modify first array
        bits1[0] = false;
        bits1[7] = true;

        // Second array should be unaffected (arrays independent) and retain original values
        bits2[0].Should().BeTrue();  // bit 0 of 0x0F is set
        bits2[7].Should().BeFalse(); // bit 7 of 0x0F is not set
    }
}
