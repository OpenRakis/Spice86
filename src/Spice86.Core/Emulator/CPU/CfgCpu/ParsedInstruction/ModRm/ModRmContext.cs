namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

using Spice86.Shared.Emulator.Memory;

public class ModRmContext {
    
    public InstructionField<byte> ModRmField { get; }
    public uint Mode { get; }
    public int RegisterIndex { get; }
    public int RegisterMemoryIndex { get; }
    public BitWidth AddressSize;
    public MemoryOffsetType MemoryOffsetType { get; }
    public MemoryAddressType MemoryAddressType { get; }
    public SibContext? SibContext { get; }
    public DisplacementType? DisplacementType { get; }
    public FieldWithValue? DisplacementField { get; }
    public ModRmOffsetType? ModRmOffsetType { get; }
    public InstructionField<ushort>? ModRmOffsetField { get; }
    public int? SegmentIndex { get; }

    public List<FieldWithValue> FieldsInOrder { get; } = new();

    public ModRmContext(
        InstructionField<byte> modRmField,
        uint mode,
        int registerIndex,
        int registerMemoryIndex,
        BitWidth addressSize,
        MemoryOffsetType memoryOffsetType,
        MemoryAddressType memoryAddressType,
        SibContext? sibContext,
        DisplacementType? displacementType,
        FieldWithValue? displacementField,
        ModRmOffsetType? modRmOffsetType,
        InstructionField<ushort>? modRmOffsetField,
        int? segmentIndex
    ) {
        ModRmField = modRmField;
        Mode = mode;
        RegisterIndex = registerIndex;
        RegisterMemoryIndex = registerMemoryIndex;
        AddressSize = addressSize;
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