// SPDX-License-Identifier: GPL-2.0-or-later
// ADPCM decoder implementations ported from DOSBox Staging
// Reference: src/hardware/audio/soundblaster.cpp lines 863-958

namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using System;

/// <summary>
/// ADPCM (Adaptive Differential Pulse Code Modulation) decoders for Sound Blaster.
/// Supports 2-bit, 3-bit, and 4-bit ADPCM formats with adaptive step-size quantization.
/// </summary>
public static class AdpcmDecoders {
    /// <summary>
    /// Decodes a single ADPCM portion using the specified mapping tables.
    /// Updates the reference sample and step size based on the bit portion value.
    /// </summary>
    /// <param name="bitPortion">The compressed bit portion to decode</param>
    /// <param name="adjustMap">Step size adjustment lookup table</param>
    /// <param name="scaleMap">Sample delta lookup table</param>
    /// <param name="lastIndex">Maximum valid index in the lookup tables</param>
    /// <param name="reference">Current reference sample (0-255), updated by this method</param>
    /// <param name="stepsize">Current step size (0-255), updated by this method</param>
    /// <returns>Decoded 8-bit unsigned sample value</returns>
    private static byte DecodeAdpcmPortion(
        int bitPortion,
        ReadOnlySpan<byte> adjustMap,
        ReadOnlySpan<sbyte> scaleMap,
        int lastIndex,
        ref byte reference,
        ref byte stepsize) {
        
        int i = Math.Clamp(bitPortion + stepsize, 0, lastIndex);
        
        stepsize = (byte)((stepsize + adjustMap[i]) & 0xFF);
        
        int newSample = reference + scaleMap[i];
        reference = (byte)Math.Clamp(newSample, 0, 255);
        
        return reference;
    }
    
    /// <summary>
    /// Decodes one byte of 2-bit ADPCM data into 4 samples.
    /// 2-bit ADPCM uses 2 bits per sample, so 1 byte = 4 samples.
    /// </summary>
    /// <param name="data">Compressed byte containing 4 samples</param>
    /// <param name="reference">Current reference sample (0-255), updated by this method</param>
    /// <param name="stepsize">Current step size (0-255), updated by this method</param>
    /// <returns>Array of 4 decoded samples</returns>
    public static byte[] DecodeAdpcm2Bit(byte data, ref byte reference, ref byte stepsize) {
        // Scale map: delta values to add to reference sample
        ReadOnlySpan<sbyte> scaleMap = stackalloc sbyte[] {
             0,  1,  0,  -1,  1,  3,  -1,  -3,
             2,  6, -2,  -6,  4, 12,  -4, -12,
             8, 24, -8, -24,  6, 48, -16, -48
        };
        
        // Adjust map: step size adjustments
        ReadOnlySpan<byte> adjustMap = stackalloc byte[] {
              0,   4,   0,   4,
            252,   4, 252,   4, 252,   4, 252,   4,
            252,   4, 252,   4, 252,   4, 252,   4,
            252,   0, 252,   0
        };
        
        const int lastIndex = 23; // Length of tables - 1
        
        byte[] samples = new byte[4];
        samples[0] = DecodeAdpcmPortion((data >> 6) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[1] = DecodeAdpcmPortion((data >> 4) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[2] = DecodeAdpcmPortion((data >> 2) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[3] = DecodeAdpcmPortion((data >> 0) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        
        return samples;
    }
    
    /// <summary>
    /// Decodes one byte of 3-bit ADPCM data into 3 samples (with 2 bits unused).
    /// 3-bit ADPCM uses 3 bits per sample, fitting 2.67 samples per byte.
    /// </summary>
    /// <param name="data">Compressed byte containing 3 samples</param>
    /// <param name="reference">Current reference sample (0-255), updated by this method</param>
    /// <param name="stepsize">Current step size (0-255), updated by this method</param>
    /// <returns>Array of 3 decoded samples</returns>
    public static byte[] DecodeAdpcm3Bit(byte data, ref byte reference, ref byte stepsize) {
        // Scale map: delta values to add to reference sample
        ReadOnlySpan<sbyte> scaleMap = stackalloc sbyte[] {
             0,  1,  2,  3,  0,  -1,  -2,  -3,
             1,  3,  5,  7, -1,  -3,  -5,  -7,
             2,  6, 10, 14, -2,  -6, -10, -14,
             4, 12, 20, 28, -4, -12, -20, -28,
             5, 15, 25, 35, -5, -15, -25, -35
        };
        
        // Adjust map: step size adjustments
        ReadOnlySpan<byte> adjustMap = stackalloc byte[] {
              0, 0, 0,   8,   0, 0, 0,   8,
            248, 0, 0,   8, 248, 0, 0,   8,
            248, 0, 0,   8, 248, 0, 0,   8,
            248, 0, 0,   8, 248, 0, 0,   8,
            248, 0, 0,   0, 248, 0, 0,   0
        };
        
        const int lastIndex = 39; // Length of tables - 1
        
        byte[] samples = new byte[3];
        samples[0] = DecodeAdpcmPortion((data >> 5) & 0x7, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[1] = DecodeAdpcmPortion((data >> 2) & 0x7, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[2] = DecodeAdpcmPortion((data & 0x3) << 1, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        
        return samples;
    }
    
    /// <summary>
    /// Decodes one byte of 4-bit ADPCM data into 2 samples.
    /// 4-bit ADPCM uses 4 bits per sample, so 1 byte = 2 samples.
    /// </summary>
    /// <param name="data">Compressed byte containing 2 samples</param>
    /// <param name="reference">Current reference sample (0-255), updated by this method</param>
    /// <param name="stepsize">Current step size (0-255), updated by this method</param>
    /// <returns>Array of 2 decoded samples</returns>
    public static byte[] DecodeAdpcm4Bit(byte data, ref byte reference, ref byte stepsize) {
        // Scale map: delta values to add to reference sample
        ReadOnlySpan<sbyte> scaleMap = stackalloc sbyte[] {
             0,  1,  2,  3,  4,  5,  6,  7,  0,  -1,  -2,  -3,  -4,  -5,  -6,  -7,
             1,  3,  5,  7,  9, 11, 13, 15, -1,  -3,  -5,  -7,  -9, -11, -13, -15,
             2,  6, 10, 14, 18, 22, 26, 30, -2,  -6, -10, -14, -18, -22, -26, -30,
             4, 12, 20, 28, 36, 44, 52, 60, -4, -12, -20, -28, -36, -44, -52, -60
        };
        
        // Adjust map: step size adjustments
        ReadOnlySpan<byte> adjustMap = stackalloc byte[] {
              0, 0, 0, 0, 0, 16, 16, 16,
              0, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0,  0,  0,  0,
            240, 0, 0, 0, 0,  0,  0,  0
        };
        
        const int lastIndex = 63; // Length of tables - 1
        
        byte[] samples = new byte[2];
        samples[0] = DecodeAdpcmPortion(data >> 4, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[1] = DecodeAdpcmPortion(data & 0xF, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        
        return samples;
    }
}
