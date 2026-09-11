namespace Spice86.Core.Emulator.Memory.Mmu;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Adds a paging translation stage after segment translation: when
/// <see cref="Registers.ControlRegisters.PagingEnable"/> is set, the linear address produced by the
/// wrapped MMU's <see cref="IMmu.TranslateAddress"/> is further translated to a physical address via
/// <see cref="PagingUnit"/>. Segment-level limit checks (<see cref="IMmu.CheckAccess"/>) are delegated
/// unchanged, since paging does not affect segment limits.
/// </summary>
public sealed class PagingMmu : IMmu {
    private readonly State _state;
    private readonly IMmu _inner;
    private readonly PagingUnit _pagingUnit;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The CPU state, used to read <see cref="Registers.ControlRegisters.PagingEnable"/>.</param>
    /// <param name="inner">The segment-translation MMU whose output is treated as the linear address.</param>
    /// <param name="pagingUnit">The page-directory/page-table walker used once paging is enabled.</param>
    public PagingMmu(State state, IMmu inner, PagingUnit pagingUnit) {
        _state = state;
        _inner = inner;
        _pagingUnit = pagingUnit;
    }

    /// <inheritdoc />
    public void CheckAccess(ushort segment, uint offset, uint length, SegmentAccessKind accessKind, bool isWrite) {
        _inner.CheckAccess(segment, offset, length, accessKind, isWrite);
    }

    /// <inheritdoc />
    public uint TranslateAddress(ushort segment, uint offset, bool isWrite) {
        uint linearAddress = _inner.TranslateAddress(segment, offset, isWrite);
        return _state.ControlRegisters.PagingEnable ? _pagingUnit.Translate(linearAddress, isWrite) : linearAddress;
    }

    /// <inheritdoc />
    public uint TranslateLinearAddress(uint linearAddress, bool isWrite) {
        return _state.ControlRegisters.PagingEnable ? _pagingUnit.Translate(linearAddress, isWrite) : linearAddress;
    }
}
