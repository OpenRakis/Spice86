namespace Spice86.Aeon.Emulator.Video.Rendering;

using Spice86.Aeon.Emulator.Video.Modes;

/// <summary>
/// Renders 8-bit graphics to a bitmap.
/// </summary>
public sealed class GraphicsPresenter8 : Presenter
{
    /// <summary>
    /// Initializes a new instance of the GraphicsPresenter8 class.
    /// </summary>
    /// <param name="videoMode">VideoMode instance describing the video mode.</param>
    public GraphicsPresenter8(VideoMode videoMode) : base(videoMode)
    {
    }

    /// <summary>
    /// Updates the bitmap to match the current state of the video RAM.
    /// </summary>
    protected override void DrawFrame(nint destination)
    {
        uint totalPixels = (uint)VideoMode.Width * (uint)VideoMode.Height;
        ReadOnlySpan<uint> palette = VideoMode.Palette;

        unsafe
        {
            byte* srcPtr = (byte*)VideoMode.VideoRam.ToPointer() + (uint)VideoMode.StartOffset;
            uint* destPtr = (uint*)destination.ToPointer();

            for (int i = 0; i < totalPixels; i++) {
                destPtr[i] = 0xFF000000 | palette[srcPtr[i]];
            }
        }
    }
}