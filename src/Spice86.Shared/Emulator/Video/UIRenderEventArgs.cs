namespace Spice86.Shared.Emulator.Video;

/// <summary>
/// Event Args that pass the WriteableBitmap buffer pointer to the video card for rendering.
/// </summary>
/// <param name="Address">The pointer to the start of the video buffer</param>
/// <param name="Length">The length of the video buffer, in bytes</param>
public readonly record struct UIRenderEventArgs(IntPtr Address, int Length);
