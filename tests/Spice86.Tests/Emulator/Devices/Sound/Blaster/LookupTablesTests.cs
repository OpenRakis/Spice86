namespace Spice86.Tests.Emulator.Devices.Sound.Blaster;

using FluentAssertions;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Xunit;

/// <summary>
/// Unit tests for Sound Blaster lookup tables (U8To16).
/// Validates against DOSBox Staging reference implementation.
/// Reference: DOSBox mixer.cpp:1744-1762 (u8to16 function and lut initialization)
/// </summary>
public class LookupTablesTests {
    
    /// <summary>
    /// Tests U8To16 lookup table against DOSBox Staging u8to16() function.
    /// DOSBox reference: mixer.cpp:1744-1752
    /// Algorithm: u_val - 128, if positive: round(s_val * 32767/127), else: s_val * 256
    /// </summary>
    [Theory]
    [InlineData(0, -32768)]      // Min: 0 - 128 = -128, -128 * 256 = -32768
    [InlineData(128, 0)]          // Center: 128 - 128 = 0
    [InlineData(255, 32767)]      // Max: 255 - 128 = 127, round(127 * 32767/127) = 32767
    [InlineData(129, 258)]        // 129 - 128 = 1, round(1 * 32767/127) = 258
    [InlineData(127, -256)]       // 127 - 128 = -1, -1 * 256 = -256
    [InlineData(1, -32512)]       // 1 - 128 = -127, -127 * 256 = -32512
    public void U8To16_MatchesDOSBoxValues(byte input, int expected) {
        // Act
        float result = LookupTables.U8To16[input];
        
        // Assert
        result.Should().Be(expected, $"U8To16[{input}] should match DOSBox u8to16({input})");
    }
    
    /// <summary>
    /// Tests ToSigned8() method against DOSBox Staging behavior.
    /// ToSigned8(sbyte) internally uses U8To16[sbyte + 128].
    /// </summary>
    [Theory]
    [InlineData(-128, -32768)]    // Min: -128 * 256 = -32768
    [InlineData(0, 0)]             // Center
    [InlineData(127, 32767)]       // Max: round(127 * 32767/127) = 32767
    [InlineData(1, 258)]           // Positive: round(1 * 32767/127) = 258
    [InlineData(-1, -256)]         // Negative: -1 * 256 = -256
    [InlineData(-127, -32512)]     // -127 * 256 = -32512
    public void ToSigned8_MatchesDOSBoxValues(sbyte input, int expected) {
        // Act
        float result = LookupTables.ToSigned8(input);
        
        // Assert
        result.Should().Be(expected, $"ToSigned8({input}) should match DOSBox conversion");
        
        // Verify the underlying U8To16 array access
        byte index = (byte)((int)input + 128);
        LookupTables.U8To16[index].Should().Be(expected);
    }
    
    /// <summary>
    /// Validates that U8To16 creates a smooth monotonic curve from -32768 to +32767.
    /// </summary>
    [Fact]
    public void U8To16_CreatesMonotonicCurve() {
        // Assert: Values should increase monotonically
        for (int i = 1; i < 256; i++) {
            float current = LookupTables.U8To16[i];
            float previous = LookupTables.U8To16[i - 1];
            current.Should().BeGreaterThan(previous, $"U8To16 should be monotonically increasing at index {i}");
        }
    }
    
    /// <summary>
    /// Tests that zero-crossing point for unsigned 8-bit is at 128.
    /// </summary>
    [Fact]
    public void U8To16_ZeroCrossingAt128() {
        // Assert
        LookupTables.U8To16[128].Should().Be(0, "Unsigned 8-bit value 128 should map to 0");
        LookupTables.U8To16[127].Should().BeLessThan(0, "Values below 128 should be negative");
        LookupTables.U8To16[129].Should().BeGreaterThan(0, "Values above 128 should be positive");
    }
    
    /// <summary>
    /// Tests the range of values in the U8To16 lookup table.
    /// DOSBox uses int16 range: [-32768, 32767]
    /// </summary>
    [Fact]
    public void U8To16_StaysWithinInt16Range() {
        // U8To16
        foreach (float value in LookupTables.U8To16) {
            value.Should().BeGreaterThanOrEqualTo(-32768, "U8To16 values should not go below int16 min");
            value.Should().BeLessThanOrEqualTo(32767, "U8To16 values should not exceed int16 max");
        }
    }
}
