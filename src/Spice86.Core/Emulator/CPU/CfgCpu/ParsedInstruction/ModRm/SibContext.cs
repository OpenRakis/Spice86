namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

public class SibContext {
    public InstructionField<byte> Sib { get; }
    public int Scale { get; }
    public int IndexRegister { get; }
    public int BaseRegister { get; }
    public SibBase SibBase { get; }
    public InstructionField<uint>? BaseField { get; }
    public SibIndex SibIndex { get; }
    
    public List<FieldWithValue> FieldsInOrder { get; } = new();

    public SibContext(
        InstructionField<byte> sib,
        int scale,
        int indexRegister,
        int baseRegister,
        SibBase sibBase,
        InstructionField<uint>? baseField,
        SibIndex sibIndex) {
        Sib = sib;
        Scale = scale;
        IndexRegister = indexRegister;
        BaseRegister = baseRegister;
        SibBase = sibBase;
        BaseField = baseField;
        SibIndex = sibIndex;
        FieldsInOrder.Add(Sib);
        if (BaseField != null) {
            FieldsInOrder.Add(BaseField);
        }
    }
}