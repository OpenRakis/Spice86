namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for instructions that only have the opcode.
/// The opcode has a reg index to indicate on which register to perform the operation.
/// Operation is performed on 16 or 32 bits operands depending on the operand size prefix.
/// </summary>
public abstract class OperationOverridableSegmentRegisterIndexParser : BaseInstructionParser {
    public OperationOverridableSegmentRegisterIndexParser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        int segmentRegisterIndex = GetSegmentRegisterOverrideOrDs(context);
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        return Parse(context, segmentRegisterIndex, bitWidth);
    }
    protected abstract CfgInstruction Parse(ParsingContext context, int segmentRegisterIndex, BitWidth bitWidth);
}

[OperationOverridableSegmentRegisterIndexParser("Movs")]
public partial class MovsParser;
[OperationOverridableSegmentRegisterIndexParser("Cmps")]
public partial class CmpsParser;
[OperationOverridableSegmentRegisterIndexParser("Lods")]
public partial class LodsParser;
[OperationOverridableSegmentRegisterIndexParser("OutsDx")]
public partial class OutsDxParser;