namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

using Xunit;

/// <summary>
/// Locks the <see cref="IrNodeTyper"/> helper-call mapping: each typed helper call node keeps its
/// type, an unrecognized statement call falls back to <see cref="OpaqueNode"/>, an unrecognized
/// call inside a condition throws, and temporaries are renamed with the instruction's local suffix.
/// </summary>
public class IrNodeTyperTest {
    private static readonly ConstantNode Ax = new(DataType.UINT16, 0x1234);

    private static MethodCallNode Call(string? propertyPath, string methodName, params IVisitableAstNode[] arguments) =>
        new(propertyPath, methodName, arguments);

    private static MethodCallValueNode CallValue(DataType dataType, string? propertyPath, string methodName,
        params IVisitableAstNode[] arguments) =>
        new(dataType, propertyPath, methodName, arguments);

    [Fact]
    public void AluUpdateFlagsStatementBecomesAluStatementNode() {
        IVisitableAstNode typed = IrNodeTyper.TypeStatement(
            new AluCallNode(AluOperation.UpdateFlags, BitWidth.BYTE_8, Ax));

        AluStatementNode alu = typed.Should().BeOfType<AluStatementNode>().Subject;
        alu.Call.MethodName.Should().Be("UpdateFlags");
        alu.Flags.Should().Be(FlagEffect.Writing(StatusFlags.Zero | StatusFlags.Parity | StatusFlags.Sign));
    }

    [Theory]
    [InlineData(BitWidth.WORD_16, "Push16")]
    [InlineData(BitWidth.DWORD_32, "Push32")]
    public void StackPushStaysAStackCallNode(BitWidth width, string methodName) {
        StackCallNode typed = IrNodeTyper.TypeStatement(new StackCallNode(StackOperation.Push, width, Ax))
            .Should().BeOfType<StackCallNode>().Subject;
        typed.MethodName.Should().Be(methodName);
        typed.Operation.Should().Be(StackOperation.Push);
        typed.Width.Should().Be(width);
    }

    [Theory]
    [InlineData(StackOperation.Pop, BitWidth.WORD_16)]
    [InlineData(StackOperation.Pop, BitWidth.DWORD_32)]
    [InlineData(StackOperation.Peek, BitWidth.WORD_16)]
    [InlineData(StackOperation.Peek, BitWidth.DWORD_32)]
    public void StackReadStaysAStackCallValueNode(StackOperation operation, BitWidth width) {
        StackCallValueNode typed = IrNodeTyper.TypeValue(new StackCallValueNode(operation, width))
            .Should().BeOfType<StackCallValueNode>().Subject;
        typed.Operation.Should().Be(operation);
        typed.Width.Should().Be(width);
    }

    [Theory]
    [InlineData(BitWidth.WORD_16)]
    [InlineData(BitWidth.DWORD_32)]
    public void StackLeaveStaysAStackCallNode(BitWidth width) {
        StackCallNode typed = IrNodeTyper.TypeStatement(new StackCallNode(StackOperation.Leave, width))
            .Should().BeOfType<StackCallNode>().Subject;
        typed.Operation.Should().Be(StackOperation.Leave);
        typed.Width.Should().Be(width);
    }

    [Theory]
    [InlineData(BitWidth.WORD_16)]
    [InlineData(BitWidth.DWORD_32)]
    public void StackPushAllStaysAStackCallNode(BitWidth width) {
        StackCallNode typed = IrNodeTyper.TypeStatement(new StackCallNode(StackOperation.PushAll, width, Ax))
            .Should().BeOfType<StackCallNode>().Subject;
        typed.Operation.Should().Be(StackOperation.PushAll);
        typed.Width.Should().Be(width);
    }

    [Theory]
    [InlineData(BitWidth.WORD_16)]
    [InlineData(BitWidth.DWORD_32)]
    public void StackPopAllStaysAStackCallNode(BitWidth width) {
        StackCallNode typed = IrNodeTyper.TypeStatement(new StackCallNode(StackOperation.PopAll, width))
            .Should().BeOfType<StackCallNode>().Subject;
        typed.Operation.Should().Be(StackOperation.PopAll);
        typed.Width.Should().Be(width);
    }

