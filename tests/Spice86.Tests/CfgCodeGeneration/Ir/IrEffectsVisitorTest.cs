namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;

using Xunit;

/// <summary>
/// Each row of the effects table on a minimal node, plus the composition rules.
/// </summary>
public sealed class IrEffectsVisitorTest {
    private static IrEffects Of(IVisitableAstNode node) => IrEffectsVisitor.Of(node, false);

    private static RegisterNode Ax() => new(DataType.UINT16, 0);
    private static RegisterNode Cx() => new(DataType.UINT16, 1);
    private static RegisterNode Bx() => new(DataType.UINT16, 3);
    private static SegmentRegisterNode Ds() => new(3);
    private static ConstantNode Constant() => new(DataType.UINT16, 0x1234);

    [Fact]
    public void RegisterRead_ReadsItsBytes() {
        // Arrange / Act
        IrEffects effects = Of(Ax());

        // Assert
        effects.Should().Be(new IrEffects(IrValues.Ax,
            IrValues.None, IrValues.None, IrTokens.None, IrTokens.None, false, false));
    }

    [Fact]
    public void SegmentRegisterRead_ReadsItsByte() {
        // Arrange / Act
        IrEffects effects = Of(Ds());

        // Assert
        effects.Reads.Should().Be(IrValues.Ds);
    }

    [Fact]
    public void AssignToRegister_WritesItsBytes() {
        // Arrange
        BinaryOperationNode assign = new(DataType.UINT16, Ax(), BinaryOperation.ASSIGN, Constant());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.Should().Be(new IrEffects(IrValues.None,
            IrValues.Ax, IrValues.None, IrTokens.None, IrTokens.None, false, false));
    }

