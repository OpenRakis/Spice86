namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using Spice86.Shared.Emulator.Memory;

using System.Runtime.InteropServices;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// In-memory structure with information about an XMS move request.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XmsMoveData {
    /// <summary>
    /// Number of bytes to move.
    /// </summary>
    public uint Length;

    /// <summary>
    /// Handle of source block; zero if moving from segment:offset pair.
    /// </summary>
    public ushort SourceHandle;

    /// <summary>
    /// Source offset as a 32-bit value or a segment:offset pair.
    /// </summary>
    public uint SourceOffset;

    /// <summary>
    /// Handle of destination block; zero if moving to segment:offset pair.
    /// </summary>
    public ushort DestHandle;

    /// <summary>
    /// Destination offset as a 32-bit value or a segment:offset pair.
    /// </summary>
    public uint DestOffset;

    /// <summary>
    /// Gets the source address as a segment:offset value.
    /// </summary>
    public SegmentedAddress SourceAddress => new((ushort)(SourceOffset >> 16), (ushort)SourceOffset);

    /// <summary>
    /// Gets the destination address as a segment:offset value.
    /// </summary>
    public SegmentedAddress DestAddress => new((ushort)(DestOffset >> 16), (ushort)DestOffset);
}
