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
        BlockNode instruction = new(AluCall("Mul", Ax, Bx), MoveIp);

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
        BlockNode instruction = new(Assign(Ax, AluCall("Mul", Ax, Bx)), MoveIp);

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
    [InlineData("Add", (ushort)StatusFlags.Zero)]
    [InlineData("Add", (ushort)StatusFlags.Sign)]
    [InlineData("Sub", (ushort)StatusFlags.Carry)]
    [InlineData("Sub", (ushort)StatusFlags.Zero)]
    [InlineData("And", (ushort)StatusFlags.Zero)]
    [InlineData("Xor", (ushort)StatusFlags.Carry)]
    [InlineData("Inc", (ushort)StatusFlags.Overflow)]
    [InlineData("Dec", (ushort)StatusFlags.Sign)]
    public void RewrittenInstructionComputesTheDestinationAndTheLiveFlagLikeTheAlu(string operation, ushort live) {
        // Arrange
        StatusFlags liveFlag = (StatusFlags)live;
        List<ValueNode> arguments = [Ax];
        if (operation is not ("Inc" or "Dec")) {
            arguments.Add(Bx);
        }
        BlockNode instruction = new(Assign(Ax, AluCall(operation, arguments.ToArray())), MoveIp);
        IReadOnlyList<IVisitableAstNode> statements = StatementsOf(AluAssignmentRewrite.TryRewrite(instruction, liveFlag));
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
                if (rewrittenState.AX != expected || ReadFlag(rewrittenState, liveFlag) != ReadFlag(aluState, liveFlag)) {
                    mismatches.Add($"{operation}(0x{left:X4}, 0x{right:X4}): AX 0x{rewrittenState.AX:X4} instead of 0x{expected:X4}, " +
                        $"{liveFlag} {ReadFlag(rewrittenState, liveFlag)} instead of {ReadFlag(aluState, liveFlag)}");
                }
            }
        }

        // Assert
        statements.Should().HaveCount(3);
        mismatches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Shl", 1, (ushort)StatusFlags.None)]
    [InlineData("Shl", 1, (ushort)StatusFlags.Carry)]
    [InlineData("Shl", 4, (ushort)StatusFlags.Zero)]
    [InlineData("Shr", 1, (ushort)StatusFlags.Carry)]
    [InlineData("Shr", 16, (ushort)StatusFlags.Carry)]
    [InlineData("Shr", 4, (ushort)StatusFlags.Sign)]
    [InlineData("Sar", 1, (ushort)StatusFlags.Sign)]
    [InlineData("Sar", 17, (ushort)StatusFlags.Carry)]
    public void RewrittenShiftComputesTheDestinationAndTheLiveFlagLikeTheAlu(string operation, int count, ushort live) {
        // Arrange
        StatusFlags liveFlag = (StatusFlags)live;
        BlockNode instruction = new(Assign(Ax, AluCall(operation, Ax, new ConstantNode(DataType.INT32, (ulong)count))), MoveIp);
        BlockNode rewritten = AluAssignmentRewrite.TryRewrite(instruction, liveFlag)
            ?? throw new InvalidOperationException($"{operation} by {count} was not rewritten");
        IReadOnlyList<IVisitableAstNode> statements = rewritten.Statements;
        BlockNode withoutIpMove = new([.. statements.Take(statements.Count - 1)]);
        AstExpressionBuilder builder = new();
        Action<State, Memory> run = builder.ToAction(withoutIpMove.Accept(builder)).Compile();
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State rewrittenState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        for (uint ax = 0; ax <= 0xFFFF; ax++) {
            rewrittenState.AX = (ushort)ax;
            run(rewrittenState, memory);
            ushort expected = RunAluShift(new Alu16(aluState), operation, (ushort)ax, count);
            if (rewrittenState.AX != expected
                || (liveFlag != StatusFlags.None && ReadFlag(rewrittenState, liveFlag) != ReadFlag(aluState, liveFlag))) {
                mismatches.Add($"{operation}(0x{ax:X4}, {count}): AX 0x{rewrittenState.AX:X4} instead of 0x{expected:X4}, " +
                    $"{liveFlag} {ReadFlag(rewrittenState, liveFlag)} instead of {ReadFlag(aluState, liveFlag)}");
            }
        }

        // Assert
        statements.Should().HaveCount(liveFlag == StatusFlags.None ? 2 : 3);
        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void ShiftByClWithDeadFlagsBecomesAMaskedShift() {
        // Arrange
        RegisterNode cl = new(DataType.UINT8, (int)RegisterIndex.CxIndex);
        BlockNode instruction = new(Assign(Ax, AluCall("Shr", Ax, cl)), MoveIp);
        BlockNode rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.None)
            ?? throw new InvalidOperationException("shr by CL with dead flags was not rewritten");
        IReadOnlyList<IVisitableAstNode> statements = rewritten.Statements;
        BlockNode withoutIpMove = new([.. statements.Take(statements.Count - 1)]);
        AstExpressionBuilder builder = new();
        Action<State, Memory> run = builder.ToAction(withoutIpMove.Accept(builder)).Compile();
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State rewrittenState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        for (int count = 0; count <= 40; count++) {
            foreach (ushort left in EdgeValues16) {
                rewrittenState.CL = (byte)count;
                rewrittenState.AX = left;
                run(rewrittenState, memory);
                ushort expected = RunAluShift(new Alu16(aluState), "Shr", left, count);
                if (rewrittenState.AX != expected) {
                    mismatches.Add($"Shr(0x{left:X4}, CL={count}): AX 0x{rewrittenState.AX:X4} instead of 0x{expected:X4}");
                }
            }
        }

        // Assert
        statements.Should().HaveCount(2);
        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void ShiftByClWithALiveFlagKeepsTheCall() {
        // Arrange
        RegisterNode cl = new(DataType.UINT8, (int)RegisterIndex.CxIndex);
        BlockNode instruction = new(Assign(Ax, AluCall("Shr", Ax, cl)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Zero);

        // Assert
        rewritten.Should().BeNull();
    }

    [Fact]
    public void ShiftByAMaskedZeroConstantKeepsTheCall() {
        // Arrange
        BlockNode instruction = new(Assign(Ax, AluCall("Shl", Ax, new ConstantNode(DataType.INT32, 32))), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.None);

        // Assert
        rewritten.Should().BeNull();
    }

    [Theory]
    [InlineData("Adc", -1)]
    [InlineData("Sbb", -1)]
    [InlineData("Rcl", 1)]
    [InlineData("Rcl", 5)]
    [InlineData("Rcr", 1)]
    [InlineData("Rcr", 16)]
    public void RewrittenCarryReaderComputesTheDestinationLikeTheAlu(string operation, int count) {
        // Arrange
        ValueNode second = count < 0 ? Bx : new ConstantNode(DataType.INT32, (ulong)count);
        BlockNode instruction = new(Assign(Ax, AluCall(operation, Ax, second)), MoveIp);
        BlockNode rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.None)
            ?? throw new InvalidOperationException($"{operation} was not rewritten");
        IReadOnlyList<IVisitableAstNode> statements = rewritten.Statements;
        BlockNode withoutIpMove = new([.. statements.Take(statements.Count - 1)]);
        AstExpressionBuilder builder = new();
        Action<State, Memory> run = builder.ToAction(withoutIpMove.Accept(builder)).Compile();
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State rewrittenState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        foreach (ushort ax in EdgeValues16) {
            foreach (ushort bx in EdgeValues16) {
                foreach (bool carry in new[] { false, true }) {
                    rewrittenState.AX = ax;
                    rewrittenState.BX = bx;
                    rewrittenState.CarryFlag = carry;
                    run(rewrittenState, memory);
                    aluState.CarryFlag = carry;
                    ushort expected = RunAluCarryReader(new Alu16(aluState), operation, ax, bx, count);
                    if (rewrittenState.AX != expected) {
                        mismatches.Add($"{operation}(0x{ax:X4}, 0x{bx:X4}), CF={carry}: AX 0x{rewrittenState.AX:X4} instead of 0x{expected:X4}");
                    }
                }
            }
        }

        // Assert
        statements.Should().HaveCount(2);
        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void CarryReaderWithALiveFlagKeepsTheCall() {
        // Arrange
        BlockNode instruction = new(Assign(Ax, AluCall("Adc", Ax, Bx)), MoveIp);

        // Act
        BlockNode? rewritten = AluAssignmentRewrite.TryRewrite(instruction, StatusFlags.Zero);

        // Assert
        rewritten.Should().BeNull();
    }

    [Theory]
    [InlineData("Rol", 4, (ushort)StatusFlags.None)]
    [InlineData("Rol", 1, (ushort)StatusFlags.Carry)]
    [InlineData("Ror", 4, (ushort)StatusFlags.None)]
    [InlineData("Ror", 15, (ushort)StatusFlags.Carry)]
    public void RewrittenRotateComputesTheDestinationAndTheLiveCarryLikeTheAlu(string operation, int count, ushort live) {
        // Arrange
        StatusFlags liveFlag = (StatusFlags)live;
        BlockNode instruction = new(Assign(Ax, AluCall(operation, Ax, new ConstantNode(DataType.INT32, (ulong)count))), MoveIp);
        BlockNode rewritten = AluAssignmentRewrite.TryRewrite(instruction, liveFlag)
            ?? throw new InvalidOperationException($"{operation} by {count} was not rewritten");
        IReadOnlyList<IVisitableAstNode> statements = rewritten.Statements;
        BlockNode withoutIpMove = new([.. statements.Take(statements.Count - 1)]);
        AstExpressionBuilder builder = new();
        Action<State, Memory> run = builder.ToAction(withoutIpMove.Accept(builder)).Compile();
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State rewrittenState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        for (uint ax = 0; ax <= 0xFFFF; ax++) {
            rewrittenState.AX = (ushort)ax;
            run(rewrittenState, memory);
            ushort expected = RunAluRotate(new Alu16(aluState), operation, (ushort)ax, count);
            if (rewrittenState.AX != expected
                || (liveFlag != StatusFlags.None && ReadFlag(rewrittenState, liveFlag) != ReadFlag(aluState, liveFlag))) {
                mismatches.Add($"{operation}(0x{ax:X4}, {count}): AX 0x{rewrittenState.AX:X4} instead of 0x{expected:X4}, " +
                    $"{liveFlag} {ReadFlag(rewrittenState, liveFlag)} instead of {ReadFlag(aluState, liveFlag)}");
            }
        }

        // Assert
        statements.Should().HaveCount(liveFlag == StatusFlags.None ? 2 : 3);
        mismatches.Should().BeEmpty();
    }

    private static ushort RunAluCarryReader(Alu16 alu, string operation, ushort left, ushort right, int count) => operation switch {
        "Adc" => alu.Adc(left, right),
        "Sbb" => alu.Sbb(left, right),
        "Rcl" => alu.Rcl(left, (byte)count),
        _ => alu.Rcr(left, count)
    };

    private static ushort RunAluRotate(Alu16 alu, string operation, ushort value, int count) => operation switch {
        "Rol" => alu.Rol(value, (byte)count),
        _ => alu.Ror(value, count)
    };

    private static ushort RunAluShift(Alu16 alu, string operation, ushort value, int count) => operation switch {
        "Shl" => alu.Shl(value, count),
        "Shr" => alu.Shr(value, count),
        _ => alu.Sar(value, count)
    };

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
