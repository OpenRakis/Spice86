namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for instructions that only have the opcode.
/// The opcode has a reg index to indicate on which register to perform the operation.
/// Operation is performed on 16 or 32 bits operands depending on the operand size prefix.
/// </summary>
public class OperationOnRegIndexParser : BaseInstructionParser {
    private readonly string _operation;
    private readonly ReflectionHelper _reflectionHelper;

    public OperationOnRegIndexParser(BaseInstructionParser other, string operation) : base(other) {
        _operation = operation;
        _reflectionHelper = new();
    }

    public CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        bool hasOperandSize32) {
        int regIndex = ComputeRegIndex(opcodeField);
        int operandSize = _reflectionHelper.GetOperandSize(false, hasOperandSize32);
        return _reflectionHelper.BuildInstruction(_operation, operandSize, address, opcodeField, prefixes, regIndex);
    }
}