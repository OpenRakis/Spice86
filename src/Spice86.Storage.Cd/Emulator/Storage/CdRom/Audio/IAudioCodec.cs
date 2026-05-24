namespace Spice86.Shared.Emulator.Storage.CdRom.Audio;

/// <summary>
/// Opens an audio file and exposes its decoded contents as a raw CDDA-compatible
/// stream (44.1 kHz, 16-bit signed little-endian, stereo interleaved, 2352 bytes per sector).
/// </summary>
public interface IAudioCodec {
    /// <summary>
    /// Opens <paramref name="filePath"/> and returns an <see cref="IDataSource"/>
    /// whose bytes are CDDA-compatible PCM samples in playback order.
    /// </summary>
    /// <param name="filePath">Path to the audio file on disk.</param>
    /// <returns>An <see cref="IDataSource"/> over the decoded PCM data.</returns>
    IDataSource OpenAsCdda(string filePath);
}
