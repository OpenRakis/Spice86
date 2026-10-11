namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>Logical framebuffer dimensions and vertical presentation scale for one completed frame.</summary>
/// <param name="Width">Width in logical pixels.</param>
/// <param name="Height">Height in logical pixels.</param>
/// <param name="AspectRatioCorrectionFactor">Vertical scale used by the presentation layer.</param>
public sealed record VideoFrameInfo(int Width, int Height, double AspectRatioCorrectionFactor);
