namespace Spice86.Core.Emulator.CPU;

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
    public void FarRet16(ushort numberOfBytesToPop = 0) {
        _state.IpSegmentedAddress = _stack.PopSegmentedAddress();
        _stack.Discard(numberOfBytesToPop);
    }

    /// <summary>
    /// Performs a 16-bit near return: pops IP from stack, then discards additional bytes.
    /// </summary>
    public void NearRet16(ushort numberOfBytesToPop = 0) {
        _state.IP = _stack.Pop16();
        _stack.Discard(numberOfBytesToPop);
    }

    /// <summary>
    /// Performs an interrupt return: pops IP, CS, and flags from stack.
    /// </summary>
    public void InterruptRet(State state, Stack stack) {
        state.IpSegmentedAddress = stack.PopSegmentedAddress();
        state.Flags.FlagRegister = stack.Pop16();
    }
}
