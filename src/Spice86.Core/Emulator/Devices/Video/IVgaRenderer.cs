namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Represents a VGA renderer.
/// </summary>
public interface IVgaRenderer {
    /// <summary>
    ///     Calculate the current width required to render.
    /// </summary>
    int Width { get; }

    /// <summary>
    ///     Calculate the current height required to render.
    /// </summary>
    int Height { get; }

    /// <summary>
    ///     Gets the size of the buffer that was presented to the renderer.
    /// </summary>
    int BufferSize { get; }

    /// <summary>
    ///     Copy the latest completed frame to the provided buffer.
    /// </summary>
    /// <param name="buffer">The framebuffer used by the VGA card to draw the image on screen.</param>
    void Render(Span<uint> buffer);

    /// <summary>
    ///     Copy the last published frame without consuming the pending-frame flag.
    ///     Use this for non-destructive reads (e.g. MCP screenshots) that must not
    ///     interfere with the normal UI render loop.
    ///     Does nothing when no frame has been published yet.
    /// </summary>
    /// <param name="buffer">Destination buffer (must be at least <see cref="BufferSize"/> elements).</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="buffer"/> is smaller than the last published frame.
    /// </exception>
    void CopyLastFrame(Span<uint> buffer);
}