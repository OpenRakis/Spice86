namespace Spice86.Aeon.Emulator.Video.Rendering; 

/// <summary>
/// Enumeration of different scaling algorithms that can be used when rendering video.
/// </summary>
public enum ScalingAlgorithm {
    /// <summary>
    /// No scaling algorithm is used.
    /// </summary>
    None,

    /// <summary>
    /// Scale2x algorithm doubles the size of each pixel.
    /// </summary>
    Scale2x,

    /// <summary>
    /// Scale3x algorithm triples the size of each pixel.
    /// </summary>
    Scale3x
}