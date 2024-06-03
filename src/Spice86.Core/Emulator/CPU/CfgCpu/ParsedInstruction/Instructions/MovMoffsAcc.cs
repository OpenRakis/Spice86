namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class MovMoffsAcc : CfgInstruction, IInstructionWithSegmentRegisterIndexAndOffsetField {
    protected MovMoffsAcc(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes, uint segmentRegisterIndex, InstructionField<ushort> offsetField) : base(address, opcodeField, prefixes) {
        SegmentRegisterIndex = segmentRegisterIndex;
        OffsetField = offsetField;
        FieldsInOrder.Add(offsetField);
    }
    
    public uint SegmentRegisterIndex { get; }
    public InstructionField<ushort> OffsetField { get; }
}

[MovMoffsAcc("AL", 8)]
public partial class MovMoffsAcc8;

[MovMoffsAcc("AX", 16)]
public partial class MovMoffsAcc16;

[MovMoffsAcc("EAX", 32)]
public partial class MovMoffsAcc32;