namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

public class Grp3Parser : BaseGrpOperationParser {
    public Grp3Parser(BaseInstructionParser instructionParser) : base(instructionParser) {
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        IInstructionWithModRmFactory operationFactory = GetOperationParser(groupIndex);
        return operationFactory.Parse(context, modRmContext, bitWidth);
    }

    private IInstructionWithModRmFactory GetOperationParser(int groupIndex) {
        return groupIndex switch {
            0 => new Grp3TestInstructionWithModRmFactory(this),
            2 => new Grp3NotRmOperationFactory(this),
            3 => new Grp3NegRmOperationFactory(this),
            4 => new Grp3MulRmAccOperationFactory(this),
            5 => new Grp3ImulRmAccOperationFactory(this),
            6 => new Grp3DivRmAccOperationFactory(this),
            7 => new Grp3IdivRmAccOperationFactory(this),
            _ => throw new InvalidGroupIndexException(_state, groupIndex)
        };
    }
}

public class Grp3TestInstructionWithModRmFactory : BaseInstructionParser, IInstructionWithModRmFactory {
    public Grp3TestInstructionWithModRmFactory(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, BitWidth bitWidth) {
        return bitWidth switch {
            BitWidth.BYTE_8 => new Grp3TestRmImm8(context.Address, context.OpcodeField, context.Prefixes, modRmContext,
                _instructionReader.UInt8.NextField(false)),
            BitWidth.WORD_16 => new Grp3TestRmImm16(context.Address, context.OpcodeField, context.Prefixes, modRmContext,
                _instructionReader.UInt16.NextField(false)),
            BitWidth.DWORD_32 => new Grp3TestRmImm32(context.Address, context.OpcodeField, context.Prefixes, modRmContext,
                _instructionReader.UInt32.NextField(false)),
        };
    }
}

[OperationModRmFactory("Grp3NotRm")]
public partial class Grp3NotRmOperationFactory;

[OperationModRmFactory("Grp3NegRm")]
public partial class Grp3NegRmOperationFactory;

[OperationModRmFactory("Grp3MulRmAcc")]
public partial class Grp3MulRmAccOperationFactory;

[OperationModRmFactory("Grp3ImulRmAcc")]
public partial class Grp3ImulRmAccOperationFactory;

[OperationModRmFactory("Grp3DivRmAcc")]
public partial class Grp3DivRmAccOperationFactory;

[OperationModRmFactory("Grp3IdivRmAcc")]
public partial class Grp3IdivRmAccOperationFactory;