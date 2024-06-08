namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for instructions that only have the opcode.
/// The opcode has a reg index to indicate on which register to perform the operation.
/// Operation is performed on 16 or 32 bits operands depending on the operand size prefix.
/// </summary>
public abstract class OperationOnRegIndexParser : BaseInstructionParser {
    public OperationOnRegIndexParser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        bool hasOperandSize32) {
        int regIndex = ComputeRegIndex(opcodeField);
        BitWidth bitWidth = GetBitWidth(false, hasOperandSize32);
        return Build(address, opcodeField, prefixes, regIndex, bitWidth);
    }
    protected abstract CfgInstruction Build(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, int regIndex, BitWidth bitWidth);
}

[OperationOnRegIndexParser("IncReg")]
public partial class IncRegIndexParser;
[OperationOnRegIndexParser("DecReg")]
public partial class DecRegIndexParser;
[OperationOnRegIndexParser("PushReg")]
public partial class PushRegIndexParser;
[OperationOnRegIndexParser("PopReg")]
public partial class PopRegIndexParser;