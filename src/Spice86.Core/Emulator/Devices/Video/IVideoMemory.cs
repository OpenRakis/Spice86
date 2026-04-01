namespace Spice86.Core.Emulator.Devices.Video;

using System;

using Spice86.Core.Emulator.Memory;

/// <summary>
///     Represents the video memory interface for managing video memory.
/// </summary>
public interface IVideoMemory : IMemoryDevice {
    /// <summary>
    ///     Raw interleaved VRAM buffer. Plane p at VGA address a = VRam[a * 4 + p].
    ///     Mode 13h chain-4 pixel N = VRam[N].
    /// </summary>
    byte[] VRam { get; }

    /// <summary>
    ///     Accessor that preserves the <c>Planes[plane, address]</c> syntax while forwarding
    ///     to the interleaved <see cref="VRam"/> buffer.
    /// </summary>
    PlaneAccessor Planes { get; }

    /// <summary>
    ///     Returns a read-only span over the interleaved VRAM starting at the given linear byte offset.
    /// </summary>
    /// <param name="linearByteOffset">The byte offset into VRam.</param>
    /// <param name="length">The number of bytes to return.</param>
    ReadOnlySpan<byte> GetLinearSpan(int linearByteOffset, int length);
}