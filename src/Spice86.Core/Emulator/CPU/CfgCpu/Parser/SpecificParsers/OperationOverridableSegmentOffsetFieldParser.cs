namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public abstract class OperationOverridableSegmentOffsetFieldParser : BaseInstructionParser {
    public OperationOverridableSegmentOffsetFieldParser(BaseInstructionParser other) : base(other) {
    }
    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        return Parse(context, bitWidth, SegmentFromPrefixesOrDs(context), _instructionReader.UInt16.NextField(false));
    }
    protected abstract CfgInstruction Parse(ParsingContext context, BitWidth bitWidth, int segmentRegisterIndex, InstructionField<ushort> offsetField);

}
[OperationOverridableSegmentOffsetFieldParser("MovMoffsAcc")]
public partial class MovMoffsAccParser;

[OperationOverridableSegmentOffsetFieldParser("MovAccMoffs")]
public partial class MovAccMoffsParser;