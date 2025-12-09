namespace Spice86.Core.Emulator.CPU;

/// <summary>
/// Static helper methods for CPU return operations (RET, RETF, IRET).
/// </summary>
public static class ReturnOperationsHelper {
    /// <summary>
    /// Performs a 16-bit far return: pops IP and CS from stack, then discards additional bytes.
    /// </summary>
    public static void FarRet16(State state, Stack stack, int numberOfBytesToPop = 0) {
        state.IP = stack.Pop16();
        state.CS = stack.Pop16();
        stack.Discard(numberOfBytesToPop);
    }

    /// <summary>
    /// Performs a 16-bit near return: pops IP from stack, then discards additional bytes.
    /// </summary>
    public static void NearRet16(State state, Stack stack, int numberOfBytesToPop = 0) {
        state.IP = stack.Pop16();
        stack.Discard(numberOfBytesToPop);
    }

    /// <summary>
    /// Performs an interrupt return: pops IP, CS, and flags from stack.
    /// </summary>
    public static void InterruptRet(State state, Stack stack) {
        state.IP = stack.Pop16();
        state.CS = stack.Pop16();
        state.Flags.FlagRegister = stack.Pop16();
    }
}
