namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

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

    [Theory]
    [InlineData(InstructionOperation.FLDCW)]
    [InlineData(InstructionOperation.FNINIT)]
    [InlineData(InstructionOperation.FNDISI)]
    public void FpuControl_SerializesOpaqueStateWithoutClobberingRegistersOrFlags(InstructionOperation operation) {
        // Arrange
        IVisitableAstNode[] arguments = operation == InstructionOperation.FLDCW ? [Constant()] : [];
        FpuControlCallNode call = new(operation, arguments);

        // Act
        IVisitableAstNode typed = IrNodeTyper.TypeStatement(call);
        IrEffects effects = Of(typed);

        // Assert
        typed.Should().BeOfType<FpuControlCallNode>();
        effects.Should().Be(new IrEffects(CpuValues.None, CpuValues.None, CpuValues.None,
            IrTokens.All, IrTokens.All, false, false));
    }

    [Fact]
    public void FpuControlWord_ReadsOpaqueStateWithoutClobberingRegistersOrFlags() {
        // Arrange
        FpuControlWordNode call = new();

        // Act
        ValueNode typed = IrNodeTyper.TypeValue(call);
        IrEffects effects = Of(typed);

        // Assert
        typed.Should().BeOfType<FpuControlWordNode>();
        effects.Should().Be(new IrEffects(CpuValues.None, CpuValues.None, CpuValues.None,
            IrTokens.All, IrTokens.None, false, false));
    }

    [Fact]
    public void RegisterRead_ReadsItsBytes() {
        // Arrange / Act
        IrEffects effects = Of(Ax());

        // Assert
        effects.Should().Be(new IrEffects(CpuValues.Ax,
            CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.None, false, false));
    }

    [Fact]
    public void SegmentRegisterRead_ReadsItsByte() {
        // Arrange / Act
        IrEffects effects = Of(Ds());

        // Assert
        effects.Reads.Should().Be(CpuValues.Ds);
    }

    [Fact]
    public void AssignToRegister_WritesItsBytes() {
        // Arrange
        BinaryOperationNode assign = new(DataType.UINT16, Ax(), BinaryOperation.ASSIGN, Constant());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.Should().Be(new IrEffects(CpuValues.None,
            CpuValues.Ax, CpuValues.None, IrTokens.None, IrTokens.None, false, false));
    }

    [Fact]
    public void AssignToSegmentedPointer_ReadsAddressAndWritesMemory() {
        // Arrange
        SegmentedPointerNode pointer = new(DataType.UINT16, Ds(), null, Constant());
        BinaryOperationNode assign = new(DataType.UINT16, pointer, BinaryOperation.ASSIGN, Ax());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.Reads.Should().Be(CpuValues.Ds | CpuValues.Ax);
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
        effects.Reads.Should().Be(CpuValues.Ds);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void AbsolutePointerRead_ReadsAddressAndMemory() {
        // Arrange
        AbsolutePointerNode pointer = new(DataType.UINT16, new ConstantNode(DataType.UINT32, 0x100));

        // Act
        IrEffects effects = Of(pointer);

        // Assert
        effects.Reads.Should().Be(CpuValues.None);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void AssignToCpuFlag_WritesTheMaskedFlag() {
        // Arrange
        BinaryOperationNode assign = new(DataType.BOOL, new CpuFlagNode(Flags.Carry), BinaryOperation.ASSIGN, Constant());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.MustWrite.Should().Be(CpuValues.Carry);
    }

    [Fact]
    public void AssignToDirectionMaskedCpuFlag_WritesDirection() {
        // Arrange
        BinaryOperationNode assign = new(DataType.BOOL, new CpuFlagNode(Flags.Direction), BinaryOperation.ASSIGN, Constant());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.MustWrite.Should().Be(CpuValues.Direction);
    }

    [Fact]
    public void AssignToFlagRegister_WritesAllStatusFlagsAndDirection() {
        // Arrange
        BinaryOperationNode assign = new(DataType.UINT16, new FlagRegisterNode(DataType.UINT16), BinaryOperation.ASSIGN, Constant());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.MustWrite.Should().Be(CpuValues.StatusFlags | CpuValues.Direction);
    }

    [Fact]
    public void CpuFlagRead_ReadsTheMaskedFlag() {
        // Arrange / Act
        IrEffects effects = Of(new CpuFlagNode(Flags.Zero));

        // Assert
        effects.Reads.Should().Be(CpuValues.Zero);
    }

    [Fact]
    public void FlagRegisterRead_ReadsAllStatusFlagsAndDirection() {
        // Arrange / Act
        IrEffects effects = Of(new FlagRegisterNode(DataType.UINT16));

        // Assert
        effects.Reads.Should().Be(CpuValues.StatusFlags | CpuValues.Direction);
    }

    [Fact]
    public void AssignToVariableReference_OnlyEvaluatesTheRightSide() {
        // Arrange
        BinaryOperationNode assign = new(DataType.UINT16, new VariableReferenceNode(DataType.UINT16, "tmp"),
            BinaryOperation.ASSIGN, Ax());

        // Act
        IrEffects effects = Of(assign);

        // Assert
        effects.Should().Be(new IrEffects(CpuValues.Ax,
            CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.None, false, false));
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
        AluCallValueNode call = new(DataType.UINT16, AluOperation.Add, BitWidth.WORD_16, Ax(), Constant());
        AluOperationNode node = new(call, FlagEffect.Writing(StatusFlags.All));

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(CpuValues.Ax);
        effects.MustWrite.Should().Be(CpuValues.StatusFlags);
    }

    [Fact]
    public void AluStatement_ReadsArgumentsAndAppliesFlags() {
        // Arrange
        AluCallNode call = new(AluOperation.UpdateFlags, BitWidth.BYTE_8,
            new RegisterNode(DataType.UINT8, 0));
        AluStatementNode node = new(call, new FlagEffect(StatusFlags.None,
            StatusFlags.Zero | StatusFlags.Parity | StatusFlags.Sign, StatusFlags.None));

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.MustWrite.Should().Be(CpuValues.Zero | CpuValues.Parity | CpuValues.Sign);
    }

    [Fact]
    public void StackPush16_ReadsSpAndSs_WritesSpAndMemory() {
        // Arrange
        StackCallNode node = new(StackOperation.Push, BitWidth.WORD_16, Ax());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(CpuValues.Ax | CpuValues.Sp | CpuValues.Ss);
        effects.MustWrite.Should().Be(CpuValues.Sp);
        effects.TokenWrites.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPop32_WritesEsp() {
        // Arrange
        StackCallValueNode node = new(StackOperation.Pop, BitWidth.DWORD_32);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.MustWrite.Should().Be(CpuValues.Esp);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPeek_DoesNotWriteTheStackPointer() {
        // Arrange
        StackCallValueNode node = new(StackOperation.Peek, BitWidth.WORD_16, Constant());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.MustWrite.Should().Be(CpuValues.None);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackLeave16_ReadsBpAndSs_WritesSpAndBp_ReadsMemory() {
        // Arrange
        StackCallNode node = new(StackOperation.Leave, BitWidth.WORD_16);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(CpuValues.Bp | CpuValues.Ss);
        effects.MustWrite.Should().Be(CpuValues.Sp | CpuValues.Bp);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPushAll16_ReadsArgsSpAndSs_WritesSpAndMemory() {
        // Arrange
        StackCallNode node = new(StackOperation.PushAll, BitWidth.WORD_16, Ax());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(CpuValues.Ax | CpuValues.Sp | CpuValues.Ss);
        effects.MustWrite.Should().Be(CpuValues.Sp);
        effects.TokenWrites.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPopAll16_ReadsSpAndSs_WritesPoppedRegistersAndSp_ReadsMemory() {
        // Arrange
        StackCallNode node = new(StackOperation.PopAll, BitWidth.WORD_16);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(CpuValues.Sp | CpuValues.Ss);
        effects.MustWrite.Should().Be(
            CpuValues.Ax | CpuValues.Cx | CpuValues.Dx | CpuValues.Bx
                | CpuValues.Sp | CpuValues.Bp | CpuValues.Si | CpuValues.Di);
        effects.TokenReads.Should().Be(IrTokens.Memory);
    }

    [Fact]
    public void StackPopAll32_Writes32BitRegistersAndEsp() {
        // Arrange
        StackCallNode node = new(StackOperation.PopAll, BitWidth.DWORD_32);

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.MustWrite.Should().Be(CpuValues.Eax | CpuValues.Ecx | CpuValues.Edx
            | CpuValues.Ebx | CpuValues.Esp | CpuValues.Ebp | CpuValues.Esi | CpuValues.Edi);
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
        effects.Reads.Should().Be(CpuValues.Ax);
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
        effects.Reads.Should().Be(CpuValues.Direction);
    }

    [Fact]
    public void BoundCheck_OnlyReadsArguments() {
        // Arrange
        BoundCheckCallNode node = new(BitWidth.WORD_16, Ax(), Constant());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(CpuValues.Ax);
        effects.TokenWrites.Should().Be(IrTokens.None);
    }

    [Fact]
    public void BitScan_OnlyReadsArguments() {
        // Arrange
        BitScanCallNode node = new(DataType.UINT16, BitScanDirection.Forward, Ax());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(CpuValues.Ax);
    }

    [Fact]
    public void Division_ReadsOperandsAndWritesBothRegisters() {
        // Arrange
        DivisionNode node = new(new DivisionPattern(BitWidth.WORD_16, false, Ax(), Bx(), Ax(), Bx()));

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(CpuValues.Ax | CpuValues.Bx);
        effects.MustWrite.Should().Be(CpuValues.Ax | CpuValues.Bx);
    }

    [Fact]
    public void TupleSwap_ReadsAndWritesBothRegisters() {
        // Arrange
        TupleSwapNode node = new(Ax(), Bx());

        // Act
        IrEffects effects = Of(node);

        // Assert
        effects.Reads.Should().Be(CpuValues.Ax | CpuValues.Bx);
        effects.MustWrite.Should().Be(CpuValues.Ax | CpuValues.Bx);
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
        effects.MustWrite.Should().Be(CpuValues.Cx);
        effects.MayWrite.Should().Be(CpuValues.All);
    }

    [Fact]
    public void SignatureMatchAndSpeculativeGuard_ReadMemory() {
        // Arrange / Act / Assert
        Of(new SignatureMatchesHelperNode())
            .Should().Be(ReadMemoryOnly());
        Of(new SpeculativeGuardNode(new SegmentedAddress(0x1000, 0x20), ["sig"])).Should().Be(ReadMemoryOnly());
    }

    [Fact]
    public void CheckExternalEvents_ReadsAndMayWriteHandlerVisibleRegistersButNoFlags() {
        // Arrange
        CpuValues handlerRegisters = CpuValues.Eax | CpuValues.Ecx
            | CpuValues.Edx | CpuValues.Ebx | CpuValues.Ebp | CpuValues.Esi
            | CpuValues.Edi | CpuValues.Es | CpuValues.Cs | CpuValues.Ds
            | CpuValues.Fs | CpuValues.Gs;

        // Act
        IrEffects effects = Of(new CheckExternalEventsNode(new SegmentedAddress(0x1000, 0x20), HardwareInterruptEffectBuilder.Conservative));

        // Assert
        effects.Reads.Should().Be(handlerRegisters);
        effects.MustWrite.Should().Be(CpuValues.None);
        effects.MayWrite.Should().Be(handlerRegisters);
        effects.TokenReads.Should().Be(IrTokens.All);
        effects.TokenWrites.Should().Be(IrTokens.All);
        effects.DeliversInterrupts.Should().BeTrue();
        effects.MayFault.Should().BeFalse();
        (effects.Reads & (CpuValues.StatusFlags | CpuValues.Direction)).Should().Be(CpuValues.None);
        (effects.MayWrite & (CpuValues.StatusFlags | CpuValues.Direction)).Should().Be(CpuValues.None);
        CpuValues stackRegisters = CpuValues.Sp | CpuValues.Ss;
        (effects.Reads & stackRegisters).Should().Be(CpuValues.None);
        (effects.MayWrite & stackRegisters).Should().Be(CpuValues.None);
    }

    [Fact]
    public void ExitNodes_UseEverything() {
        // Arrange / Act / Assert
        Of(new ReturnNode(Ax())).Should().Be(IrEffects.ExitUse);
        Of(new ThrowNode(typeof(InvalidOperationException), "x")).Should().Be(IrEffects.ExitUse);
        Of(new CyclicJumpNode("m", 0, false)).Should().Be(IrEffects.ExitUse);
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
        IrEffects write = new(CpuValues.None, CpuValues.Carry, CpuValues.None, IrTokens.None, IrTokens.None, false, false);
        IrEffects read = new(CpuValues.Carry | CpuValues.Zero, CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.None, false, false);

        // Act
        IrEffects combined = write.Then(read);

        // Assert
        combined.Reads.Should().Be(CpuValues.Zero);
        combined.MustWrite.Should().Be(CpuValues.Carry);
    }

    [Fact]
    public void Either_IntersectsMustWrites() {
        // Arrange
        IrEffects first = new(CpuValues.None, CpuValues.Carry | CpuValues.Zero, CpuValues.None, IrTokens.None, IrTokens.None, false, false);
        IrEffects second = new(CpuValues.None, CpuValues.Carry | CpuValues.Sign, CpuValues.None, IrTokens.None, IrTokens.None, false, false);

        // Act
        IrEffects either = IrEffects.Either(first, second);

        // Assert
        either.MustWrite.Should().Be(CpuValues.Carry);
        either.MayWrite.Should().Be(CpuValues.Carry | CpuValues.Zero | CpuValues.Sign);
    }

    private static IrEffects ReadMemoryOnly() =>
        new(CpuValues.None, CpuValues.None, CpuValues.None, IrTokens.Memory, IrTokens.None, false, false);
}
