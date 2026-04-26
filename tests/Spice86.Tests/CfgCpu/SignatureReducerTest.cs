namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;

using Xunit;

public class SignatureReducerTest {
    private static readonly SegmentedAddress TestAddress = new(0x1000, 0);
    private const uint DefaultValueAddress = 0x10001;

    private static SignatureReducer CreateReducer() {
        InstructionReplacerRegistry replacerRegistry = new();
        return new SignatureReducer(replacerRegistry);
    }

    private static CfgInstruction CreateCompiledInstruction(ushort value) {
        CfgInstruction instruction = CreateInstruction(value, DefaultValueAddress);
        instruction.IncrementCompilationGeneration();
        return instruction;
    }

    private static InstructionField<ushort> CreateOpcodeField() {
        return CreateOpcodeFieldWithValue(0xB8);
    }

    private static InstructionField<ushort> CreateValueField(ushort value, uint physicalAddress) {
        return new InstructionField<ushort>(
            indexInInstruction: 1,
            length: 2,
            physicalAddress: physicalAddress,
            value: value,
            signatureValue: ImmutableList.Create<byte?>(null, null),
            final: false);
    }

    private static CfgInstruction CreateInstruction(ushort value, uint physicalAddress) {
        InstructionField<ushort> opcodeField = CreateOpcodeField();
        InstructionField<ushort> valueField = CreateValueField(value, physicalAddress);
        CfgInstruction instruction = new CfgInstruction(TestAddress, opcodeField, maxSuccessorsCount: 1);
        instruction.AddField(valueField);
        return instruction;
    }

    [Fact]
    public void ReduceToOne_PrefersUncompiledInstruction() {
        // Arrange
        SignatureReducer reducer = CreateReducer();

        CfgInstruction compiled = CreateCompiledInstruction(0x1234);
        CfgInstruction uncompiled = CreateInstruction(0x5678, DefaultValueAddress);

        // Act — pass compiled first to verify order doesn't matter
        CfgInstruction? result = reducer.ReduceToOne(compiled, uncompiled);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(uncompiled, "the uncompiled instruction should be the survivor");
    }

    [Fact]
    public void ReduceToOne_ThrowsWhenAllInstructionsAreCompiled() {
        // Arrange
        SignatureReducer reducer = CreateReducer();

        CfgInstruction first = CreateCompiledInstruction(0x1234);
        CfgInstruction second = CreateCompiledInstruction(0x5678);

        // Act & Assert
        FluentActions.Invoking(() => reducer.ReduceToOne(first, second))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*All instructions in reduction set have been compiled*");
    }

    [Fact]
    public void ReduceToOne_SurvivorHasUseValueFlippedForDivergingFields() {
        // Arrange
        SignatureReducer reducer = CreateReducer();

        CfgInstruction uncompiled = CreateInstruction(0x1234, DefaultValueAddress);
        CfgInstruction compiled = CreateCompiledInstruction(0x5678);

        // Act
        CfgInstruction? result = reducer.ReduceToOne(uncompiled, compiled);

        // Assert
        result.Should().NotBeNull();
        InstructionField<ushort> valueField = (InstructionField<ushort>)result!.FieldsInOrder[1];
        valueField.UseValue.Should().BeFalse("diverging value field should not use baked-in value");
    }

    [Fact]
    public void ReduceToOne_ReturnsNull_WhenFinalFieldsDiffer() {
        // Arrange — two instructions with different opcodes (final field)
        SignatureReducer reducer = CreateReducer();

        CfgInstruction movAx = CreateInstruction(0x1234, DefaultValueAddress);
        CfgInstruction movBx = CreateInstructionWithOpcode(0xBB, 0x5678, DefaultValueAddress);

        // Act
        CfgInstruction? result = reducer.ReduceToOne(movAx, movBx);

        // Assert — different final fields means they are different instructions, no reduction
        result.Should().BeNull("instructions with different final fields should not be reduced");
    }

    [Fact]
    public void ReduceToOne_UseValueStaysTrue_WhenNonFinalFieldsAreIdentical() {
        // Arrange — two instructions with same value (non-final field matches)
        SignatureReducer reducer = CreateReducer();

        CfgInstruction first = CreateInstruction(0x1234, DefaultValueAddress);
        CfgInstruction second = CreateInstruction(0x1234, DefaultValueAddress);

        // Act
        CfgInstruction? result = reducer.ReduceToOne(first, second);

        // Assert
        result.Should().NotBeNull();
        InstructionField<ushort> valueField = (InstructionField<ushort>)result!.FieldsInOrder[1];
        valueField.UseValue.Should().BeTrue("identical non-final fields should keep UseValue true");
        valueField.Value.Should().Be(0x1234);
    }

    [Fact]
    public void ReduceAll_ReturnsSeparateGroups_WhenFinalFieldsDiffer() {
        // Arrange — three instructions: two mov ax, one mov bx
        SignatureReducer reducer = CreateReducer();

        CfgInstruction movAx1 = CreateInstruction(0x1111, DefaultValueAddress);
        CfgInstruction movAx2 = CreateInstruction(0x2222, DefaultValueAddress);
        CfgInstruction movBx = CreateInstructionWithOpcode(0xBB, 0x3333, DefaultValueAddress);

        // Act
        IList<CfgInstruction> results = reducer.ReduceAll([movAx1, movAx2, movBx]);

        // Assert — two groups: one for mov ax (reduced to 1), one for mov bx (stays as 1)
        results.Should().HaveCount(2, "different final fields produce separate reduction groups");
        results.Should().ContainSingle(i => i.OpcodeField.Value == 0xB8, "mov ax group");
        results.Should().ContainSingle(i => i.OpcodeField.Value == 0xBB, "mov bx group");
    }

    [Fact]
    public void ReduceAll_SingleInstruction_ReturnsAsIs() {
        // Arrange
        SignatureReducer reducer = CreateReducer();

        CfgInstruction single = CreateInstruction(0x1234, DefaultValueAddress);

        // Act
        IList<CfgInstruction> results = reducer.ReduceAll([single]);

        // Assert
        results.Should().ContainSingle().Which.Should().BeSameAs(single);
    }

    [Fact]
    public void ReduceToOne_IdenticalInstructions_ReturnsOneOfThem() {
        // Arrange
        SignatureReducer reducer = CreateReducer();

        CfgInstruction first = CreateInstruction(0x1234, DefaultValueAddress);
        CfgInstruction second = CreateInstruction(0x1234, DefaultValueAddress);

        // Act
        CfgInstruction? result = reducer.ReduceToOne(first, second);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOneOf(first, second);
    }

    private static InstructionField<ushort> CreateOpcodeFieldWithValue(ushort opcode) {
        return new InstructionField<ushort>(
            indexInInstruction: 0,
            length: 1,
            physicalAddress: 0x10000,
            value: opcode,
            signatureValue: ImmutableList.Create<byte?>((byte)opcode),
            final: true);
    }

    private static CfgInstruction CreateInstructionWithOpcode(ushort opcode, ushort value, uint physicalAddress) {
        InstructionField<ushort> opcodeField = CreateOpcodeFieldWithValue(opcode);
        InstructionField<ushort> valueField = CreateValueField(value, physicalAddress);
        CfgInstruction instruction = new CfgInstruction(TestAddress, opcodeField, maxSuccessorsCount: 1);
        instruction.AddField(valueField);
        return instruction;
    }
}
