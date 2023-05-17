namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// Represents the video memory interface for managing video memory.
/// </summary>
public interface IVideoMemory : IMemoryDevice {
    /// <summary>
    /// Provides access to the 4 planes of video memory.
    /// </summary>
    byte[,] Planes { get; }
}