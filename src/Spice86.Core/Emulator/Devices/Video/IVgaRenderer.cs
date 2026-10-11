namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Represents a VGA renderer.
/// </summary>
public interface IVgaRenderer {
    /// <summary>Dimensions and presentation scale of the last completed logical frame, or initial hardware geometry.</summary>
    VideoFrameInfo FrameInfo { get; }
    /// <summary>
    ///     Gets the logical width of <see cref="FrameInfo"/>.
    /// </summary>
    int Width { get; }

    /// <summary>
    ///     Gets the logical height of <see cref="FrameInfo"/>, excluding collapsed physical scanline repeats.
    /// </summary>
    int Height { get; }

    /// <summary>
    ///     Gets the tightly packed logical framebuffer size in pixels.
    /// </summary>
    int BufferSize { get; }

    /// <summary>
    ///     Copy the latest completed frame to the provided buffer.
    /// </summary>
    /// <param name="buffer">The framebuffer used by the VGA card to draw the image on screen.</param>
    void Render(Span<uint> buffer);

    /// <summary>Copies a pending frame to a pitched presentation buffer only when its dimensions match.</summary>
    /// <param name="buffer">Destination pixel storage.</param>
    /// <param name="width">Destination width in pixels.</param>
    /// <param name="height">Destination height in pixels.</param>
    /// <param name="rowStride">Destination row stride in pixels, including padding.</param>
    /// <exception cref="ArgumentException">The matching destination has an insufficient stride or buffer length.</exception>
    void Render(Span<uint> buffer, int width, int height, int rowStride);

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