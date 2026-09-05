namespace Spice86.Core.Emulator.CPU.DescriptorTables;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Protected-mode CALL-gate dispatch: a far CALL whose target selector resolves to a GDT/LDT system
/// descriptor of type <see cref="GateType.CallGate16"/> or <see cref="GateType.CallGate32"/> (rather
/// than an ordinary code segment) is redirected through the gate to its real target. Distinct from
/// <see cref="ProtectedModeInterruptDispatcher"/>'s IDT gates: the gate lives in the GDT/LDT, and access
/// requires the caller's CPL and the call selector's RPL to both be &lt;= the gate's DPL, while the
/// target code segment's DPL must be &lt;= CPL (a call gate can only enter equally- or more-privileged
/// code). Task gates reached via a direct CALL/JMP (as opposed to an IDT vector) are not yet supported.
/// </summary>
public static class ProtectedModeCallGateDispatcher {
    /// <summary>
    /// Attempts to decode <paramref name="selector"/> as a 16-bit call-gate descriptor in the GDT/LDT.
    /// Returns <c>false</c> (with no side effects) when the selector is null, out of table bounds, or
    /// resolves to an ordinary code/data segment or a different system-descriptor type - callers should
    /// fall back to direct far-call/jump handling in every such case.
    /// </summary>
    public static bool TryReadCallGate(State state, IMemory memory, ushort selector, out RawGateDescriptor gate) {
        gate = default;
        if (state.CpuMode != CpuMode.Protected) {
            return false;
        }
        SegmentSelector segmentSelector = new(selector);
        if (segmentSelector.IsNull) {
            return false;
        }
        uint tableBase = segmentSelector.ReferencesLocalDescriptorTable ? state.Ldtr.DescriptorCache.Base : state.Gdtr.Base;
        uint tableLimit = segmentSelector.ReferencesLocalDescriptorTable ? state.Ldtr.DescriptorCache.Limit : state.Gdtr.Limit;
        uint entryOffset = (uint)segmentSelector.Index * 8u;
        if (entryOffset + 7u > tableLimit) {
            return false;
        }
        Span<byte> descriptorBytes = stackalloc byte[8];
        for (int i = 0; i < 8; i++) {
            descriptorBytes[i] = memory[memory.Mmu.TranslateLinearAddress(tableBase + entryOffset + (uint)i, isWrite: false)];
        }
        // Access-byte bit 4 (S) distinguishes a code/data descriptor (S=1) from a system descriptor
        // (S=0, e.g. a gate); it occupies the same byte position in both raw descriptor layouts.
        if ((descriptorBytes[5] & 0b0001_0000) != 0) {
            return false;
        }
        RawGateDescriptor candidate = new(descriptorBytes);
        if (candidate.GateType is not (GateType.CallGate16 or GateType.CallGate32)) {
            return false;
        }
        gate = candidate;
        return true;
    }

