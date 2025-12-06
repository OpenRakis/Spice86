namespace Bufdio.Spice86.Bindings.PortAudio.Enums;

/// <summary>
/// Specifies the result codes that can be returned from a PortAudio stream callback.
/// </summary>
internal enum PaStreamCallbackResult {
    /// <summary>
    /// Continue processing audio.
    /// </summary>
    paContinue = 0,
    
    /// <summary>
    /// Complete processing and stop the stream gracefully.
    /// </summary>
    paComplete = 1,
    
    /// <summary>
    /// Abort processing and stop the stream immediately.
    /// </summary>
    paAbort = 2
}