namespace Bufdio.Spice86.Bindings.PortAudio.Enums;

/// <summary>
/// Flags used to control the behavior of a PortAudio stream.
/// </summary>
internal enum PaStreamFlags : long {
    /// <summary>
    /// No flags set (default behavior).
    /// </summary>
    paNoFlag = 0,
    
    /// <summary>
    /// Disable clipping of out-of-range samples.
    /// </summary>
    paClipOff = 0x00000001,
    
    /// <summary>
    /// Disable dithering.
    /// </summary>
    paDitherOff = 0x00000002,
    
    /// <summary>
    /// Prime output buffers using the stream callback.
    /// </summary>
    paPrimeOutputBuffersUsingStreamCallback = 0x00000008,
    
    /// <summary>
    /// Platform-specific flags mask.
    /// </summary>
    paPlatformSpecificFlags = 0xFFFF0000
}