    /// <summary>
    /// Validates access to <paramref name="gate"/> from a far CALL through selector
    /// <paramref name="callSelector"/> (gate must be present; gate DPL must be &gt;= CPL and &gt;= the
    /// call selector's RPL), resolves and validates the gate's target code segment (present, a code
    /// segment, DPL &lt;= CPL), switches to the target ring's stack via SS0:ESP0 from the current TSS
    /// when escalating, and pushes the return frame (old SS:SP if escalating, then the return address).
    /// Returns the resolved target address.
    /// </summary>
    public static SegmentedAddress Dispatch(State state, IMemory memory, Stack stack, RawGateDescriptor gate,
        ushort callSelector, SegmentedAddress expectedReturn) {
        byte cpl = state.Cpl;
        byte callSelectorRpl = new SegmentSelector(callSelector).RequestedPrivilegeLevel;
        bool is32Bit = gate.GateType == GateType.CallGate32;
        if (!gate.Present) {
            throw new CpuGeneralProtectionFaultException($"Call gate 0x{callSelector:X4} is not present", new SegmentSelector(callSelector).ErrorCode);
        }
        if (gate.DescriptorPrivilegeLevel < cpl || gate.DescriptorPrivilegeLevel < callSelectorRpl) {
            throw new CpuGeneralProtectionFaultException(
                $"Call gate 0x{callSelector:X4}: DPL {gate.DescriptorPrivilegeLevel} must be >= CPL {cpl} and >= RPL {callSelectorRpl}", new SegmentSelector(callSelector).ErrorCode);
        }
        if (!DescriptorTableReader.TryReadDescriptor(gate.Selector, state.Gdtr.Base, state.Gdtr.Limit,
                state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)], out SegmentDescriptorCache targetCode)) {
            throw new CpuGeneralProtectionFaultException($"Call gate target selector 0x{gate.Selector:X4} is invalid", new SegmentSelector(callSelector).ErrorCode);
        }
        if (!targetCode.Present) {
            throw new CpuSegmentNotPresentException($"Call gate target selector 0x{gate.Selector:X4} is not present", new SegmentSelector(gate.Selector).ErrorCode);
        }
        if (!targetCode.IsCode) {
            throw new CpuGeneralProtectionFaultException($"Call gate target selector 0x{gate.Selector:X4} is not a code segment", new SegmentSelector(gate.Selector).ErrorCode);
        }
        if (targetCode.DescriptorPrivilegeLevel > cpl) {
            throw new CpuGeneralProtectionFaultException(
                $"Call gate target DPL {targetCode.DescriptorPrivilegeLevel} is less privileged than CPL {cpl}", new SegmentSelector(gate.Selector).ErrorCode);
        }

        if (targetCode.DescriptorPrivilegeLevel < cpl) {
            // CS must be loaded before SS: state.Cpl (used to validate the new stack segment's RPL/DPL)
            // is derived from CS, so SS validation must see the NEW (more privileged) CPL, not the old one.
            (ushort newSs, uint newSp) = ProtectedModeInterruptDispatcher.ReadRing0StackFromTss(state, memory);
            ushort oldSs = state.SS;
            uint oldSp = stack.GetStackPointer();
            state.Flags.SetFlag(Flags.Virtual8086Mode, false);
            ushort escalatedCs = (ushort)((gate.Selector & 0xFFFC) | targetCode.DescriptorPrivilegeLevel);
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, escalatedCs);
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.SsIndex, newSs);
            if (is32Bit) {
                stack.SetStackPointer(newSp);
                // Call-gate privilege escalation uses the same [old SS, old ESP] save order as interrupts.
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
            ushort sameLevelCs = (ushort)((gate.Selector & 0xFFFC) | cpl);
            SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, sameLevelCs);
        }
        if (is32Bit) {
            stack.PushFarPointer32(new SegmentedAddress32(expectedReturn.Segment, expectedReturn.Offset));
            state.EIP = gate.Offset;
            state.IP = (ushort)gate.Offset;
        } else {
            stack.PushSegmentedAddress(expectedReturn);
            state.IP = (ushort)gate.Offset;
        }
        return new SegmentedAddress(gate.Selector, (ushort)gate.Offset);
    }

    /// <summary>
    /// Validates access to <paramref name="gate"/> from a direct far JMP through selector
    /// <paramref name="callSelector"/> (gate must be present; gate DPL must be &gt;= CPL and &gt;= the
    /// call selector's RPL), resolves and validates the gate's target code segment (present, a code
    /// segment). Unlike <see cref="Dispatch"/>, a JMP through a gate can NEVER change CPL: a
    /// non-conforming target must have DPL exactly equal to CPL, and a conforming target still requires
    /// DPL &lt;= CPL - there is no stack switch and no return address is pushed. Returns the resolved
    /// target address.
    /// </summary>
    public static SegmentedAddress DispatchJump(State state, IMemory memory, RawGateDescriptor gate, ushort callSelector) {
        byte cpl = state.Cpl;
        byte callSelectorRpl = new SegmentSelector(callSelector).RequestedPrivilegeLevel;
        if (!gate.Present) {
            throw new CpuGeneralProtectionFaultException($"Call gate 0x{callSelector:X4} is not present", new SegmentSelector(callSelector).ErrorCode);
        }
        if (gate.DescriptorPrivilegeLevel < cpl || gate.DescriptorPrivilegeLevel < callSelectorRpl) {
            throw new CpuGeneralProtectionFaultException(
                $"Call gate 0x{callSelector:X4}: DPL {gate.DescriptorPrivilegeLevel} must be >= CPL {cpl} and >= RPL {callSelectorRpl}", new SegmentSelector(callSelector).ErrorCode);
        }
        if (!DescriptorTableReader.TryReadDescriptor(gate.Selector, state.Gdtr.Base, state.Gdtr.Limit,
                state.Ldtr.DescriptorCache.Base, state.Ldtr.DescriptorCache.Limit, address => memory[memory.Mmu.TranslateLinearAddress(address, isWrite: false)], out SegmentDescriptorCache targetCode)) {
            throw new CpuGeneralProtectionFaultException($"Call gate target selector 0x{gate.Selector:X4} is invalid", new SegmentSelector(callSelector).ErrorCode);
        }
        if (!targetCode.Present) {
            throw new CpuSegmentNotPresentException($"Call gate target selector 0x{gate.Selector:X4} is not present", new SegmentSelector(gate.Selector).ErrorCode);
        }
        if (!targetCode.IsCode) {
            throw new CpuGeneralProtectionFaultException($"Call gate target selector 0x{gate.Selector:X4} is not a code segment", new SegmentSelector(gate.Selector).ErrorCode);
        }
        if (targetCode.IsConforming ? targetCode.DescriptorPrivilegeLevel > cpl : targetCode.DescriptorPrivilegeLevel != cpl) {
            throw new CpuGeneralProtectionFaultException(
                $"JMP via call gate: target DPL {targetCode.DescriptorPrivilegeLevel} is not reachable without a privilege change from CPL {cpl}", new SegmentSelector(gate.Selector).ErrorCode);
        }
        ushort sameLevelCs = (ushort)((gate.Selector & 0xFFFC) | cpl);
        SegmentAndControlRegisterOperations.LoadSegmentRegister(state, memory, (uint)SegmentRegisterIndex.CsIndex, sameLevelCs);
        state.EIP = gate.Offset;
        state.IP = (ushort)gate.Offset;
        return new SegmentedAddress(gate.Selector, (ushort)gate.Offset);
    }
}
