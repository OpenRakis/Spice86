namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

public class ModRmContext {
    
    public InstructionField<byte> ModRmField { get; }
    public int Mode { get; }
    public uint RegisterIndex { get; }
    public uint RegisterMemoryIndex { get; }
    public MemoryOffsetType MemoryOffsetType { get; }
    public MemoryAddressType MemoryAddressType { get; }
    public SibContext? SibContext { get; }
    public DisplacementType? DisplacementType { get; }
    public FieldWithValue? DisplacementField { get; }
    public ModRmOffsetType? ModRmOffsetType { get; }
    public InstructionField<ushort>? ModRmOffsetField { get; }
    public uint? SegmentIndex { get; }

    public List<FieldWithValue> FieldsInOrder { get; } = new();

    public ModRmContext(
        InstructionField<byte> modRmField,
        int mode,
        uint registerIndex,
        uint registerMemoryIndex,
        MemoryOffsetType memoryOffsetType,
        MemoryAddressType memoryAddressType,
        SibContext? sibContext,
        DisplacementType? displacementType,
        FieldWithValue? displacementField,
        ModRmOffsetType? modRmOffsetType,
        InstructionField<ushort>? modRmOffsetField,
        uint? segmentIndex
    ) {
        ModRmField = modRmField;
        Mode = mode;
        RegisterIndex = registerIndex;
        RegisterMemoryIndex = registerMemoryIndex;
        MemoryOffsetType = memoryOffsetType;
        MemoryAddressType = memoryAddressType;
        SibContext = sibContext;
        DisplacementType = displacementType;
        DisplacementField = displacementField;
        ModRmOffsetType = modRmOffsetType;
        ModRmOffsetField = modRmOffsetField;
        SegmentIndex = segmentIndex;

        // Order of the bytes in modrm context:
        // First ModRM byte
        FieldsInOrder.Add(ModRmField);
        // Then SIB byte and its friends
        if (SibContext != null) {
            FieldsInOrder.AddRange(SibContext.FieldsInOrder);
        }
        // Then displacement
        if (DisplacementField != null) {
            FieldsInOrder.Add(DisplacementField);
        }
        // Then offset
        if (ModRmOffsetField != null) {
            FieldsInOrder.Add(ModRmOffsetField);
        }
    }

}