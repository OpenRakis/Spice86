namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

public abstract class MovRegImm<T> : InstructionWithValueField<T>, IInstructionWithRegisterIndex where T : IUnsignedNumber<T> {
    public MovRegImm(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        InstructionField<T> valueField,
        int registerIndex) :
        base(address, opcodeField, prefixes, valueField) {
        
        RegisterIndex = registerIndex;
    }

    public int RegisterIndex { get; }
}

[MovRegImm("8HighLow", "byte")]
public partial class MovRegImm8;

[MovRegImm("16", "ushort")]
public partial class MovRegImm16;

[MovRegImm("32", "uint")]
public partial class MovRegImm32;