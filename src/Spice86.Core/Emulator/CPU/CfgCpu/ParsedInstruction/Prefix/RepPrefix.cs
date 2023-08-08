namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;

public class RepPrefix : InstructionPrefix {
    public RepPrefix(InstructionField<byte> prefixField, bool continueZeroFlagValue) : base(prefixField) {
        ContinueZeroFlagValue = continueZeroFlagValue;
    }

    public bool ContinueZeroFlagValue { get; }
}