    [Theory]
    [InlineData(BitWidth.BYTE_8)]
    [InlineData(BitWidth.WORD_16)]
    [InlineData(BitWidth.DWORD_32)]
    public void PortReadStaysAPortReadCallNode(BitWidth width) {
        IrNodeTyper.TypeValue(new PortReadCallNode(width, Ax))
            .Should().BeOfType<PortReadCallNode>()
            .Which.Width.Should().Be(width);
    }

    [Theory]
    [InlineData(BitWidth.BYTE_8)]
    [InlineData(BitWidth.WORD_16)]
    [InlineData(BitWidth.DWORD_32)]
    public void PortWriteStaysAPortWriteCallNode(BitWidth width) {
        IrNodeTyper.TypeStatement(new PortWriteCallNode(width, Ax, Ax))
            .Should().BeOfType<PortWriteCallNode>()
            .Which.Width.Should().Be(width);
    }

    [Theory]
    [InlineData("Direction8")]
    [InlineData("Direction16")]
    [InlineData("Direction32")]
    public void DirectionStepMethodCallThrows(string methodName) {
        FluentActions.Invoking(() => IrNodeTyper.TypeValue(
                CallValue(DataType.UINT16, "State", methodName, Ax)))
            .Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ConditionalValueNodeKeepsItsShape() {
        ConditionalValueNode step = AstBuilder.DirectionStep(BitWidth.WORD_16);

        IrNodeTyper.TypeValue(step).Should().BeSameAs(step);
    }

    [Fact]
    public void BoundCheckStaysABoundCheckCallNode() {
        IrNodeTyper.TypeStatement(new BoundCheckCallNode(BitWidth.WORD_16, Ax, Ax, Ax))
            .Should().BeOfType<BoundCheckCallNode>();
    }

    [Theory]
    [InlineData(BitScanDirection.Forward, BitWidth.WORD_16)]
    [InlineData(BitScanDirection.Forward, BitWidth.DWORD_32)]
    [InlineData(BitScanDirection.Reverse, BitWidth.WORD_16)]
    [InlineData(BitScanDirection.Reverse, BitWidth.DWORD_32)]
    public void BitScanStaysABitScanCallNode(BitScanDirection direction, BitWidth width) {
        IrNodeTyper.TypeValue(new BitScanCallNode(DataType.UnsignedFromBitWidth(width), direction, Ax))
            .Should().BeOfType<BitScanCallNode>();
    }

    [Theory]
    [InlineData(CallDistance.Near, BitWidth.WORD_16)]
    [InlineData(CallDistance.Near, BitWidth.DWORD_32)]
    [InlineData(CallDistance.Far, BitWidth.WORD_16)]
    [InlineData(CallDistance.Far, BitWidth.DWORD_32)]
    public void ProcedureCallStaysAProcedureCallHelperNode(CallDistance distance, BitWidth width) {
        IrNodeTyper.TypeStatement(new ProcedureCallHelperNode(distance, width, Ax))
            .Should().BeOfType<ProcedureCallHelperNode>();
    }

    [Fact]
    public void InterruptCallStaysAnInterruptCallHelperNode() {
        IrNodeTyper.TypeStatement(new InterruptCallHelperNode(Ax))
            .Should().BeOfType<InterruptCallHelperNode>();
    }

    [Fact]
    public void CallbackStaysACallbackHelperNode() {
        IrNodeTyper.TypeStatement(new CallbackHelperNode(Ax))
            .Should().BeOfType<CallbackHelperNode>();
    }

    [Fact]
    public void CallbackMovedCsIpStaysACallbackMovedCsIpHelperNode() {
        IrNodeTyper.TypeValue(new CallbackMovedCsIpHelperNode(Ax))
            .Should().BeOfType<CallbackMovedCsIpHelperNode>();
    }

    [Fact]
    public void SelectorSignatureMatchesStaysASignatureMatchesHelperNode() {
        IrNodeTyper.TypeValue(new SignatureMatchesHelperNode(Ax))
            .Should().BeOfType<SignatureMatchesHelperNode>();
    }

    [Fact]
    public void AluOperationValueBecomesAluOperationNodeWithFlagEffect() {
        IVisitableAstNode typed = IrNodeTyper.TypeValue(
            new AluCallValueNode(DataType.UINT16, AluOperation.Add, BitWidth.WORD_16, Ax, Ax));

        AluOperationNode alu = typed.Should().BeOfType<AluOperationNode>().Subject;
        alu.Call.CallNode.MethodName.Should().Be("Add");
        alu.Flags.Should().Be(FlagEffect.Writing(StatusFlags.All));
    }

    [Fact]
    public void AluOperationNotInFlagTableBecomesOpaqueStatement() {
        MethodCallValueNode call = CallValue(DataType.UINT16, "Alu16", "NoSuchOp", Ax, Ax);
        VariableDeclarationNode statement = new(DataType.UINT16, "tmp", call);

        IVisitableAstNode typed = IrNodeTyper.TypeStatement(statement);

        typed.Should().BeOfType<OpaqueNode>()
            .Which.Original.Should().BeSameAs(statement);
    }

    [Theory]
    [InlineData("Alu16", "Add", "Alu16.Add")]
    [InlineData("Stack", "Pop16", "Stack.Pop16")]
    [InlineData(null, "In8", "In8")]
    public void UntypedHelperCallInValueThrows(string? propertyPath, string methodName, string expected) {
        MethodCallValueNode call = CallValue(DataType.UINT16, propertyPath, methodName, Ax);

        Action act = () => IrNodeTyper.TypeValue(call);

        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain(expected);
    }

    [Fact]
    public void UntypedHelperStatementCallBecomesOpaqueNode() {
        MethodCallNode call = Call("Stack", "Push16", Ax);

        IVisitableAstNode typed = IrNodeTyper.TypeStatement(call);

        typed.Should().BeOfType<OpaqueNode>()
            .Which.Original.Should().BeSameAs(call);
    }

    [Fact]
    public void UnknownStatementCallBecomesOpaqueNode() {
        MethodCallNode call = Call("Stack", "TotallyUnknownHelper", Ax);

        IVisitableAstNode typed = IrNodeTyper.TypeStatement(call);

        typed.Should().BeOfType<OpaqueNode>()
            .Which.Original.Should().BeSameAs(call);
    }

    [Fact]
    public void UnknownCallInConditionValueThrows() {
        MethodCallValueNode call = CallValue(DataType.BOOL, null, "UnknownCondition");

        Action act = () => IrNodeTyper.TypeValue(call);

        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("UnknownCondition");
    }

    [Fact]
    public void TemporariesAreRenamedWithLocalSuffix() {
        VariableDeclarationNode declaration = new(DataType.UINT16, "tmp",
            new VariableReferenceNode(DataType.UINT16, "other"));

        VariableDeclarationNode typed = IrNodeTyper.TypeStatement(declaration, "0006")
            .Should().BeOfType<VariableDeclarationNode>().Subject;

        typed.VariableName.Should().Be("tmp_0006");
        typed.Initializer.Should().BeOfType<VariableReferenceNode>()
            .Which.VariableName.Should().Be("other_0006");
    }

    [Fact]
    public void MethodScopedVariableNamesAreNotRenamed() {
        VariableDeclarationNode declaration = new(DataType.UINT16, "tmp",
            new MethodScopedVariableReferenceNode(DataType.UINT16, "methodVar"));

        VariableDeclarationNode typed = IrNodeTyper.TypeStatement(declaration, "0006")
            .Should().BeOfType<VariableDeclarationNode>().Subject;

        typed.Initializer.Should().BeOfType<MethodScopedVariableReferenceNode>()
            .Which.VariableName.Should().Be("methodVar");
    }
}
