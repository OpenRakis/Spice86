namespace Spice86.Core.Emulator.CPU.DescriptorTables;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Protected-mode hardware task switching: a far CALL whose target selector resolves to an available
/// 32-bit TSS descriptor in the GDT (rather than an ordinary code segment or call gate) performs a full
/// task switch instead of an ordinary call - saving every general/segment register, EFLAGS, EIP and CR3
/// into the current task's TSS, then loading the same fields from the new task's TSS. The new task is
/// marked busy and linked back to the calling task via EFLAGS.NT and the new TSS's back-link field, so a
/// subsequent IRET with EFLAGS.NT=1 (<see cref="SwitchBackViaBackLink"/>) resumes the calling task
/// instead of performing an ordinary interrupt return. An interrupt/exception vectoring through an IDT
/// task gate (see <see cref="ProtectedModeInterruptDispatcher"/>) reuses this exact same
/// <see cref="SwitchToNewTask"/> mechanism. Only the CALL/interrupt-triggered (nested) form of task
/// switching is implemented: the calling task's busy bit is never touched (only a JMP-triggered switch
/// clears it), and only <see cref="SwitchBackViaBackLink"/> - not a JMP or another CALL - resumes it.
/// JMP directly to a TSS selector and GDT/LDT task-gate descriptors (reached via a direct CALL/JMP rather
/// than an IDT vector) are not yet supported - matches the existing "JMP through a call gate" and
/// "32-bit call gates" gaps left by call-gate dispatch.
/// </summary>
public static class TaskSwitchOperations {
    private const byte AvailableTss386Type = 0x9;
    private const byte BusyBitMask = 0b0000_0010;

    /// <summary>
    /// Attempts to decode <paramref name="selector"/> as an available 32-bit TSS descriptor in the GDT
    /// (TSS selectors are never resolved through the LDT). Returns <c>false</c> (no side effects) for a
    /// null selector, an out-of-bounds selector, an LDT-referencing selector, or any descriptor that
    /// isn't an available 32-bit TSS - callers should fall back to call-gate/direct-transfer handling.
    /// </summary>
    public static bool TryReadAvailableTss(State state, IMemory memory, ushort selector) {
        if (state.CpuMode != CpuMode.Protected) {
            return false;
        }
        SegmentSelector segmentSelector = new(selector);
        if (segmentSelector.IsNull || segmentSelector.ReferencesLocalDescriptorTable) {
            return false;
        }
        uint entryOffset = (uint)segmentSelector.Index * 8u;
        if (entryOffset + 7u > state.Gdtr.Limit) {
            return false;
        }
        byte typeByte = memory[memory.Mmu.TranslateLinearAddress(state.Gdtr.Base + entryOffset + 5u, isWrite: false)];
        if ((typeByte & 0b0001_0000) != 0) {
            return false; // S bit set: an ordinary code/data segment, not a system descriptor.
        }
        return (typeByte & 0x0F) == AvailableTss386Type;
    }

