namespace Bufdio.Spice86.Bindings.Speex;

/// <summary>
/// Speex resampler error codes.
/// Reference: speex_resampler.h
/// </summary>
public enum SpeexError {
    /// <summary>
    /// No error
    /// </summary>
    Success = 0,
    
    /// <summary>
    /// Memory allocation failed
    /// </summary>
    AllocFailed = 1,
    
    /// <summary>
    /// Bad resampler state
    /// </summary>
    BadState = 2,
    
    /// <summary>
    /// Invalid argument
    /// </summary>
    InvalidArg = 3,
    
    /// <summary>
    /// Invalid input pointer
    /// </summary>
    PtrOverlap = 4
}
