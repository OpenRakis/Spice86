namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

public abstract class BaseOperationModRmFactory : BaseInstructionParser, IInstructionWithModRmFactory {
    protected BaseOperationModRmFactory(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, BitWidth bitWidth) {
        return bitWidth switch {
            BitWidth.BYTE_8 => BuildOperandSize8(context, modRmContext),
            BitWidth.WORD_16 => BuildOperandSize16(context, modRmContext),
            BitWidth.DWORD_32 => BuildOperandSize32(context, modRmContext),
        };
    }

    protected abstract CfgInstruction BuildOperandSize8(ParsingContext context, ModRmContext modRmContext);
    protected abstract CfgInstruction BuildOperandSize16(ParsingContext context, ModRmContext modRmContext);
    protected abstract CfgInstruction BuildOperandSize32(ParsingContext context, ModRmContext modRmContext);
}