    [Fact]
    public void AssignToSegmentedPointer_ReadsAddressAndWritesMemory() {
        // Arrange
        SegmentedPointerNode pointer = new(DataType.UINT16, Ds(), null, Constant());
        BinaryOperationNode assign = new(DataType.UINT16, pointer, BinaryOperation.ASSIGN, Ax());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.Reads.Should().Be(IrValues.Ds | IrValues.Ax);
        effects.TokenReads.Should().Be(IrTokens.None);
        effects.TokenWrites.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void SegmentedPointerRead_ReadsAddressAndMemory() {
        // Arrange
        SegmentedPointerNode pointer = new(DataType.UINT16, Ds(), null, Constant());

        // Act
        IrEffects effects = Of(pointer);

        // Assert
        effects.Reads.Should().Be(IrValues.Ds);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void AbsolutePointerRead_ReadsAddressAndMemory() {
        // Arrange
        AbsolutePointerNode pointer = new(DataType.UINT16, new ConstantNode(DataType.UINT32, 0x100));

        // Act
        IrEffects effects = Of(pointer);

        // Assert
        effects.Reads.Should().Be(IrValues.None);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void AssignToCpuFlag_WritesTheMaskedFlag() {
        // Arrange
        BinaryOperationNode assign = new(DataType.BOOL, new CpuFlagNode(Flags.Carry), BinaryOperation.ASSIGN, Constant());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.MustWrite.Should().Be(IrValues.Carry);
    }

    [Fact]
    public void AssignToDirectionMaskedCpuFlag_WritesDirection() {
        // Arrange
        BinaryOperationNode assign = new(DataType.BOOL, new CpuFlagNode(Flags.Direction), BinaryOperation.ASSIGN, Constant());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.MustWrite.Should().Be(IrValues.Direction);
    }

    [Fact]
    public void AssignToFlagRegister_WritesAllStatusFlagsAndDirection() {
        // Arrange
        BinaryOperationNode assign = new(DataType.UINT16, new FlagRegisterNode(DataType.UINT16), BinaryOperation.ASSIGN, Constant());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.MustWrite.Should().Be(IrValues.StatusFlags | IrValues.Direction);
    }

    [Fact]
    public void CpuFlagRead_ReadsTheMaskedFlag() {
        // Arrange / Act
        IrEffects effects = Of(new CpuFlagNode(Flags.Zero));

        // Assert
        effects.Reads.Should().Be(IrValues.Zero);
    }

    [Fact]
    public void FlagRegisterRead_ReadsAllStatusFlagsAndDirection() {
        // Arrange / Act
        IrEffects effects = Of(new FlagRegisterNode(DataType.UINT16));

        // Assert
        effects.Reads.Should().Be(IrValues.StatusFlags | IrValues.Direction);
    }

    [Fact]
    public void AssignToVariableReference_OnlyEvaluatesTheRightSide() {
        // Arrange
        BinaryOperationNode assign = new(DataType.UINT16, new VariableReferenceNode(DataType.UINT16, "tmp"),
            BinaryOperation.ASSIGN, Ax());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.Should().Be(new IrEffects(IrValues.Ax,
            IrValues.None, IrValues.None, IrTokens.None, IrTokens.None, false, false));
    }

    [Fact]
    public void InstructionField_ReadsMemoryWhenNotAConstant() {
        // Arrange
        InstructionField<ushort> field = new(0, 2, 0x10000, 0x1234,
            ImmutableList.Create<byte?>(0x34, 0x12), true) { UseValue = false };
        InstructionFieldNode node = new(DataType.UINT16, field, 0x1234);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void ConstantAndVariableNodes_HaveNoEffects() {
        // Arrange / Act / Assert
        Of(Constant()).Should().Be(IrEffects.None);
        Of(new VariableReferenceNode(DataType.UINT16, "tmp")).Should().Be(IrEffects.None);
        Of(new MethodScopedVariableReferenceNode(DataType.UINT16, "tmp")).Should().Be(IrEffects.None);
        Of(new InstructionStartNode("nop")).Should().Be(IrEffects.None);
        Of(new CommentNode("nop")).Should().Be(IrEffects.None);
    }

    [Fact]
    public void AluOperation_ReadsArgumentsAndAppliesFlags() {
        // Arrange
        MethodCallValueNode call = new(DataType.UINT16, "Alu16", "Add", Ax(), Constant());
        AluOperationNode node = new(call, FlagEffect.Writing(StatusFlags.All), AluOperation.Add,
            BitWidth.WORD_16);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Ax);
        effects.MustWrite.Should().Be(IrValues.StatusFlags);
    }

    [Fact]
    public void AluStatement_ReadsArgumentsAndAppliesFlags() {
        // Arrange
        MethodCallNode call = new("Alu8", "UpdateFlags", new RegisterNode(DataType.UINT8, 0));
        AluStatementNode node = new(call, new FlagEffect(StatusFlags.None,
            StatusFlags.Zero | StatusFlags.Parity | StatusFlags.Sign, StatusFlags.None),
            AluOperation.UpdateFlags, BitWidth.BYTE_8);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.MustWrite.Should().Be(IrValues.Zero | IrValues.Parity | IrValues.Sign);
    }

    [Fact]
    public void StackPush16_ReadsSpAndSs_WritesSpAndMemory() {
        // Arrange
        StackCallNode node = new(StackOperation.Push, BitWidth.WORD_16, Ax());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Ax | IrValues.Sp | IrValues.Ss);
        effects.MustWrite.Should().Be(IrValues.Sp);
        effects.TokenWrites.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPop32_WritesEsp() {
        // Arrange
        StackCallValueNode node = new(StackOperation.Pop, BitWidth.DWORD_32);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.MustWrite.Should().Be((IrValues)(0xFUL << 16));
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPeek_DoesNotWriteTheStackPointer() {
        // Arrange
        StackCallValueNode node = new(StackOperation.Peek, BitWidth.WORD_16, Constant());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.MustWrite.Should().Be(IrValues.None);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackLeave16_ReadsBpAndSs_WritesSpAndBp_ReadsMemory() {
        // Arrange
        StackCallNode node = new(StackOperation.Leave, BitWidth.WORD_16);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Bp | IrValues.Ss);
        effects.MustWrite.Should().Be(IrValues.Sp | IrValues.Bp);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPushAll16_ReadsArgsSpAndSs_WritesSpAndMemory() {
        // Arrange
        StackCallNode node = new(StackOperation.PushAll, BitWidth.WORD_16, Ax());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Ax | IrValues.Sp | IrValues.Ss);
        effects.MustWrite.Should().Be(IrValues.Sp);
        effects.TokenWrites.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPopAll16_ReadsSpAndSs_WritesPoppedRegistersAndSp_ReadsMemory() {
        // Arrange
        StackCallNode node = new(StackOperation.PopAll, BitWidth.WORD_16);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Sp | IrValues.Ss);
        effects.MustWrite.Should().Be(
            IrValues.Ax | IrValues.Cx | IrValues.Dx | IrValues.Bx
                | IrValues.Sp | IrValues.Bp | IrValues.Si | IrValues.Di);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPopAll32_Writes32BitRegistersAndEsp() {
        // Arrange
        StackCallNode node = new(StackOperation.PopAll, BitWidth.DWORD_32);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.MustWrite.Should().Be((IrValues)(0xF | (0xFUL << 4) | (0xFUL << 8) | (0xFUL << 12)
            | (0xFUL << 16) | (0xFUL << 20) | (0xFUL << 24) | (0xFUL << 28)));
    }

    [Fact]
    public void PortRead_ReadsArgumentsAndIo() {
        // Arrange
        PortReadCallNode node = new(BitWidth.WORD_16, Ax());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.TokenReads.Should().Be(IrTokens.Io);
        effects.TokenWrites.Should().Be(IrTokens.Io);
        effects.Reads.Should().Be(IrValues.Ax);
    }

    [Fact]
    public void PortWrite_ReadsArgumentsAndWritesIo() {
        // Arrange
        PortWriteCallNode node = new(BitWidth.BYTE_8, Constant(), Ax());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.TokenReads.Should().Be(IrTokens.Io);
        effects.TokenWrites.Should().Be(IrTokens.Io);
    }

    [Fact]
    public void DirectionStep_ReadsDirection() {
        // Arrange
        ConditionalValueNode node = AstBuilder.DirectionStep(BitWidth.WORD_16);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Direction);
    }

    [Fact]
    public void BoundCheck_OnlyReadsArguments() {
        // Arrange
        BoundCheckCallNode node = new(BitWidth.WORD_16, Ax(), Constant());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Ax);
        effects.TokenWrites.Should().Be(IrTokens.None);
    }

    [Fact]
    public void BitScan_OnlyReadsArguments() {
        // Arrange
        BitScanCallNode node = new(DataType.UINT16, BitScanDirection.Forward, Ax());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Ax);
    }

    [Fact]
    public void Division_ReadsOperandsAndWritesBothRegisters() {
        // Arrange
        DivisionNode node = new(new DivisionPattern(BitWidth.WORD_16, false, Ax(), Bx(), Ax(), Bx()));

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Ax | IrValues.Bx);
        effects.MustWrite.Should().Be(IrValues.Ax | IrValues.Bx);
    }

    [Fact]
    public void TupleSwap_ReadsAndWritesBothRegisters() {
        // Arrange
        TupleSwapNode node = new(Ax(), Bx());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(IrValues.Ax | IrValues.Bx);
        effects.MustWrite.Should().Be(IrValues.Ax | IrValues.Bx);
    }

    [Fact]
    public void OpaqueAndExternalCalls_AreUnknown() {
        // Arrange / Act / Assert
        Of(new OpaqueNode(Constant())).Should().Be(IrEffects.Unknown);
        Of(new ProcedureCallHelperNode(CallDistance.Near, BitWidth.WORD_16, Constant())).Should().Be(IrEffects.Unknown);
        Of(new CallbackMovedCsIpHelperNode()).Should().Be(IrEffects.Unknown);
        Of(new MethodCallNode(null, "AnythingGoes", Constant())).Should().Be(IrEffects.Unknown);
    }

    [Fact]
    public void TypedCallInsideAnOpaqueStatementHasUnknownEffects() {
        // Arrange: CX = Stack.Pop16() kept opaque; a typed pop alone must-writes SP
        OpaqueNode node = new(new BinaryOperationNode(DataType.UINT16, Cx(), BinaryOperation.ASSIGN,
            new StackCallValueNode(StackOperation.Pop, BitWidth.WORD_16)));

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.MustWrite.Should().Be(IrValues.Cx);
        effects.MayWrite.Should().Be(IrValues.All);
    }

    [Fact]
    public void SignatureMatchAndSpeculativeGuard_ReadMemory() {
        // Arrange / Act / Assert
        Of(new SignatureMatchesHelperNode())
            .Should().Be(ReadMemoryOnly());
        Of(new SpeculativeGuardNode(new SegmentedAddress(0x1000, 0x20), "sig")).Should().Be(ReadMemoryOnly());
    }

    [Fact]
    public void CheckExternalEvents_ReadsAndMayWriteHandlerVisibleRegistersButNoFlags() {
        // Arrange
        IrValues handlerRegisters = IrValues.Eax | IrValues.Ecx
            | IrValues.Edx | IrValues.Ebx | IrValues.Ebp | IrValues.Esi
            | IrValues.Edi | IrValues.Es | IrValues.Cs | IrValues.Ds
            | IrValues.Fs | IrValues.Gs;

        // Act
        IrEffects effects = Of(new CheckExternalEventsNode(new SegmentedAddress(0x1000, 0x20)));

        // Assert
        effects.Reads.Should().Be(handlerRegisters);
        effects.MustWrite.Should().Be(IrValues.None);
        effects.MayWrite.Should().Be(handlerRegisters);
        effects.TokenReads.Should().Be(IrTokens.All);
        effects.TokenWrites.Should().Be(IrTokens.All);
        effects.DeliversInterrupts.Should().BeTrue();
        effects.MayFault.Should().BeFalse();
        (effects.Reads & (IrValues.StatusFlags | IrValues.Direction)).Should().Be(IrValues.None);
        (effects.MayWrite & (IrValues.StatusFlags | IrValues.Direction)).Should().Be(IrValues.None);
        IrValues stackRegisters = IrValues.Sp | IrValues.Ss;
        (effects.Reads & stackRegisters).Should().Be(IrValues.None);
        (effects.MayWrite & stackRegisters).Should().Be(IrValues.None);
    }

    [Fact]
    public void ExitNodes_UseEverything() {
        // Arrange / Act / Assert
        Of(new ReturnNode(Ax())).Should().Be(IrEffects.ExitUse);
        Of(new ThrowNode(typeof(InvalidOperationException), "x")).Should().Be(IrEffects.ExitUse);
        Of(new CyclicJumpNode("m", 0)).Should().Be(IrEffects.ExitUse);
    }

    [Fact]
    public void FailAsUntested_ReadsOnlyItsMessageParts() {
        // Arrange / Act: the program aborts here, so nothing after it observes machine state.
        IrEffects effects = Of(new FailAsUntestedNode([
            UntestedMessagePart.Literal("failed"),
            UntestedMessagePart.Hole(Ax(), "X4")
        ]));

        // Assert
        effects.Should().Be(Of(Ax()));
    }

    [Fact]
    public void StructuralNode_Throws() {
        // Arrange
        BlockNode block = new();

        // Act
        Action act = () => IrEffectsVisitor.Of(block, false);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MayFault_IsSetOnTheResult() {
        // Arrange / Act
        IrEffects effects = IrEffectsVisitor.Of(Ax(), true);

        // Assert
        effects.MayFault.Should().BeTrue();
    }

    [Fact]
    public void Then_HidesAReadBehindAMustWrite() {
        // Arrange
        IrEffects write = new(IrValues.None, IrValues.Carry, IrValues.None, IrTokens.None, IrTokens.None, false, false);
        IrEffects read = new(IrValues.Carry | IrValues.Zero, IrValues.None, IrValues.None, IrTokens.None, IrTokens.None, false, false);

        // Act
        IrEffects combined = write.Then(read);

        // Assert
        combined.Reads.Should().Be(IrValues.Zero);
        combined.MustWrite.Should().Be(IrValues.Carry);
    }

    [Fact]
    public void Either_IntersectsMustWrites() {
        // Arrange
        IrEffects first = new(IrValues.None, IrValues.Carry | IrValues.Zero, IrValues.None, IrTokens.None, IrTokens.None, false, false);
        IrEffects second = new(IrValues.None, IrValues.Carry | IrValues.Sign, IrValues.None, IrTokens.None, IrTokens.None, false, false);

        // Act
        IrEffects either = IrEffects.Either(first, second);

        // Assert
        either.MustWrite.Should().Be(IrValues.Carry);
        either.MayWrite.Should().Be(IrValues.Carry | IrValues.Zero | IrValues.Sign);
    }

    private static IrEffects ReadMemoryOnly() =>
        new(IrValues.None, IrValues.None, IrValues.None, IrTokens.Memory, IrTokens.None, false, false);
}
