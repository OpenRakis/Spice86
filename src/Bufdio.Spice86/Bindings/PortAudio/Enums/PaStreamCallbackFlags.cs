namespace Bufdio.Spice86.Bindings.PortAudio.Enums;

/// <summary>
/// Flags that can be passed to a PortAudio stream callback to indicate buffer status.
/// </summary>
internal enum PaStreamCallbackFlags : long {
    /// <summary>
    /// Input buffer underflow detected.
    /// </summary>
    paInputUnderflow = 0x00000001,
    
    /// <summary>
    /// Input buffer overflow detected.
    /// </summary>
    paInputOverflow = 0x00000002,
    
    /// <summary>
    /// Output buffer underflow detected.
    /// </summary>
    paOutputUnderflow = 0x00000004,
    
    /// <summary>
    /// Output buffer overflow detected.
    /// </summary>
    paOutputOverflow = 0x00000008,
    
    /// <summary>
    /// Output is being primed.
    /// </summary>
    paPrimingOutput = 0x00000010
}