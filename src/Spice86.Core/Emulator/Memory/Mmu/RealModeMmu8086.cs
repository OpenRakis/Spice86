namespace Spice86.Core.Emulator.Memory.Mmu;

using Spice86.Shared.Utils;

/// <summary>
/// Real-mode MMU for 8086-class CPUs. Accesses wrap within the 64KB segment without faulting.
/// Real-mode only; a future protected-mode MMU will extend this design.
/// </summary>
public sealed class RealModeMmu8086 : IMmu {
    /// <inheritdoc />
    public void CheckAccess(ushort segment, uint offset, uint length, SegmentAccessKind accessKind, bool isWrite) {
        // 8086 wraps within segment — all accesses are valid.
    }

    /// <inheritdoc />
    public uint TranslateAddress(ushort segment, uint offset, bool isWrite) {
        return MemoryUtils.ToPhysicalAddress(segment, (ushort)offset);
    }

    /// <summary>The 8086 has no paging concept: returns <paramref name="linearAddress"/> unchanged.</summary>
    public uint TranslateLinearAddress(uint linearAddress, bool isWrite) {
        return linearAddress;
    }
}
