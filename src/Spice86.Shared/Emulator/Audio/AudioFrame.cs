namespace Spice86.Shared.Emulator.Audio;

/// <summary>
/// Represents a single audio frame.
/// </summary>
public struct AudioFrame<T> where T : struct
{
    /// <summary>
    /// Represents the left audio channel.
    /// </summary>
    public T Left { get; set; }

    /// <summary>
    /// Represents the right audio channel.
    /// </summary>
    public T Right { get; set; }
}
