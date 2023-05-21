namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
/// Represents a VGA renderer.
/// </summary>
public interface IVgaRenderer {
    /// <summary>
    /// Render the current video memory to a buffer.
    /// </summary>
    /// <param name="buffer"></param>
    void Render(Span<uint> buffer);

    /// <summary>
    /// Calculate the current width required to render. 
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Calculate the current height required to render.
    /// </summary>
    int Height { get; }
}