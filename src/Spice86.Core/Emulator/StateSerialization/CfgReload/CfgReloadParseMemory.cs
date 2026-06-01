namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;

/// <summary>
/// Byte-backed <see cref="Spice86.Core.Emulator.Memory.Indexable.IIndexable"/> used only to re-parse
/// stored CFG nodes during reload. Backed by a flat physical-address buffer that covers the whole
/// real-mode address space reachable by a 16-bit segmented address (<see cref="A20Gate.EndOfHighMemoryArea"/>),
/// so any node address plus its bytes fits.
///
/// The buffer is written per-node immediately before parsing that node (see <c>CfgNodeReconstructor</c>):
/// multiple variant instructions and a selector can share one address with different byte images, so a
/// single shared address-to-byte map would make them collide. Writing the current node's bytes, parsing,
/// then moving on avoids the collision.
/// </summary>
internal sealed class CfgReloadParseMemory : ByteArrayBasedIndexable {
    // A small margin past the top of high memory so multi-byte field reads at the very top never go
    // out of bounds.
    private const int TrailingReadMargin = 16;
    private const int BufferSize = (int)A20Gate.EndOfHighMemoryArea + TrailingReadMargin;

    public CfgReloadParseMemory() : base(new byte[BufferSize]) {
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> at <paramref name="physicalAddress"/>. A <c>null</c> entry is a
    /// modified-immediate placeholder and is written as <c>0</c>: the value is irrelevant for length /
    /// structure decoding (nullable bytes are always immediates / displacements), and the field is
    /// nullified after parsing.
    /// </summary>
    public void WriteNodeBytes(uint physicalAddress, IReadOnlyList<byte?> bytes) {
        byte[] array = Array;
        for (int i = 0; i < bytes.Count; i++) {
            array[physicalAddress + i] = bytes[i] ?? 0;
        }
    }
}
