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
        AddField(StorageField);
        AddField(LevelField);
    }
    public InstructionField<ushort> StorageField { get; }
    public InstructionField<byte> LevelField { get; }
}