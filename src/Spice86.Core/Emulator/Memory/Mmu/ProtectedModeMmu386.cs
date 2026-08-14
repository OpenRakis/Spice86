namespace Spice86.Core.Emulator.Memory.Mmu;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.DescriptorTables;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// Protected-mode MMU for 386-class CPUs. Translates and validates segmented accesses using the
/// descriptor cache already loaded into the segment register the caller is using, matching real
/// hardware (which caches base/limit/access-rights at segment-load time rather than re-reading the
/// GDT/LDT on every access).
/// </summary>
/// <remarks>
/// <see cref="IMmu"/> callers only pass the raw selector currently held by a segment register, not
/// which register it came from. This MMU recovers the register identity by matching the raw value
/// against the CPU's live segment register contents and uses that register's cache; if two registers
/// coincidentally hold the same selector, using either cache is harmless because both were decoded
/// from the same descriptor unless it was edited between the two loads (an edge case out of scope
/// here). If no register currently holds a matching selector — e.g. a stale/literal value used right
/// after CR0.PE flips before the mandatory far jump reloads CS — the descriptor is decoded directly
/// from the live GDT/LDT as a fallback.
/// </remarks>
public sealed class ProtectedModeMmu386 : IMmu {
    private static readonly SegmentRegisterIndex[] AllSegmentRegisterIndices = [
        SegmentRegisterIndex.EsIndex, SegmentRegisterIndex.CsIndex, SegmentRegisterIndex.SsIndex,
        SegmentRegisterIndex.DsIndex, SegmentRegisterIndex.FsIndex, SegmentRegisterIndex.GsIndex
    ];

    private readonly State _state;
    private readonly IMemoryDevice _ram;
    private readonly PagingUnit _pagingUnit;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The CPU state, used to read segment registers, their descriptor caches, and the GDTR/LDTR.</param>
    /// <param name="ram">The raw memory device backing linear/physical address reads (no MMU translation applied).</param>
    /// <param name="pagingUnit">The page-directory/page-table walker, shared with the outer <see cref="PagingMmu"/> so both agree on Accessed/Dirty-bit state.</param>
    public ProtectedModeMmu386(State state, IMemoryDevice ram, PagingUnit pagingUnit) {
        _state = state;
        _ram = ram;
        _pagingUnit = pagingUnit;
    }

    /// <inheritdoc />
    public void CheckAccess(ushort segment, uint offset, uint length, SegmentAccessKind accessKind, bool isWrite) {
        SegmentDescriptorCache descriptorCache = ResolveDescriptorCache(segment);
        if (!descriptorCache.Present) {
            throw new CpuGeneralProtectionFaultException($"Segment 0x{segment:X4} is not present");
        }
        if (isWrite && descriptorCache.IsCodeOrDataSegment && !descriptorCache.IsCode && !descriptorCache.IsReadWriteBitSet) {
            throw new CpuGeneralProtectionFaultException($"Segment 0x{segment:X4} is not writable");
        }
        if (offset <= descriptorCache.Limit && length - 1u <= descriptorCache.Limit - offset) {
            return;
        }

        string message = $"Segment access 0x{offset:X8}+{length}B exceeds segment limit 0x{descriptorCache.Limit:X8}";
        if (accessKind == SegmentAccessKind.Stack) {
            throw new CpuStackSegmentFaultException(message);
        }
        throw new CpuGeneralProtectionFaultException(message);
    }

    /// <inheritdoc />
    public uint TranslateAddress(ushort segment, uint offset, bool isWrite) {
        SegmentDescriptorCache descriptorCache = ResolveDescriptorCache(segment);
        return descriptorCache.Base + offset;
    }

    /// <summary>
    /// A no-op: paging is applied by the outer <see cref="PagingMmu"/> that wraps this MMU, not here.
    /// </summary>
    public uint TranslateLinearAddress(uint linearAddress, bool isWrite) {
        return linearAddress;
    }

    private SegmentDescriptorCache ResolveDescriptorCache(ushort segment) {
        foreach (SegmentRegisterIndex index in AllSegmentRegisterIndices) {
            if (_state.SegmentRegisters.UInt16[(uint)index] == segment) {
                return _state.SegmentDescriptorCaches[index];
            }
        }
        return DescriptorTableReader.ReadDescriptor(segment,
            _state.Gdtr.Base, _state.Gdtr.Limit,
            _state.Ldtr.DescriptorCache.Base, _state.Ldtr.DescriptorCache.Limit,
            address => _ram.Read(TranslateLinearForFallback(address)));
    }

    private uint TranslateLinearForFallback(uint linearAddress) {
        return _state.ControlRegisters.PagingEnable ? _pagingUnit.Translate(linearAddress, isWrite: false) : linearAddress;
    }
}