    /// <summary>
    /// Performs a task switch via far CALL to the TSS selector already validated by
    /// <see cref="TryReadAvailableTss"/>: saves the calling task's full state into its own TSS, loads the
    /// new task's state, marks the new task busy, sets EFLAGS.NT and the new task's back-link to the
    /// calling task, and returns the new task's entry address. This only implements the CALL-triggered
    /// (nested) form of task switching - the calling task's own busy bit is deliberately left untouched
    /// (it is only ever cleared by a JMP-triggered switch, which is not yet supported).
    /// </summary>
    public static SegmentedAddress SwitchToNewTask(State state, IMemory memory, ushort tssSelector, uint returnEip) {
        if (!DescriptorTableReader.TryReadDescriptor(tssSelector, state.Gdtr.Base, state.Gdtr.Limit,
                state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)], out SegmentDescriptorCache newTssDescriptor)) {
            throw new CpuGeneralProtectionFaultException($"TSS selector 0x{tssSelector:X4} is invalid", new SegmentSelector(tssSelector).ErrorCode);
        }
        if (!newTssDescriptor.Present) {
            throw new CpuSegmentNotPresentException($"TSS selector 0x{tssSelector:X4} is not present", new SegmentSelector(tssSelector).ErrorCode);
        }

        ushort oldTssSelector = state.Tr.Selector;
        uint oldTssBase = state.Tr.DescriptorCache.Base;
        TaskStateSegment.SaveState(state, memory, oldTssBase, returnEip);

        uint newTssBase = newTssDescriptor.Base;
        TaskStateSegment.TssSnapshot snapshot = TaskStateSegment.LoadState(state, memory, newTssBase);
        memory.UInt16[memory.Mmu.TranslateLinearAddress(newTssBase + TaskStateSegment.LinkOffset, isWrite: true)] = oldTssSelector;

        state.Tr.Selector = tssSelector;
        state.Tr.DescriptorCache = newTssDescriptor;
        SetBusyBit(memory, GdtDescriptorOffsetOf(state, tssSelector), busy: true);
        state.Flags.FlagRegister |= Flags.NestedTask;

        ApplySnapshotSegments(state, memory, snapshot);
        return new SegmentedAddress(state.CS, state.IP);
    }

    /// <summary>
    /// Performs a task switch back to the calling task via its TSS back-link selector, invoked instead
    /// of an ordinary IRET whenever EFLAGS.NT is set on entry - the mirror image of
    /// <see cref="SwitchToNewTask"/>: saves the current (nested) task's state, clears its busy bit, then
    /// loads the calling task's state (already marked busy from the original switch, left unchanged) and
    /// resumes it exactly where it left off.
    /// </summary>
    public static SegmentedAddress SwitchBackViaBackLink(State state, IMemory memory) {
        ushort nestedTssSelector = state.Tr.Selector;
        uint nestedTssBase = state.Tr.DescriptorCache.Base;
        ushort callerTssSelector = memory.UInt16[memory.Mmu.TranslateLinearAddress(nestedTssBase + TaskStateSegment.LinkOffset, isWrite: false)];

        if (!DescriptorTableReader.TryReadDescriptor(callerTssSelector, state.Gdtr.Base, state.Gdtr.Limit,
                state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)], out SegmentDescriptorCache callerTssDescriptor)) {
            throw new CpuGeneralProtectionFaultException($"Back-link TSS selector 0x{callerTssSelector:X4} is invalid", new SegmentSelector(callerTssSelector).ErrorCode);
        }

        TaskStateSegment.SaveState(state, memory, nestedTssBase, state.IP);
        SetBusyBit(memory, GdtDescriptorOffsetOf(state, nestedTssSelector), busy: false);

        uint callerTssBase = callerTssDescriptor.Base;
        TaskStateSegment.TssSnapshot snapshot = TaskStateSegment.LoadState(state, memory, callerTssBase);

        state.Tr.Selector = callerTssSelector;
        state.Tr.DescriptorCache = callerTssDescriptor;

        ApplySnapshotSegments(state, memory, snapshot);
        return new SegmentedAddress(state.CS, state.IP);
    }

    private static void ApplySnapshotSegments(State state, IMemory memory, TaskStateSegment.TssSnapshot snapshot) {
        SegmentAndControlRegisterOperations.LoadLdtr(state, memory, snapshot.LdtSelector);
        // CS must be loaded before SS: state.Cpl (used to validate the new stack segment) is derived
        // from CS, so SS validation must see the new task's CPL, not whatever CPL was active before.
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, snapshot.Cs);
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.SsIndex, snapshot.Ss);
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.DsIndex, snapshot.Ds);
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.EsIndex, snapshot.Es);
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.FsIndex, snapshot.Fs);
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.GsIndex, snapshot.Gs);
        state.IP = (ushort)snapshot.Eip;
    }

    private static uint GdtDescriptorOffsetOf(State state, ushort selector) {
        return state.Gdtr.Base + (uint)new SegmentSelector(selector).Index * 8u;
    }

    private static void SetBusyBit(IMemory memory, uint descriptorOffset, bool busy) {
        uint typeByteAddress = memory.Mmu.TranslateLinearAddress(descriptorOffset + 5u, isWrite: true);
        byte typeByte = memory[typeByteAddress];
        memory[typeByteAddress] = busy ? (byte)(typeByte | BusyBitMask) : (byte)(typeByte & ~BusyBitMask);
    }
}
