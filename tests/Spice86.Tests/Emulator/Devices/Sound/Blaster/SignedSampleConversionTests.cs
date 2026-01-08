namespace Spice86.Tests.Emulator.Devices.Sound.Blaster;

using Xunit;
using FluentAssertions;
using Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// Tests validating the correct conversion of signed 8-bit samples to lookup table indices.
/// This is the critical fix for PCM static noise bug.
/// Reference: DOSBox Staging src/hardware/audio/soundblaster.cpp:1214-1220
/// </summary>
public class SignedSampleConversionTests {
    
    [Fact]
    public void SignedByte_NegativeOne_MapsToCorrectLookupIndex() {
        // Byte 0xFF represents -1 as signed int8
        // Should map to S8To16[127] = -256, NOT S8To16[255] = 32767
        
        byte byteValue = 0xFF;
        sbyte signedValue = unchecked((sbyte)byteValue);
        signedValue.Should().Be(-1, "0xFF as signed byte is -1");
        
        byte lookupIndex = (byte)(signedValue + 128);
        lookupIndex.Should().Be(127, "signed -1 + 128 = 127");
        
        float convertedValue = LookupTables.S8To16[lookupIndex];
        convertedValue.Should().Be(-256.0f, "S8To16[127] should be -256");
        
        // WRONG behavior (the bug we fixed):
        // float wrongValue = LookupTables.S8To16[byteValue]; // S8To16[255] = 32767
        // This would give 32767 instead of -256!
    }
    
    [Fact]
    public void SignedByte_PositiveMax_MapsToCorrectLookupIndex() {
        // Byte 0x7F represents +127 as signed int8
        // Should map to S8To16[255] = 32767
        
        byte byteValue = 0x7F;
        sbyte signedValue = unchecked((sbyte)byteValue);
        signedValue.Should().Be(127, "0x7F as signed byte is 127");
        
        byte lookupIndex = (byte)(signedValue + 128);
        lookupIndex.Should().Be(255, "signed 127 + 128 = 255");
        
        float convertedValue = LookupTables.S8To16[lookupIndex];
        convertedValue.Should().Be(32767.0f, "S8To16[255] should be 32767");
    }
    
    [Fact]
    public void SignedByte_NegativeMax_MapsToCorrectLookupIndex() {
        // Byte 0x80 represents -128 as signed int8
        // Should map to S8To16[0] = -32768
        
        byte byteValue = 0x80;
        sbyte signedValue = unchecked((sbyte)byteValue);
        signedValue.Should().Be(-128, "0x80 as signed byte is -128");
        
        byte lookupIndex = (byte)(signedValue + 128);
        lookupIndex.Should().Be(0, "signed -128 + 128 = 0");
        
        float convertedValue = LookupTables.S8To16[lookupIndex];
        convertedValue.Should().Be(-32768.0f, "S8To16[0] should be -32768");
    }
    
    [Fact]
    public void SignedByte_Zero_MapsToCorrectLookupIndex() {
        // Byte 0x00 represents 0 as signed int8
        // Should map to S8To16[128] = 0
        
        byte byteValue = 0x00;
        sbyte signedValue = unchecked((sbyte)byteValue);
        signedValue.Should().Be(0, "0x00 as signed byte is 0");
        
        byte lookupIndex = (byte)(signedValue + 128);
        lookupIndex.Should().Be(128, "signed 0 + 128 = 128");
        
        float convertedValue = LookupTables.S8To16[lookupIndex];
        convertedValue.Should().Be(0.0f, "S8To16[128] should be 0");
    }
    
    [Fact]
    public void SignedByte_AllNegativeValues_MapToLowerHalfOfTable() {
        // All negative signed bytes (-128 to -1) should map to indices 0-127
        for (int i = 0; i < 128; i++) {
            byte byteValue = (byte)(0x80 + i);  // 0x80 to 0xFF
            sbyte signedValue = unchecked((sbyte)byteValue);
            
            signedValue.Should().BeNegative("bytes 0x80-0xFF are negative as signed");
            
            byte lookupIndex = (byte)(signedValue + 128);
            lookupIndex.Should().BeLessThan(128, "negative values map to lower half of table");
            
            float convertedValue = LookupTables.S8To16[lookupIndex];
            convertedValue.Should().BeLessThanOrEqualTo(0.0f, "negative signed bytes produce non-positive values");
        }
    }
    
    [Fact]
    public void SignedByte_AllPositiveValues_MapToUpperHalfOfTable() {
        // All positive signed bytes (0 to 127) should map to indices 128-255
        for (int i = 0; i < 128; i++) {
            byte byteValue = (byte)i;  // 0x00 to 0x7F
            sbyte signedValue = unchecked((sbyte)byteValue);
            
            signedValue.Should().BeGreaterThanOrEqualTo((sbyte)0, "bytes 0x00-0x7F are non-negative as signed");
            
            byte lookupIndex = (byte)(signedValue + 128);
            lookupIndex.Should().BeGreaterThanOrEqualTo((byte)128, "non-negative values map to upper half of table");
            
            float convertedValue = LookupTables.S8To16[lookupIndex];
            convertedValue.Should().BeGreaterThanOrEqualTo(0.0f, "non-negative signed bytes produce non-negative values");
        }
    }
    
    [Fact]
    public void SignedConversion_MatchesDOSBoxBehavior() {
        // Verify the conversion matches DOSBox's behavior:
        // DOSBox: const auto signed_buf = reinterpret_cast<int8_t*>(sb.dma.buf.b8);
        //         return lut_s8to16[signed_buf[i]];
        // C# equivalent: sbyte signedSample = unchecked((sbyte)samples[i]);
        //                byte lookupIndex = (byte)(signedSample + 128);
        //                value = LookupTables.S8To16[lookupIndex];
        
        // Test a few key values
        byte[] testBytes = new byte[] { 0x00, 0x7F, 0x80, 0xFF };
        sbyte[] expectedSigned = new sbyte[] { 0, 127, -128, -1 };
        byte[] expectedIndices = new byte[] { 128, 255, 0, 127 };
        
        for (int i = 0; i < testBytes.Length; i++) {
            sbyte signedValue = unchecked((sbyte)testBytes[i]);
            signedValue.Should().Be(expectedSigned[i], $"byte 0x{testBytes[i]:X2} as signed");
            
            byte lookupIndex = (byte)(signedValue + 128);
            lookupIndex.Should().Be(expectedIndices[i], $"lookup index for 0x{testBytes[i]:X2}");
        }
    }
    
    [Fact]
    public void UnsignedByte_DirectIndexing_IsCorrect() {
        // Unsigned samples should use the byte value directly as index
        // This was already correct, just validating it here
        
        byte unsignedValue = 0xFF;
        float convertedValue = LookupTables.U8To16[unsignedValue];
        convertedValue.Should().Be(32767.0f, "U8To16[255] = 32767");
        
        unsignedValue = 0x00;
        convertedValue = LookupTables.U8To16[unsignedValue];
        convertedValue.Should().Be(-32768.0f, "U8To16[0] = -32768");
        
        unsignedValue = 0x80;
        convertedValue = LookupTables.U8To16[unsignedValue];
        convertedValue.Should().Be(0.0f, "U8To16[128] = 0 (zero-crossing for unsigned)");
    }
}
