namespace Spice86.Tests.CfgCpu.ModRm;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.CPU.Registers;

using Xunit;

public class ModRmParserTest {
    private readonly ModRmHelper _modRmHelper = new();

    [Fact]
    public void Parse16Mod0R0Rm0() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(0, 0, 0));

        // Act
        ModRmContext context = parser.ParseNext(16, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(0, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.SEGMENT_OFFSET, context.MemoryAddressType);
        Assert.Null(context.SibContext);
        Assert.Equal(DisplacementType.ZERO, context.DisplacementType);
        Assert.Null(context.DisplacementField);
        Assert.Equal(ModRmOffsetType.BX_PLUS_SI, context.ModRmOffsetType);
        Assert.Null(context.ModRmOffsetField);
        Assert.Equal(SegmentRegisters.DsIndex, context.SegmentIndex);
    }

    [Fact]
    public void Parse16Mod0R0Rm110() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(0, 0, 0b110),
            // Offset Field
            0x11, 0x22
        );

        // Act
        ModRmContext context = parser.ParseNext(16, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(0, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0b110, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.SEGMENT_OFFSET, context.MemoryAddressType);
        Assert.Null(context.SibContext);
        Assert.Equal(DisplacementType.ZERO, context.DisplacementType);
        Assert.Null(context.DisplacementField);
        Assert.Equal(ModRmOffsetType.OFFSET_FIELD_16, context.ModRmOffsetType);
        Assert.Equal(0x2211, _modRmHelper.InstructionFieldValueRetriever.GetFieldValue(context.ModRmOffsetField!));
        Assert.Equal(SegmentRegisters.DsIndex, context.SegmentIndex);
    }

    [Fact]
    public void Parse16Mod1R0Rm110() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(1, 0, 0b110),
            //Displacement field
            0x11
        );

        // Act
        ModRmContext context = parser.ParseNext(16, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(1, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0b110, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.SEGMENT_OFFSET, context.MemoryAddressType);
        Assert.Null(context.SibContext);
        Assert.Equal(DisplacementType.UINT8, context.DisplacementType);
        Assert.Equal(0x11,
            _modRmHelper.InstructionFieldValueRetriever.GetFieldValue((InstructionField<byte>?)context.DisplacementField!));
        Assert.Equal(ModRmOffsetType.BP, context.ModRmOffsetType);
        Assert.Null(context.ModRmOffsetField);
        Assert.Equal(SegmentRegisters.SsIndex, context.SegmentIndex);
    }

    [Fact]
    public void Parse16Mod2R0Rm110() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(2, 0, 0b110),
            //Displacement field
            0x11, 0x22
        );

        // Act
        ModRmContext context = parser.ParseNext(16, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(2, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0b110, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.SEGMENT_OFFSET, context.MemoryAddressType);
        Assert.Null(context.SibContext);
        Assert.Equal(DisplacementType.UINT16, context.DisplacementType);
        Assert.Equal(0x2211,
            _modRmHelper.InstructionFieldValueRetriever.GetFieldValue((InstructionField<ushort>?)context.DisplacementField!));
        Assert.Equal(ModRmOffsetType.BP, context.ModRmOffsetType);
        Assert.Null(context.ModRmOffsetField);
        Assert.Equal(SegmentRegisters.SsIndex, context.SegmentIndex);
    }

    [Fact]
    public void Parse16Mod3R0Rm0() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(3, 0, 0));

        // Act
        ModRmContext context = parser.ParseNext(16, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(3, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.NONE, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.NONE, context.MemoryAddressType);
        Assert.Null(context.SibContext);
        Assert.Null(context.DisplacementType);
        Assert.Null(context.DisplacementField);
        Assert.Null(context.ModRmOffsetType);
        Assert.Null(context.ModRmOffsetField);
        Assert.Null(context.SegmentIndex);
    }


    [Fact]
    public void Parse32Mod0R0Rm0() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(_modRmHelper.GenerateModRm(0, 0, 0));

        // Act
        ModRmContext context = parser.ParseNext(32, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(0, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.SEGMENT_OFFSET, context.MemoryAddressType);
        Assert.Null(context.SibContext);
        Assert.Equal(DisplacementType.ZERO, context.DisplacementType);
        Assert.Null(context.DisplacementField);
        Assert.Equal(ModRmOffsetType.EAX, context.ModRmOffsetType);
        Assert.Null(context.ModRmOffsetField);
        Assert.Equal(SegmentRegisters.DsIndex, context.SegmentIndex);
    }

    [Fact]
    public void Parse32Mod0R0Rm100() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(0, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 3)
        );

        // Act
        ModRmContext context = parser.ParseNext(32, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(0, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0b100, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.SEGMENT_OFFSET, context.MemoryAddressType);
        Assert.Equal(DisplacementType.ZERO, context.DisplacementType);
        Assert.Null(context.DisplacementField);
        Assert.Equal(ModRmOffsetType.SIB, context.ModRmOffsetType);
        Assert.NotNull(context.SibContext);
        Assert.Equal(SegmentRegisters.DsIndex, context.SegmentIndex);
        SibContext sibContext = context.SibContext;
        // scale is x2 the amount in the SIB byte
        Assert.Equal(2, sibContext.Scale);
        Assert.Equal(SibBase.EBX, sibContext.SibBase);
        Assert.Null(sibContext.BaseField);
        Assert.Equal(SibIndex.EDX, sibContext.SibIndex);
    }

    [Fact]
    public void Parse32Mod0R0Rm100Base5() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(0, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 5),
            // Base field
            0x11, 0x22, 0x33, 0x44
        );

        // Act
        ModRmContext context = parser.ParseNext(32, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(0, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0b100, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.SEGMENT_OFFSET, context.MemoryAddressType);
        Assert.Equal(DisplacementType.ZERO, context.DisplacementType);
        Assert.Null(context.DisplacementField);
        Assert.Equal(ModRmOffsetType.SIB, context.ModRmOffsetType);
        Assert.NotNull(context.SibContext);
        Assert.Equal(SegmentRegisters.DsIndex, context.SegmentIndex);
        SibContext sibContext = context.SibContext;
        // scale is x2 the amount in the SIB byte
        Assert.Equal(2, sibContext.Scale);
        Assert.Equal(SibBase.BASE_FIELD_32, sibContext.SibBase);
        Assert.NotNull(sibContext.BaseField);
        Assert.Equal(0x44332211, (int)_modRmHelper.InstructionFieldValueRetriever.GetFieldValue(sibContext.BaseField));
        Assert.Equal(SibIndex.EDX, sibContext.SibIndex);
    }

    [Fact]
    public void Parse32Mod1R0Rm100() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(1, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 3),
            // Displacement
            0x11
        );

        // Act
        ModRmContext context = parser.ParseNext(32, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(1, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0b100, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.SEGMENT_OFFSET, context.MemoryAddressType);
        Assert.Equal(DisplacementType.UINT8, context.DisplacementType);
        Assert.Equal(0x11,
            _modRmHelper.InstructionFieldValueRetriever.GetFieldValue((InstructionField<byte>?)context.DisplacementField!));
        Assert.Equal(ModRmOffsetType.SIB, context.ModRmOffsetType);
        Assert.NotNull(context.SibContext);
        Assert.Equal(SegmentRegisters.DsIndex, context.SegmentIndex);
        SibContext sibContext = context.SibContext;
        // scale is x2 the amount in the SIB byte
        Assert.Equal(2, sibContext.Scale);
        Assert.Equal(SibBase.EBX, sibContext.SibBase);
        Assert.Null(sibContext.BaseField);
        Assert.Equal(SibIndex.EDX, sibContext.SibIndex);
    }

    [Fact]
    public void Parse32Mod2R0Rm100() {
        // Arrange
        ModRmParser parser = _modRmHelper.CreateModRmParser();
        int expectedBytesLength = _modRmHelper.WriteToMemory(
            _modRmHelper.GenerateModRm(2, 0, 0b100),
            _modRmHelper.GenerateSib(1, 2, 3),
            //Displacement field
            0x11, 0x22, 0x33, 0x44
        );

        // Act
        ModRmContext context = parser.ParseNext(32, null);

        // Assert
        Assert.Equal(expectedBytesLength, _modRmHelper.BytesLength(context.FieldsInOrder));
        Assert.Equal(2, (int)context.Mode);
        Assert.Equal(GeneralRegisters.AxIndex, context.RegisterIndex);
        Assert.Equal(0b100, (int)context.RegisterMemoryIndex);
        Assert.Equal(MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT, context.MemoryOffsetType);
        Assert.Equal(MemoryAddressType.SEGMENT_OFFSET, context.MemoryAddressType);
        Assert.Equal(DisplacementType.UINT32, context.DisplacementType);
        Assert.Equal(0x44332211,
            (int)_modRmHelper.InstructionFieldValueRetriever.GetFieldValue((InstructionField<uint>?)context.DisplacementField!));
        Assert.Equal(ModRmOffsetType.SIB, context.ModRmOffsetType);
        Assert.NotNull(context.SibContext);
        Assert.Equal(SegmentRegisters.DsIndex, context.SegmentIndex);
        SibContext sibContext = context.SibContext;
        // scale is x2 the amount in the SIB byte
        Assert.Equal(2, sibContext.Scale);
        Assert.Equal(SibBase.EBX, sibContext.SibBase);
        Assert.Null(sibContext.BaseField);
        Assert.Equal(SibIndex.EDX, sibContext.SibIndex);
    }
}