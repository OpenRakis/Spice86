namespace Spice86.Shared.Emulator.Storage.CdRom.Audio;

/// <summary>
/// Selects an <see cref="IAudioCodec"/> implementation that can decode a particular
/// CUE-referenced audio file.
/// </summary>
public interface IAudioCodecFactory {
    /// <summary>
    /// Returns <see langword="true"/> when this factory can produce a codec for the
    /// given <paramref name="fileType"/> and <paramref name="filePath"/>.
    /// </summary>
    /// <param name="fileType">CUE-declared file type (BINARY/WAVE/MP3/...).</param>
    /// <param name="filePath">Path to the file referenced by the CUE entry.</param>
    /// <returns><see langword="true"/> if <see cref="Create"/> can be invoked.</returns>
    bool CanHandle(CueFileType fileType, string filePath);

    /// <summary>
    /// Creates a fresh <see cref="IAudioCodec"/> instance.
    /// </summary>
    /// <returns>A new codec instance.</returns>
    IAudioCodec Create();
}
