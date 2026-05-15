namespace Spice86.Shared.Emulator.Storage.CdRom.Audio;

/// <summary>
/// Static helper that constructs the default <see cref="CompositeAudioCodecFactory"/>
/// used by <see cref="CueBinImage"/> when no factory is explicitly provided.
/// </summary>
public static class DefaultAudioCodecFactory {
    /// <summary>
    /// Creates a <see cref="CompositeAudioCodecFactory"/> composed of
    /// <see cref="WavAudioCodecFactory"/> (for WAVE) and
    /// <see cref="LibVlcAudioCodecFactory"/> (for everything LibVLC can decode).
    /// </summary>
    /// <returns>The default composite factory.</returns>
    public static CompositeAudioCodecFactory Create() {
        return new CompositeAudioCodecFactory(
            new WavAudioCodecFactory(),
            new LibVlcAudioCodecFactory());
    }
}
