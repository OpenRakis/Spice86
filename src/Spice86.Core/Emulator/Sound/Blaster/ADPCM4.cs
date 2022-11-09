namespace Spice86.Core.Emulator.Sound.Blaster;

using System;

/// <summary>
/// 4-bit ADPCM decoder.
/// </summary>
internal sealed class ADPCM4 : ADPCMDecoder {
    /// <summary>
    /// The limit value.
    /// </summary>
    private const int Limit = 5;

    /// <summary>
    /// Initializes a new instance of the ADPCM4 class.
    /// </summary>
    public ADPCM4()
        : base(2) {
    }

    /// <summary>
    /// Decodes a block of ADPCM compressed data.
    /// </summary>
    /// <param name="source">Source array containing ADPCM data to decode.</param>
    /// <param name="sourceOffset">Offset in source array to start decoding.</param>
    /// <param name="count">Number of bytes to decode.</param>
    /// <param name="destination">Destination array to write decoded PCM data.</param>
    /// <param name="destinationOffset">Offset in destination array to start writing.</param>
    public override void Decode(byte[] source, int sourceOffset, int count, Span<byte> destination) {
        byte current = Reference;

        for (int i = 0; i < count; i++) {
            int sample = source[sourceOffset + i] & 0x0F;
            current = DecodeSample(current, sample);
            destination[i * 2] = current;

            sample = source[sourceOffset + i] >> 4 & 0x0F;
            current = DecodeSample(current, sample);
            destination[(i * 2) + 1] = current;
        }

        Reference = current;
    }

    /// <summary>
    /// Decodes a 4-bit sample into an 8-bit sample.
    /// </summary>
    /// <param name="current">Current prediction value.</param>
    /// <param name="sample">4-bit sample to decode.</param>
    /// <returns>Decoded 8-bit sample.</returns>
    private byte DecodeSample(byte current, int sample) {
        if ((sample & 0x08) == 0) {
            current += (byte)(sample << _step);
        } else {
            current -= (byte)((sample & 0x07) << _step);
        }

        if (current >= Limit) {
            _step++;
            if (_step > 3) {
                _step = 3;
            }
        } else if (current == 0) {
            _step--;
            if (_step < 0) {
                _step = 0;
            }
        }

        return current;
    }
}
