namespace Spice86.Core.Emulator.CPU.DescriptorTables;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Protected-mode interrupt/exception dispatch through the IDT, shared by both execution paths
/// (<c>InstructionExecutionHelper</c> and <c>CSharpOverrideHelper</c>) so software `INT n`, hardware
/// interrupts, and CPU exceptions resolve to the same target and push the same return frame whether
/// the destination runs interpreted or as a generated/hand-written C# override. Real and Virtual-8086
/// mode keep using the real-mode IVT and are not routed through this class.
/// 16-bit and 32-bit interrupt/trap gates are both supported. 32-bit gates use the real 32-bit stack
/// frame width (EIP/CS/EFLAGS pushed as dwords, matching <see cref="Stack.PushFarPointer32"/>'s layout,
/// and a dword error code) since some CPU-conformance tests check the exact byte offsets of that frame;
/// the pushed EIP's high word is always 0 since <see cref="State.IP"/> tracks only a 16-bit instruction
/// pointer throughout this codebase - a genuine EIP-tracking implementation remains out of scope.
/// IDT task gates are supported too: dispatch redirects to <see cref="TaskSwitchOperations.SwitchToNewTask"/>
/// instead of pushing an interrupt frame, exactly like a CALL to a TSS selector - the interrupted task's
/// resume point is saved into its own TSS rather than onto its stack, and any exception error code is
/// pushed onto the NEW task's stack once the switch completes, matching real hardware. GDT/LDT task-gate
/// descriptors (reached via a direct CALL/JMP rather than an IDT vector) are not yet implemented.
/// </summary>
public static class ProtectedModeInterruptDispatcher {
    /// <summary>
    /// Decodes the IDT gate for <paramref name="vectorNumber"/>, validates it, and either performs a task
    /// switch (see <see cref="TaskSwitchOperations.SwitchToNewTask"/>) if the gate is a task gate, or
    /// switches to the target ring's stack via SS0:ESP0 read directly from the current TSS on privilege
    /// escalation and pushes the return frame (old SS:SP if escalating, then FLAGS, then
    /// <paramref name="expectedReturn"/>, then the error code if any) and sets CS:IP to the gate's target.
    /// Returns the resolved target address.
    /// </summary>
    /// <param name="expectedReturn">
    /// The address to resume at once the handler returns. Passed explicitly rather than read from
    /// <c>state.IpSegmentedAddress</c> because generated/override code does not keep <c>State.IP</c>
    /// continuously in sync the way the interpreter does.
    /// </param>
    public static SegmentedAddress Dispatch(State state, IMemory memory, Stack stack, byte vectorNumber, bool checkGateDpl,
        ushort? errorCode, SegmentedAddress expectedReturn) {
        RawGateDescriptor gate = ReadIdtGate(state, memory, vectorNumber);
        if (checkGateDpl && gate.DescriptorPrivilegeLevel < state.Cpl) {
            throw new CpuGeneralProtectionFaultException(
                $"Vector 0x{vectorNumber:X2} gate DPL {gate.DescriptorPrivilegeLevel} < CPL {state.Cpl}", IdtErrorCode(vectorNumber));
        }
        if (gate.GateType == GateType.TaskGate) {
            return DispatchViaTaskGate(state, memory, stack, gate.Selector, errorCode, expectedReturn, vectorNumber);
        }
        bool is32Bit = gate.GateType is GateType.InterruptGate32 or GateType.TrapGate32;
        if (gate.GateType is not (GateType.InterruptGate16 or GateType.TrapGate16 or GateType.InterruptGate32 or GateType.TrapGate32)) {
            throw new UnhandledOperationException(state, $"IDT gate type {gate.GateType} is not yet supported");
        }
        if (!DescriptorTableReader.TryReadDescriptor(gate.Selector, state.Gdtr.Base, state.Gdtr.Limit,
                state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)], out SegmentDescriptorCache targetCode)) {
            throw new CpuGeneralProtectionFaultException($"Gate selector 0x{gate.Selector:X4} is invalid", IdtErrorCode(vectorNumber));
        }
        if (!targetCode.IsConforming && targetCode.DescriptorPrivilegeLevel > state.Cpl) {
            throw new CpuGeneralProtectionFaultException(
                $"Gate target DPL {targetCode.DescriptorPrivilegeLevel} is less privileged than CPL {state.Cpl}", IdtErrorCode(vectorNumber));
        }

        if (targetCode.DescriptorPrivilegeLevel < state.Cpl) {
            // CS must be loaded before SS: state.Cpl (used to validate the new stack segment's RPL/DPL)
            // is derived from CS, so SS validation must see the NEW (more privileged) CPL, not the old one.
            (ushort newSs, uint newSp) = ReadRing0StackFromTss(state, memory);
            ushort oldSs = state.SS;
            uint oldSp = stack.GetStackPointer();
            // Reflecting from V86 mode always leaves it (real hardware treats the handler as running in
            // ordinary protected mode): clear VM only now, after every CPL-dependent check above has
            // already read the V86-implies-CPL3 value, so the CS/SS loads below resolve through the GDT
            // instead of re-synthesizing a real-mode-style cache from the raw selector.
            state.Flags.SetFlag(Flags.Virtual8086Mode, false);
            ushort escalatedCs = (ushort)((gate.Selector & 0xFFFC) | targetCode.DescriptorPrivilegeLevel);
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, escalatedCs);
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.SsIndex, newSs);
            if (is32Bit) {
                stack.SetStackPointer(newSp);
                // Real x86 pushes [old SS, old ESP] on the target ring's stack, then the return frame.
                // The outer ring restores in reverse order: old ESP first, then old SS.
                stack.Push32(oldSs);
                stack.Push32(oldSp);
            } else {
                stack.SetStackPointer(newSp);
                stack.Push16(oldSs);
                stack.Push16((ushort)oldSp);
            }
        } else {
            // Same-privilege dispatch (conforming target, or DPL == CPL): CPL is unchanged, so the loaded
            // CS's RPL must match the CURRENT CPL, not the raw (RPL=0) selector baked into the gate.
            ushort sameLevelCs = (ushort)((gate.Selector & 0xFFFC) | state.Cpl);
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, sameLevelCs);
        }
        if (is32Bit) {
            stack.Push32(state.Flags.FlagRegister);
            stack.PushFarPointer32(new SegmentedAddress32(expectedReturn.Segment, expectedReturn.Offset));
            if (errorCode.HasValue) {
                stack.Push32(errorCode.Value);
            }
        } else {
            stack.Push16(state.Flags.FlagRegister16);
            stack.PushSegmentedAddress(expectedReturn);
            if (errorCode.HasValue) {
                stack.Push16(errorCode.Value);
            }
        }
        if (gate.GateType is GateType.InterruptGate16 or GateType.InterruptGate32) {
            state.InterruptFlag = false;
        }
        if (is32Bit) {
            state.EIP = gate.Offset;
        }
        state.IP = (ushort)gate.Offset;
        return new SegmentedAddress(gate.Selector, (ushort)gate.Offset);
    }

    /// <summary>
    /// Dispatches an interrupt/exception that vectors through an IDT task gate: switches to the task
    /// referenced by <paramref name="tssSelector"/> (saving the interrupted task's resume point,
    /// <paramref name="expectedReturn"/>, into its own TSS rather than pushing it onto its stack), then
    /// pushes any exception error code onto the NEW task's stack once the switch has completed, matching
    /// real hardware's task-gate error-code delivery.
    /// </summary>
    private static SegmentedAddress DispatchViaTaskGate(State state, IMemory memory, Stack stack, ushort tssSelector,
        ushort? errorCode, SegmentedAddress expectedReturn, byte vectorNumber) {
        if (!TaskSwitchOperations.TryReadAvailableTss(state, memory, tssSelector)) {
            throw new CpuGeneralProtectionFaultException($"Task gate TSS selector 0x{tssSelector:X4} is invalid", IdtErrorCode(vectorNumber));
        }
        SegmentedAddress target = TaskSwitchOperations.SwitchToNewTask(state, memory, tssSelector, expectedReturn.Offset);
        if (errorCode.HasValue) {
            stack.Push16(errorCode.Value);
        }
        return target;
    }

    /// <summary>
    /// Protected-mode 32-bit IRETD: if EFLAGS.NT is set, performs a task switch back to the calling task
    /// via its TSS back-link (<see cref="TaskSwitchOperations.SwitchBackViaBackLink"/>) instead of an
    /// ordinary return. Otherwise pops EIP, CS (padded to a dword, matching
    /// <see cref="Stack.PushFarPointer32"/>'s layout) and EFLAGS; if the popped CS's RPL is less
    /// privileged than the current CPL (i.e. returning outward from a privilege escalation), also pops
    /// ESP and SS (each pushed as a dword by the escalating dispatch path).
    /// </summary>
    public static void InterruptReturn32(State state, IMemory memory, Stack stack) {
        if (state.Flags.GetFlag(Flags.NestedTask)) {
            TaskSwitchOperations.SwitchBackViaBackLink(state, memory);
            return;
        }
        byte cplBeforeReturn = state.Cpl;
        SegmentedAddress32 poppedCsEip = stack.PopSegmentedAddress32();
        uint poppedEflags = stack.Pop32();
        byte returningRpl = new SegmentSelector(poppedCsEip.Segment).RequestedPrivilegeLevel;
        state.EIP = poppedCsEip.Offset;
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, poppedCsEip.Segment);
        state.Flags.FlagRegister = poppedEflags;
        if (returningRpl > cplBeforeReturn) {
            // Real x86 restores the old stack pointer before reloading SS.
            uint poppedEsp = stack.Pop32();
            ushort poppedSs = (ushort)stack.Pop32();
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.SsIndex, poppedSs);
            stack.SetStackPointer(poppedEsp);
            PrivilegeChecks.NullifyInaccessibleDataSegments(state);
        }
    }

    /// <summary>
    /// Protected-mode 16-bit IRET: if EFLAGS.NT is set, performs a task switch back to the calling task
    /// via its TSS back-link (<see cref="TaskSwitchOperations.SwitchBackViaBackLink"/>) instead of an
    /// ordinary return. Otherwise pops IP, CS, and FLAGS; if the popped CS's RPL is less privileged
    /// than the current CPL (i.e. returning outward from a privilege escalation), also pops SP and SS.
    /// </summary>
    public static void InterruptReturn16(State state, IMemory memory, Stack stack) {
        if (state.Flags.GetFlag(Flags.NestedTask)) {
            TaskSwitchOperations.SwitchBackViaBackLink(state, memory);
            return;
        }
        byte cplBeforeReturn = state.Cpl;
        ushort poppedIp = stack.Pop16();
        ushort poppedCs = stack.Pop16();
        ushort poppedFlags = stack.Pop16();
        byte returningRpl = new SegmentSelector(poppedCs).RequestedPrivilegeLevel;
        state.IP = poppedIp;
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, poppedCs);
        state.Flags.FlagRegister16 = poppedFlags;
        if (returningRpl > cplBeforeReturn) {
            // 16-bit IRET restores SP before SS, matching the real stack-save order.
            ushort poppedSp = stack.Pop16();
            ushort poppedSs = stack.Pop16();
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.SsIndex, poppedSs);
            stack.SetStackPointer(poppedSp);
            PrivilegeChecks.NullifyInaccessibleDataSegments(state);
        }
    }

    /// <summary>
    /// Protected-mode 16-bit far RET: pops IP and CS; a target RPL less privileged than the current CPL
    /// (i.e. returning outward across a privilege boundary) additionally pops SS:SP after discarding
    /// <paramref name="numberOfBytesToPop"/> from the callee stack, then adds it again to the restored
    /// SP (no call-gate parameter copying is implemented, so this only matters for `RETF imm16` cleanup
    /// conventions, not genuine copied parameters). Returning to a MORE privileged level is a #GP.
    /// </summary>
    public static void FarReturn16(State state, IMemory memory, Stack stack, ushort numberOfBytesToPop) {
        byte cplBeforeReturn = state.Cpl;
        ushort poppedIp = stack.Pop16();
        ushort poppedCs = stack.Pop16();
        byte returningRpl = new SegmentSelector(poppedCs).RequestedPrivilegeLevel;
        if (returningRpl < cplBeforeReturn) {
            throw new CpuGeneralProtectionFaultException(
                $"RETF cannot return to a more privileged level (target RPL {returningRpl} < CPL {cplBeforeReturn})", new SegmentSelector(poppedCs).ErrorCode);
        }
        state.IP = poppedIp;
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, poppedCs);
        stack.Discard(numberOfBytesToPop);
        if (returningRpl > cplBeforeReturn) {
            ushort poppedSp = stack.Pop16();
            ushort poppedSs = stack.Pop16();
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.SsIndex, poppedSs);
            stack.SetStackPointer((ushort)(poppedSp + numberOfBytesToPop));
            PrivilegeChecks.NullifyInaccessibleDataSegments(state);
        }
    }

    /// <summary>
    /// Protected-mode 32-bit RETF: pops EIP and CS (padded to a dword, matching
    /// <see cref="Stack.PushFarPointer32"/>'s layout); a target RPL less privileged than the current CPL
    /// (i.e. returning outward across a privilege boundary) additionally pops ESP:SS after discarding
    /// <paramref name="numberOfBytesToPop"/> from the callee stack, then adds it again to the restored
    /// ESP. Returning to a MORE privileged level is a #GP.
    /// </summary>
    public static void FarReturn32(State state, IMemory memory, Stack stack, ushort numberOfBytesToPop) {
        byte cplBeforeReturn = state.Cpl;
        SegmentedAddress32 poppedCsEip = stack.PopSegmentedAddress32();
        byte returningRpl = new SegmentSelector(poppedCsEip.Segment).RequestedPrivilegeLevel;
        if (returningRpl < cplBeforeReturn) {
            throw new CpuGeneralProtectionFaultException(
                $"RETF cannot return to a more privileged level (target RPL {returningRpl} < CPL {cplBeforeReturn})", new SegmentSelector(poppedCsEip.Segment).ErrorCode);
        }
        state.EIP = poppedCsEip.Offset;
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, poppedCsEip.Segment);
        stack.Discard(numberOfBytesToPop);
        if (returningRpl > cplBeforeReturn) {
            // RETF restores the old stack pointer before SS, in reverse of the push order used during the
            // privilege change.
            uint poppedEsp = stack.Pop32();
            ushort poppedSs = (ushort)stack.Pop32();
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.SsIndex, poppedSs);
            stack.SetStackPointer(poppedEsp + numberOfBytesToPop);
            PrivilegeChecks.NullifyInaccessibleDataSegments(state);
        }
    }

    private static RawGateDescriptor ReadIdtGate(State state, IMemory memory, byte vectorNumber) {
        uint entryOffset = (uint)vectorNumber * 8;
        if (entryOffset + 7 > state.Idtr.Limit) {
            throw new CpuGeneralProtectionFaultException($"Vector 0x{vectorNumber:X2} is outside the IDT limit", IdtErrorCode(vectorNumber));
        }
        Span<byte> gateBytes = stackalloc byte[8];
        for (int i = 0; i < 8; i++) {
            gateBytes[i] = memory[memory.Mmu.TranslateLinearAddress(state.Idtr.Base + entryOffset + (uint)i, isWrite: false)];
        }
        RawGateDescriptor gate = new(gateBytes);
        if (!gate.Present) {
            throw new CpuGeneralProtectionFaultException($"Vector 0x{vectorNumber:X2} gate is not present", IdtErrorCode(vectorNumber));
        }
        return gate;
    }

    private static ushort IdtErrorCode(byte vectorNumber) {
        // Selector-like error code: bit 1 set means the index refers to the IDT.
        return (ushort)((vectorNumber * 8) | 0b10);
    }

    /// <summary>
    /// Reads SS0:ESP0 (the ring-0 stack pointer) from the standard 32-bit TSS layout. Shared with
    /// <see cref="ProtectedModeCallGateDispatcher"/>, which needs the identical ring-0-stack lookup for
    /// privilege-escalating CALLs through a call gate.
    /// </summary>
    internal static (ushort ss0, uint sp0) ReadRing0StackFromTss(State state, IMemory memory) {
        uint tssBase = state.Tr.DescriptorCache.Base;
        uint esp0 = memory.UInt32[memory.Mmu.TranslateLinearAddress(tssBase + 4, isWrite: false)];
        ushort ss0 = memory.UInt16[memory.Mmu.TranslateLinearAddress(tssBase + 8, isWrite: false)];
        return (ss0, esp0);
    }
}
