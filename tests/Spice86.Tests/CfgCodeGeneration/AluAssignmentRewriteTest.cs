namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Xunit;

public class AluAssignmentRewriteTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
    private static readonly RegisterNode Bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
    private static readonly RegisterNode Cx = new(DataType.UINT16, (int)RegisterIndex.CxIndex);
    private static readonly MoveIpNextNode MoveIp = new(new ConstantNode(DataType.UINT16, 0x0102));

    private static readonly ushort[] EdgeValues16 = [0x0000, 0x0001, 0x0002, 0x000F, 0x0010, 0x007F, 0x0080, 0x00FF,
        0x0100, 0x7FFE, 0x7FFF, 0x8000, 0x8001, 0xFFFE, 0xFFFF];

    [Fact]
    public void AssignmentWithDeadFlagsBecomesPlainArithmetic() {
        // Arrange
        BlockNode instruction = new(Assign(Ax, AluCall("Add", Ax, Bx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.None);

        // Assert
        StatementsOf(rewritten).Should().Equal(
            Assign(Ax, new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.PLUS, Bx)), MoveIp);
    }

    [Fact]
    public void LiveCarryDoesNotKeepAnIncrement() {
        // Arrange
        BlockNode instruction = new(Assign(Cx, AluCall("Inc", Cx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Carry);

        // Assert
        StatementsOf(rewritten).Should().Equal(
            Assign(Cx, new BinaryOperationNode(DataType.UINT16, Cx, BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 1))),
            MoveIp);
    }

    [Fact]
    public void MemoryDestinationIsReadOnceInTheSum() {
        // Arrange
        SegmentedPointerNode memory = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0), null,
            new ConstantNode(DataType.UINT16, 0x0010));
        BlockNode instruction = new(Assign(memory, AluCall("Add", memory, Ax)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.None);

        // Assert
        StatementsOf(rewritten).Should().Equal(
            Assign(memory, new BinaryOperationNode(DataType.UINT16, memory, BinaryOperation.PLUS, Ax)), MoveIp);
    }

    [Fact]
    public void CompareOfRegistersWithDeadFlagsDisappears() {
        // Arrange
        BlockNode compare = new(AluCall("Sub", Ax, Bx), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(compare, StatusFlags.None);

        // Assert
        StatementsOf(rewritten).Should().Equal(MoveIp);
    }

    [Fact]
    public void TestOfARegisterWithAConstantWithDeadFlagsDisappears() {
        // Arrange
        BlockNode test = new(AluCall("And", Ax, new ConstantNode(DataType.UINT16, 0x0001)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(test, StatusFlags.None);

        // Assert
        StatementsOf(rewritten).Should().Equal(MoveIp);
    }

    [Theory]
    [InlineData(StatusFlags.Zero)]
    [InlineData(StatusFlags.Carry)]
    [InlineData(StatusFlags.Parity)]
    private void CompareWithALiveFlagKeepsTheCall(StatusFlags live) {
        // Arrange
        BlockNode compare = new(AluCall("Sub", Ax, Bx), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(compare, live);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void CompareWithAMemoryOperandKeepsTheCall() {
        // Arrange
        SegmentedPointerNode memory = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0), null,
            new ConstantNode(DataType.UINT16, 0x0100));
        BlockNode compare = new(AluCall("Sub", Ax, memory), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(compare, StatusFlags.None);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void CompareWithACallOperandKeepsTheCall() {
        // Arrange
        MethodCallValueNode peek = new(DataType.UINT16, "Stack", "Peek16", new ConstantNode(DataType.UINT32, 0));
        BlockNode compare = new(AluCall("Sub", peek, Ax), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(compare, StatusFlags.None);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void UnusedCallTheExpansionDoesNotCoverKeepsTheCall() {
        // Arrange
        BlockNode instruction = new(AluCall("Adc", Ax, Bx), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.None);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void InstructionWithMoreStatementsIsNotRewritten() {
        // Arrange
        ConstantNode zero = new(DataType.UINT16, 0);
        BinaryOperationNode setCarry = new(DataType.BOOL, new CpuFlagNode((ushort)StatusFlags.Carry), BinaryOperation.ASSIGN,
            new BinaryOperationNode(DataType.BOOL, Ax, BinaryOperation.NOT_EQUAL, zero));
        BlockNode negate = new(Assign(Ax, AluCall("Sub", zero, Ax)), setCarry, MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(negate, StatusFlags.None);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void CallTheExpansionDoesNotCoverIsNotRewritten() {
        // Arrange
        BlockNode instruction = new(Assign(Ax, AluCall("Adc", Ax, Bx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.None);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void OperandTheCleanupKeepsAsATempIsNotRewritten() {
        // Arrange
        MethodCallValueNode peek = new(DataType.UINT16, "Stack", "Peek16", new ConstantNode(DataType.UINT32, 0));
        BlockNode instruction = new(Assign(Ax, AluCall("Add", peek, Ax)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.None);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void ZeroLiveAfterAddIsTestedOnTheDestination() {
        // Arrange
        BlockNode instruction = new(Assign(Ax, AluCall("Add", Ax, Bx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Zero);

        // Assert
        StatementsOf(rewritten).Should().Equal(
            Assign(Ax, new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.PLUS, Bx)),
            FlagAssignment(StatusFlags.Zero, new BinaryOperationNode(DataType.BOOL, Ax, BinaryOperation.EQUAL,
                new ConstantNode(DataType.UINT16, 0))),
            MoveIp);
    }

    [Fact]
    public void SignLiveAfterAddIsTestedOnTheDestination() {
        // Arrange
        BlockNode instruction = new(Assign(Ax, AluCall("Add", Ax, Bx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Sign);

        // Assert
        StatementsOf(rewritten).Should().Equal(
            Assign(Ax, new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.PLUS, Bx)),
            FlagAssignment(StatusFlags.Sign, new BinaryOperationNode(DataType.BOOL, new TypeConversionNode(DataType.INT16, Ax),
                BinaryOperation.LESS_THAN, new ConstantNode(DataType.INT16, 0))),
            MoveIp);
    }

    [Fact]
    public void CarryLiveAfterSubIsComputedBeforeTheAssignment() {
        // Arrange
        BlockNode instruction = new(Assign(Ax, AluCall("Sub", Ax, Bx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Carry);

        // Assert
        StatementsOf(rewritten).Should().Equal(
            FlagAssignment(StatusFlags.Carry, new BinaryOperationNode(DataType.BOOL, Ax, BinaryOperation.LESS_THAN, Bx)),
            Assign(Ax, new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.MINUS, Bx)),
            MoveIp);
    }

    [Fact]
    public void CarryLiveAfterXorIsTheConstantFalse() {
        // Arrange
        BlockNode instruction = new(Assign(Ax, AluCall("Xor", Ax, Bx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Carry);

        // Assert
        StatementsOf(rewritten).Should().Equal(
            FlagAssignment(StatusFlags.Carry, new ConstantNode(DataType.BOOL, 0)),
            Assign(Ax, new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.BITWISE_XOR, Bx)),
            MoveIp);
    }

    [Fact]
    public void OverflowLiveAfterDecIsTestedOnTheDestination() {
        // Arrange
        BlockNode instruction = new(Assign(Cx, AluCall("Dec", Cx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Overflow);

        // Assert
        StatementsOf(rewritten).Should().Equal(
            Assign(Cx, new BinaryOperationNode(DataType.UINT16, Cx, BinaryOperation.MINUS, new ConstantNode(DataType.UINT16, 1))),
            FlagAssignment(StatusFlags.Overflow, new BinaryOperationNode(DataType.BOOL, Cx, BinaryOperation.EQUAL,
                new ConstantNode(DataType.UINT16, 0x7FFF))),
            MoveIp);
    }

    [Theory]
    [InlineData(StatusFlags.Carry)]
    [InlineData(StatusFlags.Zero | StatusFlags.Sign)]
    [InlineData(StatusFlags.Parity)]
    [InlineData(StatusFlags.Auxiliary)]
    [InlineData(StatusFlags.Overflow)]
    private void AddKeepsTheCallWhenTheLiveFlagsHaveNoTwoStatementForm(StatusFlags live) {
        // Arrange
        BlockNode instruction = new(Assign(Ax, AluCall("Add", Ax, Bx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, live);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void MemoryDestinationIsNotReadBackForAFlag() {
        // Arrange
        SegmentedPointerNode memory = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0), null,
            new ConstantNode(DataType.UINT16, 0x0010));
        BlockNode instruction = new(Assign(memory, AluCall("Add", memory, Ax)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Zero);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void MemoryOperandReadByTheFlagAndTheResultKeepsTheCall() {
        // Arrange
        SegmentedPointerNode memory = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0), null,
            new ConstantNode(DataType.UINT16, 0x0010));
        BlockNode instruction = new(Assign(Ax, AluCall("Sub", Ax, memory)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Carry);

        // Assert
        rewritten.Should().BeNull();
    }

    [Theory]
    [InlineData("Add", StatusFlags.Zero)]
    [InlineData("Add", StatusFlags.Sign)]
    [InlineData("Sub", StatusFlags.Carry)]
    [InlineData("Sub", StatusFlags.Zero)]
    [InlineData("And", StatusFlags.Zero)]
    [InlineData("Xor", StatusFlags.Carry)]
    [InlineData("Inc", StatusFlags.Overflow)]
    [InlineData("Dec", StatusFlags.Sign)]
    private void RewrittenInstructionComputesTheDestinationAndTheLiveFlagLikeTheAlu(string operation, StatusFlags live) {
        // Arrange
        List<ValueNode> arguments = [Ax];
        if (operation is not ("Inc" or "Dec")) {
            arguments.Add(Bx);
        }
        BlockNode instruction = new(Assign(Ax, AluCall(operation, arguments.ToArray())), MoveIp);
        IReadOnlyList<IVisitableAstNode> statements = StatementsOf(AluAssignmentRewrite.TryRewrite(instruction, live));
        BlockNode withoutIpMove = new([.. statements.Take(statements.Count - 1)]);
        AstExpressionBuilder builder = new();
        Action<State, Memory> run = builder.ToAction(withoutIpMove.Accept(builder)).Compile();
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State rewrittenState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        foreach (ushort left in EdgeValues16) {
            foreach (ushort right in EdgeValues16) {
                rewrittenState.AX = left;
                rewrittenState.BX = right;
                run(rewrittenState, memory);
                ushort expected = RunAlu(new Alu16(aluState), operation, left, right);
                if (rewrittenState.AX != expected || ReadFlag(rewrittenState, live) != ReadFlag(aluState, live)) {
                    mismatches.Add($"{operation}(0x{left:X4}, 0x{right:X4}): AX 0x{rewrittenState.AX:X4} instead of 0x{expected:X4}, " +
                        $"{live} {ReadFlag(rewrittenState, live)} instead of {ReadFlag(aluState, live)}");
                }
            }
        }

        // Assert
        statements.Should().HaveCount(3);
        mismatches.Should().BeEmpty();
    }

    private static BinaryOperationNode FlagAssignment(StatusFlags flag, ValueNode value) =>
        new(DataType.BOOL, new CpuFlagNode((ushort)flag), BinaryOperation.ASSIGN, value);

    private static ushort RunAlu(Alu16 alu, string operation, ushort left, ushort right) => operation switch {
        "Add" => alu.Add(left, right),
        "Sub" => alu.Sub(left, right),
        "And" => alu.And(left, right),
        "Xor" => alu.Xor(left, right),
        "Inc" => alu.Inc(left),
        "Dec" => alu.Dec(left),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };

    private static bool ReadFlag(State state, StatusFlags flag) => flag switch {
        StatusFlags.Carry => state.CarryFlag,
        StatusFlags.Zero => state.ZeroFlag,
        StatusFlags.Sign => state.SignFlag,
        StatusFlags.Overflow => state.OverflowFlag,
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, null)
    };

    private static MethodCallValueNode AluCall(string operation, params ValueNode[] arguments) =>
        new(DataType.UINT16, "Alu16", operation, arguments.Cast<IVisitableAstNode>().ToArray());

    private static BinaryOperationNode Assign(ValueNode destination, ValueNode value) =>
        new(DataType.UINT16, destination, BinaryOperation.ASSIGN, value);

    private static IReadOnlyList<IVisitableAstNode> StatementsOf(BlockNode? block) => block?.Statements ?? [];
}
