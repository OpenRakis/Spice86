namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>
/// Standard file-type tokens that may appear after the file name in a CUE
/// sheet <c>FILE</c> directive (e.g. <c>FILE "track.wav" WAVE</c>).
/// </summary>
public enum CueFileType
{
    /// <summary>Raw binary image (the default when no explicit type is supplied).</summary>
    Binary = 0,

    /// <summary>Big-endian raw binary image (Motorola byte order).</summary>
    Motorola = 1,

    /// <summary>Microsoft RIFF/WAVE PCM audio file.</summary>
    Wave = 2,

    /// <summary>Apple AIFF audio file.</summary>
    Aiff = 3,

    /// <summary>MPEG-1 Layer III audio file.</summary>
    Mp3 = 4,

    /// <summary>Free Lossless Audio Codec file.</summary>
    Flac = 5,

    /// <summary>Ogg Vorbis audio file.</summary>
    Ogg = 6,

    /// <summary>Opus audio file (Ogg container).</summary>
    Opus = 7,
}
