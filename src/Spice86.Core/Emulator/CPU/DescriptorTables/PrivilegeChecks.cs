namespace Spice86.Core.Emulator.CPU.DescriptorTables;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// Privilege-level validation shared by both execution paths (<c>InstructionExecutionHelper</c> and
/// <c>CSharpOverrideHelper</c>): IOPL gating for I/O and flag-control instructions, and DPL/RPL checks
/// for data/stack segment loads. Code-segment (CS) transfer privilege rules (call gates, conforming
/// vs non-conforming checks) are validated separately, alongside gate dispatch.
/// </summary>
public static class PrivilegeChecks {
    /// <summary>
    /// Throws #GP if the current privilege level is not allowed to execute `IN`/`OUT`/`CLI`/`STI`:
    /// outside Virtual-8086 mode, CPL must be &lt;= IOPL; inside it, IOPL must be exactly 3.
    /// </summary>
    public static void EnsureIoPrivilege(State state) {
        if (state.CpuMode == CpuMode.Real) {
            return;
        }
        bool isVirtual8086 = state.CpuMode == CpuMode.Virtual8086;
        byte iopl = state.IoPrivilegeLevel;
        bool violatesIoPrivilege = isVirtual8086 ? iopl < 3 : iopl < state.Cpl;
        if (violatesIoPrivilege) {
            throw new CpuGeneralProtectionFaultException(
                $"IOPL check failed: CPL={state.Cpl}, IOPL={iopl}, VM={isVirtual8086}");
        }
    }

    /// <summary>
    /// Throws #GP if executed anywhere but CPL 0 (e.g. `HLT`, `LGDT`/`LIDT`, `MOV CRn`). A no-op in real
    /// mode, where CPL is always effectively 0.
    /// </summary>
    public static void EnsureCpl0(State state, string instructionName) {
        if (state.CpuMode is CpuMode.Protected or CpuMode.Virtual8086 && state.Cpl != 0) {
            throw new CpuGeneralProtectionFaultException($"{instructionName} requires CPL 0, current CPL is {state.Cpl}");
        }
    }

    private static readonly SegmentRegisterIndex[] DataSegmentIndices = [
        SegmentRegisterIndex.DsIndex, SegmentRegisterIndex.EsIndex,
        SegmentRegisterIndex.FsIndex, SegmentRegisterIndex.GsIndex
    ];

    /// <summary>
    /// After a privilege-decreasing return (IRET/RETF raising CPL), real hardware automatically loads
    /// the null selector into any of DS/ES/FS/GS whose current segment is no longer accessible at the
    /// new, less-privileged CPL: a data segment (or non-conforming code segment) whose descriptor DPL is
    /// less than the new CPL. This must be called AFTER the new CS/CPL is already in effect. A no-op
    /// outside protected mode.
    /// </summary>
    public static void NullifyInaccessibleDataSegments(State state) {
        if (state.CpuMode != CpuMode.Protected) {
            return;
        }
        byte newCpl = state.Cpl;
        foreach (SegmentRegisterIndex index in DataSegmentIndices) {
            SegmentDescriptorCache cache = state.SegmentDescriptorCaches[index];
            bool conformingCode = cache.IsCode && cache.IsConforming;
            if (!conformingCode && cache.DescriptorPrivilegeLevel < newCpl) {
                state.SegmentRegisters.UInt16[(uint)index] = 0;
                state.SegmentDescriptorCaches[index] = default;
            }
        }
    }

    /// <summary>
    /// Validates a data or stack segment load against DPL/RPL rules once its descriptor has been
    /// decoded. A no-op outside protected mode or for CS (validated separately).
    /// </summary>
    public static void ValidateDataSegmentLoad(State state, SegmentRegisterIndex index, ushort selector, SegmentDescriptorCache descriptor) {
        if (state.CpuMode != CpuMode.Protected || index == SegmentRegisterIndex.CsIndex) {
            return;
        }

        byte cpl = state.Cpl;
        byte rpl = new SegmentSelector(selector).RequestedPrivilegeLevel;

        if (index == SegmentRegisterIndex.SsIndex) {
            ValidateStackSegmentLoad(selector, descriptor, cpl, rpl);
            return;
        }

        if (!descriptor.Present) {
            throw new CpuSegmentNotPresentException($"Selector 0x{selector:X4} is not present", new SegmentSelector(selector).ErrorCode);
        }
        bool isDataOrReadableCode = descriptor.IsCodeOrDataSegment && (!descriptor.IsCode || descriptor.IsReadWriteBitSet);
        if (!isDataOrReadableCode) {
            throw new CpuGeneralProtectionFaultException(
                $"Selector 0x{selector:X4} is not a data segment or readable code segment", new SegmentSelector(selector).ErrorCode);
        }
        if (!descriptor.IsConforming && Math.Max(rpl, cpl) > descriptor.DescriptorPrivilegeLevel) {
            throw new CpuGeneralProtectionFaultException(
                $"Selector 0x{selector:X4}: max(RPL={rpl}, CPL={cpl}) exceeds DPL={descriptor.DescriptorPrivilegeLevel}", new SegmentSelector(selector).ErrorCode);
        }
    }

