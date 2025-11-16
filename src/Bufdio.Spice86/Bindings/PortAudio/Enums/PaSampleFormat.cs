namespace Bufdio.Spice86.Bindings.PortAudio.Enums;

/// <summary>
/// Specifies the sample formats supported by PortAudio.
/// </summary>
internal enum PaSampleFormat : long {
    /// <summary>
    /// 32-bit floating point samples.
    /// </summary>
    paFloat32 = 0x00000001,
    
    /// <summary>
    /// 32-bit signed integer samples.
    /// </summary>
    paInt32 = 0x00000002,
    
    /// <summary>
    /// 24-bit signed integer samples (packed).
    /// </summary>
    paInt24 = 0x00000004,
    
    /// <summary>
    /// 16-bit signed integer samples.
    /// </summary>
    paInt16 = 0x00000008,
    
    /// <summary>
    /// 8-bit signed integer samples.
    /// </summary>
    paInt8 = 0x00000010,
    
    /// <summary>
    /// 8-bit unsigned integer samples.
    /// </summary>
    paUInt8 = 0x00000020,
    
    /// <summary>
    /// Custom sample format.
    /// </summary>
    paCustomFormat = 0x00010000,
    
    /// <summary>
    /// Non-interleaved buffer organization.
    /// </summary>
    paNonInterleaved = 0x80000000,
}