namespace Spice86.Tests.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

using System.Linq.Expressions;

using Xunit;

public class AstExpressionBuilderAluExpansionTest {
    private readonly Memory _memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
    private readonly State _state = new(CpuModel.INTEL_80286);
    private static readonly RegisterNode Ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
    private static readonly RegisterNode Bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
    private static readonly RegisterNode Cl = new(DataType.UINT8, (int)RegisterIndex.CxIndex);

    [Fact]
    public void AluCallIsAMethodCallWhenExpansionIsOff() {
        // Arrange
        AstExpressionBuilder builder = new(false);

        // Act
        Expression expression = AluCall("Add", Ax, Bx).Accept(builder);

        // Assert
        expression.Should().BeAssignableTo<MethodCallExpression>();
    }

    [Fact]
    public void AluCallIsABlockWhenExpansionIsOn() {
        // Arrange
        AstExpressionBuilder builder = new(true);

        // Act
        Expression expression = AluCall("Add", Ax, Bx).Accept(builder);

        // Assert
        expression.Should().BeAssignableTo<BlockExpression>();
        expression.Type.Should().Be(typeof(ushort));
    }

    [Fact]
    public void CallTheExpansionDoesNotCoverStaysAMethodCall() {
        // Arrange
        AstExpressionBuilder builder = new(true);

        // Act
        Expression expression = AluCall("Shld", Ax, Bx, Cl).Accept(builder);

        // Assert
        expression.Should().BeAssignableTo<MethodCallExpression>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpandedAddAssignsResultAndFlags(bool preferInterpretation) {
        // Arrange
        _state.AX = 0xFFFF;
        _state.BX = 0x0001;
        SetFlags(carry: false, parity: false, auxiliary: false, zero: false, sign: true, overflow: true);
        BinaryOperationNode program = new(DataType.UINT16, Ax, BinaryOperation.ASSIGN, AluCall("Add", Ax, Bx));

        // Act
        Execute(program, preferInterpretation);

        // Assert
        _state.AX.Should().Be(0x0000);
        AssertFlags(carry: true, parity: true, auxiliary: true, zero: true, sign: false, overflow: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpandedSubAsAStatementOnlyAssignsFlags(bool preferInterpretation) {
        // Arrange
        _state.AX = 0x0001;
        _state.BX = 0x0002;
        SetFlags(carry: false, parity: false, auxiliary: false, zero: true, sign: false, overflow: true);
        BlockNode program = new(AluCall("Sub", Ax, Bx));

        // Act
        Execute(program, preferInterpretation);

        // Assert
        _state.AX.Should().Be(0x0001);
        AssertFlags(carry: true, parity: true, auxiliary: true, zero: false, sign: true, overflow: false);
    }

    [Fact]
    public void OperandNamedLikeAnExpansionTempReadsTheEnclosingVariable() {
        // Arrange
        VariableDeclarationNode result = new(DataType.UINT16, "result", new ConstantNode(DataType.UINT16, 5));
        BinaryOperationNode assign = new(DataType.UINT16, Ax, BinaryOperation.ASSIGN,
            AluCall("Add", result.Reference, new ConstantNode(DataType.UINT16, 3)));
        BlockNode program = new(result, assign);

        // Act
        Execute(program, false);

        // Assert
        _state.AX.Should().Be(8);
    }

    private static AluCallValueNode AluCall(string operation, params ValueNode[] arguments) =>
        new(DataType.UINT16, Enum.Parse<AluOperation>(operation), BitWidth.WORD_16,
            arguments.Cast<IVisitableAstNode>().ToArray());

    private void Execute(IVisitableAstNode program, bool preferInterpretation) {
        AstExpressionBuilder builder = new(true);
        Action<State, Memory> action = builder.ToAction(program.Accept(builder)).Compile(preferInterpretation);
        action(_state, _memory);
    }

    private void SetFlags(bool carry, bool parity, bool auxiliary, bool zero, bool sign, bool overflow) {
        _state.CarryFlag = carry;
        _state.ParityFlag = parity;
        _state.AuxiliaryFlag = auxiliary;
        _state.ZeroFlag = zero;
        _state.SignFlag = sign;
        _state.OverflowFlag = overflow;
    }

    private void AssertFlags(bool carry, bool parity, bool auxiliary, bool zero, bool sign, bool overflow) {
        _state.CarryFlag.Should().Be(carry);
        _state.ParityFlag.Should().Be(parity);
        _state.AuxiliaryFlag.Should().Be(auxiliary);
        _state.ZeroFlag.Should().Be(zero);
        _state.SignFlag.Should().Be(sign);
        _state.OverflowFlag.Should().Be(overflow);
    }
}
