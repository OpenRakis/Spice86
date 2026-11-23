namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// Shared debug-register operations used by both interpreted execution
/// (<c>InstructionExecutionHelper</c>) and generated/hand-written C# overrides
/// (<c>CSharpOverrideHelper</c>), so the two execution paths behave identically.
/// </summary>
public static class DebugRegisterOperations {
    /// <summary>
    /// MOV DRn, r/m32 (0F 23 /r): writes a 32-bit value into debug register <paramref name="drNumber"/>.
    /// Requires privilege level 0 in protected mode. Reserved registers (DR4/DR5) alias DR6/DR7, and the
    /// hardware-reserved bits of DR6/DR7 are kept in the state real hardware holds them (pre-Pentium mask).
    /// </summary>
    public static void WriteDebugRegister(State state, uint drNumber, uint value) {
        if (state.CpuMode == CpuMode.Protected && state.Cpl > 0) {
            throw new CpuGeneralProtectionFaultException($"Writing DR{drNumber} requires privilege level 0");
        }
        switch (drNumber) {
            case 0: state.DebugRegisters.Dr0 = value; break;
            case 1: state.DebugRegisters.Dr1 = value; break;
            case 2: state.DebugRegisters.Dr2 = value; break;
            case 3: state.DebugRegisters.Dr3 = value; break;
            case 4:
            case 6: state.DebugRegisters.Dr6 = (value | 0xFFFF0FF0) & 0xFFFF0000 | 0x0000EFFF; break;
            case 5:
            case 7: state.DebugRegisters.Dr7 = (value | 0x400) & 0xFFFF2FFF; break;
        }
    }
}
