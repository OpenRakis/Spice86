namespace Spice86.Core.Emulator.Memory.Mmu;

using Spice86.Shared.Utils;

/// <summary>
/// Real-mode MMU for 8086-class CPUs. Accesses wrap within the 64KB segment without faulting.
/// Real-mode only; a future protected-mode MMU will extend this design.
/// </summary>
public sealed class RealModeMmu8086 : IMmu {
    /// <inheritdoc />
    public void CheckAccess(ushort segment, uint offset, uint length, SegmentAccessKind accessKind) {
        // 8086 wraps within segment — all accesses are valid.
    }

    /// <inheritdoc />
    public uint TranslateAddress(ushort segment, uint offset) {
        return MemoryUtils.ToPhysicalAddress(segment, (ushort)offset);
    }
}
