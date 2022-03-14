namespace Spice86.Emulator.Sound.Blaster;

using System;

/// <summary>
/// Base class for ADPCM decoders.
/// </summary>
internal abstract class ADPCMDecoder
{
    /// <summary>
    /// The current step value.
    /// </summary>
    protected int step;

    /// <summary>
    /// Initializes a new instance of the ADPCMDecoder class.
    /// </summary>
    /// <param name="factor">The compression factor.</param>
    protected ADPCMDecoder(int factor)
    {
        this.CompressionFactor = factor;
    }

    /// <summary>
    /// Gets or sets the decoder reference byte.
    /// </summary>
    public byte Reference { get; set; }
    /// <summary>
    /// Gets the compression ratio.
    /// </summary>
    public int CompressionFactor { get; }

    /// <summary>
    /// Resets the decoder to its initial state.
    /// </summary>
    public void Reset()
    {
        this.step = 0;
        this.Reference = 0;
    }

    /// <summary>
    /// Decodes a block of ADPCM compressed data.
    /// </summary>
    /// <param name="source">Source array containing ADPCM data to decode.</param>
    /// <param name="sourceOffset">Offset in source array to start decoding.</param>
    /// <param name="count">Number of bytes to decode.</param>
    /// <param name="destination">Destination buffer to write decoded PCM data.</param>
    public abstract void Decode(byte[] source, int sourceOffset, int count, Span<byte> destination);
}