    private static void ValidateStackSegmentLoad(ushort selector, SegmentDescriptorCache descriptor, byte cpl, byte rpl) {
        if (!descriptor.Present) {
            throw new CpuStackSegmentFaultException($"Selector 0x{selector:X4} is not present", new SegmentSelector(selector).ErrorCode);
        }
        bool isWritableData = descriptor.IsCodeOrDataSegment && !descriptor.IsCode && descriptor.IsReadWriteBitSet;
        if (!isWritableData) {
            throw new CpuGeneralProtectionFaultException($"Selector 0x{selector:X4} is not a writable data segment", new SegmentSelector(selector).ErrorCode);
        }
        if (rpl != cpl || descriptor.DescriptorPrivilegeLevel != cpl) {
            throw new CpuGeneralProtectionFaultException(
                $"Selector 0x{selector:X4}: RPL={rpl} and DPL={descriptor.DescriptorPrivilegeLevel} must both equal CPL={cpl}", new SegmentSelector(selector).ErrorCode);
        }
    }

    /// <summary>
    /// Validates a direct (non-gate) far JMP/CALL code-segment transfer: present, actually a code
    /// segment (a selector resolving to a system/gate descriptor means call-gate dispatch is needed,
    /// which is not yet implemented), and DPL/RPL rules (conforming segments require DPL &lt;= CPL;
    /// non-conforming segments require DPL == CPL and RPL &lt;= CPL). CPL never changes for a direct
    /// transfer. A no-op outside protected mode.
    /// </summary>
    public static void ValidateFarCodeSegmentTransfer(State state, IMemory memory, ushort selector) {
        if (state.CpuMode != CpuMode.Protected) {
            return;
        }
        if (!DescriptorTableReader.TryReadDescriptor(selector, state.Gdtr.Base, state.Gdtr.Limit,
                state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)], out SegmentDescriptorCache descriptor)) {
            throw new CpuGeneralProtectionFaultException($"Selector 0x{selector:X4} is outside its descriptor table limit", new SegmentSelector(selector).ErrorCode);
        }
        if (!descriptor.Present) {
            throw new CpuSegmentNotPresentException($"Selector 0x{selector:X4} is not present", new SegmentSelector(selector).ErrorCode);
        }
        if (!descriptor.IsCodeOrDataSegment) {
            throw new UnhandledOperationException(state,
                $"Selector 0x{selector:X4} is a system descriptor (call/task/interrupt/trap gate); gate dispatch via direct far JMP/CALL is not yet supported");
        }
        if (!descriptor.IsCode) {
            throw new CpuGeneralProtectionFaultException($"Selector 0x{selector:X4} is not a code segment", new SegmentSelector(selector).ErrorCode);
        }
        byte cpl = state.Cpl;
        byte rpl = new SegmentSelector(selector).RequestedPrivilegeLevel;
        byte dpl = descriptor.DescriptorPrivilegeLevel;
        if (descriptor.IsConforming) {
            if (dpl > cpl) {
                throw new CpuGeneralProtectionFaultException($"Selector 0x{selector:X4}: conforming DPL={dpl} > CPL={cpl}", new SegmentSelector(selector).ErrorCode);
            }
        } else if (dpl != cpl || rpl > cpl) {
            throw new CpuGeneralProtectionFaultException(
                $"Selector 0x{selector:X4}: non-conforming DPL={dpl} must equal CPL={cpl} and RPL={rpl} must be <= CPL", new SegmentSelector(selector).ErrorCode);
        }
    }
}
