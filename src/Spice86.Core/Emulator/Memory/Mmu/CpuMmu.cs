namespace Spice86.Core.Emulator.Memory.Mmu;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Resolves segmented memory accesses to the real-mode or descriptor-cache-based MMU. CS is always
/// resolved through its descriptor cache, regardless of the live <see cref="CpuMode"/>: real hardware
/// keeps fetching through the CS cache across a CR0.PE transition until the mandatory far jump reloads
/// it, so dispatching CS by the live mode would pick the wrong translation at the exact instant
/// CR0.PE changes. Every other segment register dispatches by <see cref="CpuMode"/> as usual, because
/// plenty of code outside instruction execution (BIOS/VGA/DOS setup) writes ES/DS/etc. directly
/// without ever populating their descriptor cache, and real mode never needs one (base is always
/// selector * 16).
/// </summary>
public sealed class CpuMmu : IMmu {
    private readonly State _state;
    private readonly IMmu _realModeMmu;
    private readonly IMmu _cachedSegmentMmu;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The CPU state, used to read the current CS value and <see cref="CpuMode"/>.</param>
    /// <param name="realModeMmu">The MMU used for non-CS accesses while not in protected mode.</param>
    /// <param name="cachedSegmentMmu">The descriptor-cache-based MMU used for CS and for protected-mode accesses.</param>
    public CpuMmu(State state, IMmu realModeMmu, IMmu cachedSegmentMmu) {
        _state = state;
        _realModeMmu = realModeMmu;
        _cachedSegmentMmu = cachedSegmentMmu;
    }

    private IMmu Resolve(ushort segment) {
        if (segment == _state.CS) {
            return _cachedSegmentMmu;
        }
        return _state.CpuMode == CpuMode.Protected ? _cachedSegmentMmu : _realModeMmu;
    }

    /// <inheritdoc />
    public void CheckAccess(ushort segment, uint offset, uint length, SegmentAccessKind accessKind, bool isWrite) {
        Resolve(segment).CheckAccess(segment, offset, length, accessKind, isWrite);
    }

    /// <inheritdoc />
    public uint TranslateAddress(ushort segment, uint offset, bool isWrite) {
        return Resolve(segment).TranslateAddress(segment, offset, isWrite);
    }

    /// <summary>
    /// A no-op: paging is applied by the outer <see cref="PagingMmu"/> that wraps this MMU, not here.
    /// </summary>
    public uint TranslateLinearAddress(uint linearAddress, bool isWrite) {
        return linearAddress;
    }
}
