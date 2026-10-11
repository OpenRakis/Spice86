namespace Spice86.Shared.Emulator.Video;

/// <summary>
/// Event Args that pass the WriteableBitmap buffer pointer to the video card for rendering.
/// </summary>
/// <param name="Address">The pointer to the start of the video buffer</param>
/// <param name="Length">The length of the video buffer, in pixels</param>
/// <param name="Width">Logical framebuffer width.</param>
/// <param name="Height">Logical framebuffer height.</param>
/// <param name="RowStride">Destination row stride in pixels, including padding.</param>
public readonly record struct UIRenderEventArgs(IntPtr Address, int Length, int Width, int Height, int RowStride);
