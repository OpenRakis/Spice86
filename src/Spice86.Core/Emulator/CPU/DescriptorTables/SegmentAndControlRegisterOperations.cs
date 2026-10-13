namespace Spice86.Core.Emulator.CPU.DescriptorTables;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// Shared segment-load, GDTR/IDTR, and control-register operations used by both interpreted execution
/// (<c>InstructionExecutionHelper</c>) and generated/hand-written C# overrides
/// (<c>CSharpOverrideHelper</c>), so the two execution paths behave identically.
/// </summary>
public static class SegmentAndControlRegisterOperations {
    /// <summary>
    /// Loads a raw selector value into a segment register and refreshes its descriptor cache: the
    /// real-mode synthesized cache (base = selector*16) outside protected mode, or the decoded GDT/LDT
    /// descriptor once <see cref="CpuMode.Protected"/> is active. This is the single path every
    /// segment-register write (MOV Sreg, POP Sreg, far transfers) goes through. A null selector is
    /// allowed for DS/ES/FS/GS (real hardware only faults when it is later used), but not for SS.
    /// DPL/RPL privilege rules are validated for every register except CS (validated separately
    /// alongside code-segment/gate transfer rules).
    /// </summary>
    public static void LoadSegmentRegister(State state, IMemory memory, uint segmentRegisterIndex, ushort selector) {
        SegmentRegisterIndex index = (SegmentRegisterIndex)segmentRegisterIndex;
        SegmentDescriptorCache descriptorCache;
        if (state.CpuMode != CpuMode.Protected) {
            descriptorCache = SegmentDescriptorCache.CreateRealMode(selector);
        } else if (new SegmentSelector(selector).IsNull) {
            if (index == SegmentRegisterIndex.SsIndex) {
                throw new CpuGeneralProtectionFaultException("Cannot load a null selector into SS");
            }
            descriptorCache = default;
        } else if (!DescriptorTableReader.TryReadDescriptor(selector, state.Gdtr.Base, state.Gdtr.Limit,
                state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)], out descriptorCache)) {
            throw new CpuGeneralProtectionFaultException($"Selector 0x{selector:X4} is outside its descriptor table limit", new SegmentSelector(selector).ErrorCode);
        } else {
            if (Environment.GetEnvironmentVariable("SPICE86_TRACE_EXC") is not null) {
                System.IO.Directory.CreateDirectory("tmp");
                SegmentSelector ss = new(selector);
                uint tblBase = ss.ReferencesLocalDescriptorTable ? state.Ldtr.DescriptorCache.Base : state.Gdtr.Base;
                uint entryOff = (uint)ss.Index * 8u;
                byte[] raw = new byte[8];
                for (int i = 0; i < 8; i++) { raw[i] = memory[tblBase + entryOff + (uint)i]; }
                System.IO.File.AppendAllText("tmp/seg_trace.txt",
                    $"reg={index} sel=0x{selector:X4} TI={ss.ReferencesLocalDescriptorTable} idx={ss.Index} tblBase=0x{tblBase:X} descBase=0x{descriptorCache.Base:X} LdtrBase=0x{state.Ldtr.DescriptorCache.Base:X} LdtrLimit=0x{state.Ldtr.DescriptorCache.Limit:X} raw={Convert.ToHexString(raw)} present={descriptorCache.Present} PG={state.ControlRegisters.PagingEnable} CR3=0x{state.ControlRegisters.Cr3:X}\n");
            }
            PrivilegeChecks.ValidateDataSegmentLoad(state, index, selector, descriptorCache);
        }
        state.SegmentRegisters.UInt16[segmentRegisterIndex] = selector;
        state.SegmentDescriptorCaches[index] = descriptorCache;
    }

    /// <summary>LGDT: loads GDTR from a 6-byte memory pointer (2-byte limit, 4-byte base).</summary>
    public static void LoadGdtr(State state, IMemory memory, ushort segment, uint offset) {
        state.Gdtr.Limit = memory.UInt16[segment, offset];
        state.Gdtr.Base = memory.UInt32[segment, offset + 2];
    }

    /// <summary>SGDT: stores GDTR to a 6-byte memory pointer (2-byte limit, 4-byte base).</summary>
    public static void StoreGdtr(State state, IMemory memory, ushort segment, uint offset) {
        memory.UInt16[segment, offset] = state.Gdtr.Limit;
        memory.UInt32[segment, offset + 2] = state.Gdtr.Base;
    }

    /// <summary>LIDT: loads IDTR from a 6-byte memory pointer (2-byte limit, 4-byte base).</summary>
    public static void LoadIdtr(State state, IMemory memory, ushort segment, uint offset) {
        state.Idtr.Limit = memory.UInt16[segment, offset];
        state.Idtr.Base = memory.UInt32[segment, offset + 2];
    }

    /// <summary>SIDT: stores IDTR to a 6-byte memory pointer (2-byte limit, 4-byte base).</summary>
    public static void StoreIdtr(State state, IMemory memory, ushort segment, uint offset) {
        memory.UInt16[segment, offset] = state.Idtr.Limit;
        memory.UInt32[segment, offset + 2] = state.Idtr.Base;
    }

    /// <summary>MOV r32, CRn: reads CR0/CR2/CR3/CR4.</summary>
    public static uint ReadControlRegister(State state, uint crNumber) {
        return crNumber switch {
            0 => state.ControlRegisters.Cr0,
            2 => state.ControlRegisters.Cr2,
            3 => state.ControlRegisters.Cr3,
            4 => state.ControlRegisters.Cr4,
            _ => throw new CpuInvalidOpcodeException($"MOV from CR{crNumber} is not supported")
        };
    }

    /// <summary>MOV CRn, r32: writes CR0/CR2/CR3/CR4.</summary>
    public static void WriteControlRegister(State state, uint crNumber, uint value) {
        switch (crNumber) {
            case 0: WriteCr0(state, value); break;
            case 2: state.ControlRegisters.Cr2 = value; break;
            case 3: state.ControlRegisters.Cr3 = value; break;
            case 4: state.ControlRegisters.Cr4 = value; break;
            default: throw new CpuInvalidOpcodeException($"MOV to CR{crNumber} is not supported");
        }
    }

    /// <summary>SMSW: reads the low 16 bits of CR0 (the legacy 80286 "machine status word").</summary>
    public static ushort ReadMachineStatusWord(State state) {
        return (ushort)state.ControlRegisters.Cr0;
    }

    /// <summary>
    /// LMSW: writes the low 4 bits of CR0 (PE, MP, EM, TS) from a 16-bit machine status word. Matches
    /// real hardware: bits above 3 are ignored, and PE can only be set, never cleared, by LMSW.
    /// </summary>
    public static void LoadMachineStatusWord(State state, ushort value) {
        uint newLowBits = (uint)(value & 0xF) | (state.ControlRegisters.ProtectionEnable ? 1u : 0u);
        uint newCr0 = (state.ControlRegisters.Cr0 & ~0xFu) | newLowBits;
        WriteCr0(state, newCr0);
    }

    /// <summary>CLTS: clears CR0.TS (Task Switched), set by the CPU on every task switch.</summary>
    public static void Clts(State state) {
        state.ControlRegisters.TaskSwitched = false;
    }

    private static void WriteCr0(State state, uint value) {
        bool enteringProtectedMode = !state.ControlRegisters.ProtectionEnable && (value & 1) != 0;
        state.ControlRegisters.Cr0 = value;
        if (enteringProtectedMode) {
            RefreshDescriptorCachesForRealModeTransition(state);
        }
    }

    /// <summary>
    /// LLDT: loads LDTR from a GDT selector and caches its descriptor. A null selector is allowed (it
    /// means "no LDT is loaded"), matching real hardware and <see cref="LoadSegmentRegister"/>'s
    /// null-selector handling for DS/ES/FS/GS.
    /// </summary>
    public static void LoadLdtr(State state, IMemory memory, ushort selector) {
        state.Ldtr.Selector = selector;
        state.Ldtr.DescriptorCache = new SegmentSelector(selector).IsNull
            ? default
            : DescriptorTableReader.ReadDescriptor(selector, state.Gdtr.Base, state.Gdtr.Limit,
                state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)]);
    }

    /// <summary>SLDT: reads the current LDTR selector.</summary>
    public static ushort StoreLdtr(State state) {
        return state.Ldtr.Selector;
    }

    /// <summary>LTR: loads the Task Register from a GDT selector and caches its (TSS) descriptor.</summary>
    public static void LoadTr(State state, IMemory memory, ushort selector) {
        state.Tr.Selector = selector;
        state.Tr.DescriptorCache = DescriptorTableReader.ReadDescriptor(selector, state.Gdtr.Base, state.Gdtr.Limit,
            state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)]);
    }

    /// <summary>STR: reads the current Task Register selector.</summary>
    public static ushort StoreTr(State state) {
        return state.Tr.Selector;
    }

    /// <summary>
    /// ARPL: returns the r/m operand with its RPL raised to the register operand's RPL if it was lower.
    /// </summary>
    public static ushort AdjustRequestedPrivilegeLevel(ushort rmSelector, ushort regSelector) {
        byte rmRpl = new SegmentSelector(rmSelector).RequestedPrivilegeLevel;
        byte regRpl = new SegmentSelector(regSelector).RequestedPrivilegeLevel;
        return rmRpl < regRpl ? (ushort)((rmSelector & ~0b11) | regRpl) : rmSelector;
    }

    /// <summary>ARPL: whether the r/m operand's RPL was raised (sets ZF).</summary>
    public static bool WasPrivilegeLevelAdjusted(ushort rmSelector, ushort regSelector) {
        return new SegmentSelector(rmSelector).RequestedPrivilegeLevel < new SegmentSelector(regSelector).RequestedPrivilegeLevel;
    }

    /// <summary>LAR: whether <paramref name="selector"/> resolves to a present descriptor (sets ZF).</summary>
    public static bool IsSelectorValidForLar(State state, IMemory memory, ushort selector) {
        return TryReadDescriptorForVerification(state, memory, selector, out SegmentDescriptorCache descriptor)
            && descriptor.Present;
    }

    /// <summary>
    /// LAR: loads the packed access-rights doubleword for <paramref name="selector"/> (only meaningful
    /// when <see cref="IsSelectorValidForLar"/> is true; the destination is left unchanged otherwise).
    /// Bits 8-15 are the raw access byte (type/S/DPL/P); bits 20-23 are AVL/reserved/D-B/G.
    /// </summary>
    public static uint LoadAccessRights(State state, IMemory memory, ushort selector) {
        TryReadDescriptorForVerification(state, memory, selector, out SegmentDescriptorCache descriptor);
        uint flagsNibble = (descriptor.Granularity4K ? 0x8u : 0) | (descriptor.DefaultBig ? 0x4u : 0);
        return ((uint)descriptor.AccessRights << 8) | (flagsNibble << 20);
    }

    /// <summary>LSL: whether <paramref name="selector"/> resolves to a present segment descriptor (sets ZF).</summary>
    public static bool IsSelectorValidForLsl(State state, IMemory memory, ushort selector) {
        return TryReadDescriptorForVerification(state, memory, selector, out SegmentDescriptorCache descriptor)
            && descriptor.Present && descriptor.IsCodeOrDataSegment;
    }

    /// <summary>
    /// LSL: loads the (already granularity-scaled) limit for <paramref name="selector"/> (only
    /// meaningful when <see cref="IsSelectorValidForLsl"/> is true).
    /// </summary>
    public static uint LoadSegmentLimit(State state, IMemory memory, ushort selector) {
        TryReadDescriptorForVerification(state, memory, selector, out SegmentDescriptorCache descriptor);
        return descriptor.Limit;
    }

    /// <summary>VERR: whether <paramref name="selector"/> is a present, readable data or code segment
    /// accessible from the current privilege level.</summary>
    public static bool VerifyReadable(State state, IMemory memory, ushort selector) {
        if (!TryReadDescriptorForVerification(state, memory, selector, out SegmentDescriptorCache descriptor)
            || !descriptor.Present || !descriptor.IsCodeOrDataSegment) {
            return false;
        }
        // Code segments are readable only when the readable bit (access byte bit 1) is set.
        bool readableBit = (descriptor.AccessRights & 0b10) != 0;
        if (descriptor.IsCode && !readableBit) {
            return false;
        }
        // A conforming code segment is accessible from any privilege level; every other segment
        // (data, or non-conforming code) requires max(RPL, CPL) <= DPL, same as a normal segment load.
        if (descriptor.IsCode && descriptor.IsConforming) {
            return true;
        }
        byte rpl = new SegmentSelector(selector).RequestedPrivilegeLevel;
        return Math.Max(rpl, state.Cpl) <= descriptor.DescriptorPrivilegeLevel;
    }

    /// <summary>VERW: whether <paramref name="selector"/> is a present, writable data segment
    /// accessible from the current privilege level.</summary>
    public static bool VerifyWritable(State state, IMemory memory, ushort selector) {
        if (!TryReadDescriptorForVerification(state, memory, selector, out SegmentDescriptorCache descriptor)
            || !descriptor.Present || !descriptor.IsCodeOrDataSegment || descriptor.IsCode) {
            return false;
        }
        // Data segments are writable only when the writable bit (access byte bit 1) is set.
        if ((descriptor.AccessRights & 0b10) == 0) {
            return false;
        }
        // Data segments are never conforming, so the privilege check always applies.
        byte rpl = new SegmentSelector(selector).RequestedPrivilegeLevel;
        return Math.Max(rpl, state.Cpl) <= descriptor.DescriptorPrivilegeLevel;
    }


    private static bool TryReadDescriptorForVerification(State state, IMemory memory, ushort selector, out SegmentDescriptorCache descriptor) {
        return DescriptorTableReader.TryReadDescriptor(selector, state.Gdtr.Base, state.Gdtr.Limit,
            state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)], out descriptor);
    }

    /// <summary>
    /// Real hardware always keeps a segment register's hidden descriptor cache in sync with its raw
    /// value while in real mode, even when that value was set by something other than a segment-load
    /// instruction (e.g. a loader writing CS/DS directly at boot). This emulator only refreshes the
    /// cache on explicit loads (<see cref="LoadSegmentRegister"/>), so the moment CR0.PE transitions
    /// to 1 - before the mandatory far jump reloads CS - any register whose cache was never refreshed
    /// this way would resolve through a stale, mismatched cache. Snapshotting every cache to its
    /// real-mode equivalent here restores the invariant real hardware never breaks.
    /// </summary>
    private static void RefreshDescriptorCachesForRealModeTransition(State state) {
        foreach (SegmentRegisterIndex index in Enum.GetValues<SegmentRegisterIndex>()) {
            state.SegmentDescriptorCaches[index] = SegmentDescriptorCache.CreateRealMode(state.SegmentRegisters.UInt16[(uint)index]);
        }
    }
}
