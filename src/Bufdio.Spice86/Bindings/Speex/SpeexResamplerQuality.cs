namespace Bufdio.Spice86.Bindings.Speex;

/// <summary>
/// Speex resampler quality settings.
/// Mirrors DOSBox Staging's Speex quality configuration.
/// Reference: speex_resampler.h
/// </summary>
public enum SpeexResamplerQuality {
    /// <summary>
    /// Fastest, lowest quality (quality level 0)
    /// </summary>
    Fastest = 0,
    
    /// <summary>
    /// Fast, low quality (quality level 3)
    /// </summary>
    Fast = 3,
    
    /// <summary>
    /// Medium quality, balanced (quality level 5)
    /// </summary>
    Medium = 5,
    
    /// <summary>
    /// High quality (quality level 8)
    /// </summary>
    High = 8,
    
    /// <summary>
    /// Best quality, slowest (quality level 10)
    /// </summary>
    Best = 10
}
