namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public abstract class OperationImmParser : BaseInstructionParser {
    public OperationImmParser(BaseInstructionParser other) : base(other) {
    }
    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        return Parse(context, bitWidth);
    }

    protected abstract CfgInstruction Parse(ParsingContext context, BitWidth bitWidth);
}

[OperationImmParser("TestAccImm", Has8: true, IsOnlyField8: false, IsUnsignedField: true, IsFinalValue: false)]
partial class TestAccImmParser;

[OperationImmParser("InAccImm", Has8: true, IsOnlyField8: true, IsUnsignedField: true, IsFinalValue: false)]
partial class InAccImmParser;

[OperationImmParser("OutAccImm", Has8: true, IsOnlyField8: true, IsUnsignedField: true, IsFinalValue: false)]
partial class OutAccImmParser;

[OperationImmParser("PushImm", Has8: false, IsOnlyField8: false, IsUnsignedField: true, IsFinalValue: false)]
partial class PushImmParser;

[OperationImmParser("PushImm8SignExtended", Has8: false, IsOnlyField8: true, IsUnsignedField: false, IsFinalValue: false)]
partial class PushImm8SignExtendedParser;
[OperationImmParser("Jcxz", Has8: false, IsOnlyField8: true, IsUnsignedField: false, IsFinalValue: true)]
partial class JcxzParser;