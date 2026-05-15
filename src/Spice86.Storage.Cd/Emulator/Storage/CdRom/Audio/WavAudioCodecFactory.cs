namespace Spice86.Shared.Emulator.Storage.CdRom.Audio;

/// <summary>Factory that creates <see cref="WavAudioCodec"/> instances for WAVE-typed CUE entries.</summary>
public sealed class WavAudioCodecFactory : IAudioCodecFactory {
    /// <inheritdoc/>
    public bool CanHandle(CueFileType fileType, string filePath) {
        return fileType == CueFileType.Wave;
    }

    /// <inheritdoc/>
    public IAudioCodec Create() {
        return new WavAudioCodec();
    }
}
