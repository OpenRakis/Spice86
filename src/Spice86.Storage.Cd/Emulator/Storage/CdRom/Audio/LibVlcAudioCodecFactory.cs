namespace Spice86.Shared.Emulator.Storage.CdRom.Audio;

/// <summary>
/// Factory that produces <see cref="LibVlcAudioCodec"/> instances for the compressed
/// CUE audio types LibVLC can decode (MP3, FLAC, OGG, OPUS, AIFF, Motorola big-endian PCM).
/// </summary>
public sealed class LibVlcAudioCodecFactory : IAudioCodecFactory {
    /// <inheritdoc/>
    public bool CanHandle(CueFileType fileType, string filePath) {
        return fileType switch {
            CueFileType.Mp3 => true,
            CueFileType.Flac => true,
            CueFileType.Ogg => true,
            CueFileType.Opus => true,
            CueFileType.Aiff => true,
            CueFileType.Motorola => true,
            _ => false,
        };
    }

    /// <inheritdoc/>
    public IAudioCodec Create() {
        return new LibVlcAudioCodec();
    }
}
