namespace Spice86.Emulator.Sound.Blaster;

/// <summary>
/// Represents a Sound Blaster PCM compression level.
/// </summary>
internal enum CompressionLevel {
    /// <summary>
    /// The data is not compressed.
    /// </summary>
    None,
    /// <summary>
    /// The data is compressed using ADPCM2.
    /// </summary>
    ADPCM2,
    /// <summary>
    /// The data is compressed using ADPCM3.
    /// </summary>
    ADPCM3,
    /// <summary>
    /// The data is compressed using ADPCM4.
    /// </summary>
    ADPCM4
}
