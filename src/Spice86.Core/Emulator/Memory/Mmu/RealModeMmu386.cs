namespace Spice86.Core.Emulator.Memory.Mmu;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Utils;

/// <summary>
/// Real-mode MMU for 386-class CPUs. Validates that accesses stay within the 64KB segment limit
/// and faults with #GP or #SS if they do not.
/// Real-mode only; a future protected-mode MMU will extend this design.
/// </summary>
public sealed class RealModeMmu386 : IMmu {
    private const uint SegmentLimit = 0xFFFFu;

    /// <inheritdoc />
    public void CheckAccess(ushort segment, uint offset, uint length, SegmentAccessKind accessKind) {
        if (IsValidAccess(offset, length)) {
            return;
        }

        string message = $"Segment access 0x{offset:X8}+{length}B exceeds real-mode segment limit 0xFFFF";
        if (accessKind == SegmentAccessKind.Stack) {
            throw new CpuStackSegmentFaultException(message);
        }
        throw new CpuGeneralProtectionFaultException(message);
    }

    private static bool IsValidAccess(uint offset, uint length) {
        return offset <= SegmentLimit && length - 1u <= SegmentLimit - offset;
    }

    /// <inheritdoc />
    public uint TranslateAddress(ushort segment, uint offset) {
        return MemoryUtils.ToPhysicalAddress(segment, (ushort)offset);
    }
}
