namespace Spice86.Emulator.Sound.Blaster;

using System;

/// <summary>
/// 3-bit ADPCM decoder.
/// </summary>
internal sealed class ADPCM3 : ADPCM2
{
    /// <summary>
    /// The limit value.
    /// </summary>
    private const int Limit = 3;

    /// <summary>
    /// Initializes a new instance of the ADPCM3 class.
    /// </summary>
    public ADPCM3()
        : base(3)
    {
    }

    /// <summary>
    /// Decodes a block of ADPCM compressed data.
    /// </summary>
    /// <param name="source">Source array containing ADPCM data to decode.</param>
    /// <param name="sourceOffset">Offset in source array to start decoding.</param>
    /// <param name="count">Number of bytes to decode.</param>
    /// <param name="destination">Destination buffer to write decoded PCM data.</param>
    public override void Decode(byte[] source, int sourceOffset, int count, Span<byte> destination)
    {
        byte current = this.Reference;

        for (int i = 0; i < count; i++)
        {
            int sample = source[sourceOffset + i] & 0x07;
            current = DecodeSample(current, sample);
            destination[(i * 3)] = current;

            sample = (source[sourceOffset + i] >> 3) & 0x07;
            current = DecodeSample(current, sample);
            destination[(i * 3) + 1] = current;

            sample = (source[sourceOffset + i] >> 6) & 0x03;
            current = base.DecodeSample(current, sample);
            destination[(i * 3) + 2] = current;
        }

        this.Reference = current;
    }

    /// <summary>
    /// Decodes a 3-bit sample into an 8-bit sample.
    /// </summary>
    /// <param name="current">Current prediction value.</param>
    /// <param name="sample">3-bit sample to decode.</param>
    /// <returns>Decoded 8-bit sample.</returns>
    private new byte DecodeSample(byte current, int sample)
    {
        if ((sample & 0x04) == 0) {
            current += (byte)(sample << this.step);
        } else {
            current -= (byte)((sample & 0x03) << this.step);
        }

        if (current >= Limit)
        {
            this.step++;
            if (this.step > 3) {
                this.step = 3;
            }
        }
        else if (current == 0)
        {
            this.step--;
            if (this.step < 0) {
                this.step = 0;
            }
        }

        return current;
    }
}
