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
    /// Gets the time it took to render the last frame.
    /// </summary>
    TimeSpan LastFrameRenderTime { get; }

    /// <summary>
    /// Gets or sets whether the rendering can start.
    /// This should be set to true when the emulator starts running to prevent
    /// race conditions between the UI rendering thread and the emulator thread.
    /// </summary>
    bool CanRenderingStart { get; set; }

    /// <summary>
    ///     Render the current video memory to a buffer.
    /// </summary>
    /// <param name="buffer">The framebuffer used by the VGA card to draw the image on screen.</param>
    void Render(Span<uint> buffer);
}