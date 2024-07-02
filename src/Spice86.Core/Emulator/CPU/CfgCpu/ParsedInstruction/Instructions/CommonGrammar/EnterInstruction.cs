namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public abstract class EnterInstruction : CfgInstruction {
    public EnterInstruction(SegmentedAddress address,
        InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes,
        InstructionField<ushort> storageField,
        InstructionField<byte> levelField) : base(address, opcodeField, prefixes) {
        StorageField = storageField;
        LevelField = levelField;
        FieldsInOrder.Add(StorageField);
        FieldsInOrder.Add(LevelField);
    }
    public InstructionField<ushort> StorageField { get; }
    public InstructionField<byte> LevelField { get; }
}