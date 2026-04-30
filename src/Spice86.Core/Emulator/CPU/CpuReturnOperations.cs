namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Exceptions;

using System.Runtime.CompilerServices;

/// <summary>
/// Static helper methods for CPU return operations (RET, RETF, IRET).
/// </summary>
public class ReturnOperationsHelper {
    private readonly State _state;
    private readonly Stack _stack;

    public ReturnOperationsHelper(State state, Stack stack) {
        _state = state;
        _stack = stack;
    }

    /// <summary>
    /// Performs a 16-bit far return: pops IP and CS from stack, then discards additional bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarRet16(ushort numberOfBytesToPop = 0) {
        _state.IpSegmentedAddress = _stack.PopSegmentedAddress();
        _stack.Discard(numberOfBytesToPop);
    }
    
    /// <summary>
    /// Performs a 32-bit far return: pops EIP and CS from stack, then discards additional bytes.
    /// Faults atomically (without modifying SP) if the popped EIP exceeds the real-mode segment limit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarRet32(ushort numberOfBytesToPop = 0) {
        uint eip = _stack.Peek32(0);
        if (eip > ushort.MaxValue) {
            throw new CpuGeneralProtectionFaultException($"Far return target EIP 0x{eip:X8} exceeds real-mode code segment limit 0xFFFF");
        }
        _state.IpSegmentedAddress = _stack.PopSegmentedAddress32().ToSegmentedAddress();
        _stack.Discard(numberOfBytesToPop);
    }


    /// <summary>
    /// Performs a 16-bit near return: pops IP from stack, then discards additional bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NearRet16(ushort numberOfBytesToPop = 0) {
        _state.IP = _stack.Pop16();
        _stack.Discard(numberOfBytesToPop);
    }
    
    /// <summary>
    /// Performs a 32-bit near return: pops IP from stack, then discards additional bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NearRet32(ushort numberOfBytesToPop = 0) {
        uint eip = _stack.Peek32(0);
        if (eip > ushort.MaxValue) {
            throw new CpuGeneralProtectionFaultException($"Near return target EIP 0x{eip:X8} exceeds real-mode code segment limit 0xFFFF");
        }
        _state.IP = (ushort)eip;
        _stack.Discard(numberOfBytesToPop + 4);
    }

    /// <summary>
    /// Performs an interrupt return: pops IP, CS, and flags from stack.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InterruptRet() {
        _state.IpSegmentedAddress = _stack.PopSegmentedAddress();
        _state.Flags.FlagRegister = _stack.Pop16();
    }

    /// <summary>
    /// Performs a 32-bit interrupt return (IRETD): pops EIP, CS (with padding),
    /// and EFLAGS from stack.
    /// Faults atomically (without modifying SP) if the popped EIP exceeds the real-mode code segment limit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InterruptRet32() {
        uint eip = _stack.Peek32(0);
        if (eip > ushort.MaxValue) {
            throw new CpuGeneralProtectionFaultException($"Interrupt return target EIP 0x{eip:X8} exceeds real-mode code segment limit 0xFFFF");
        }
        _state.IpSegmentedAddress = _stack.PopInterruptPointer32();
        _state.Flags.FlagRegister = _stack.Pop32();
    }
}
