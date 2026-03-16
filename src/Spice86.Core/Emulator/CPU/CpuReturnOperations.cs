namespace Spice86.Core.Emulator.CPU;

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
    /// Performs a 32-bit far return: pops IP and CS from stack, then discards additional bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FarRet32(ushort numberOfBytesToPop = 0) {
        _state.IpSegmentedAddress = _stack.PopSegmentedAddress32();
        // CS padding, discard at least 2
        _stack.Discard(numberOfBytesToPop + 2);
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
        _state.IP = (ushort)_stack.Pop32();
        _stack.Discard(numberOfBytesToPop);
    }

    /// <summary>
    /// Performs an interrupt return: pops IP, CS, and flags from stack.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InterruptRet() {
        _state.IpSegmentedAddress = _stack.PopSegmentedAddress();
        _state.Flags.FlagRegister = _stack.Pop16();
    }
}
