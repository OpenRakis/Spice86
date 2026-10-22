namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

using Xunit;

public class AluExpansionBuilderTest {
    private const int RandomPairs = 20000;
    private const int RandomSeed = 20240607;
    private const int ReportedMismatches = 20;
    private static readonly uint[] EdgeValues16 = [0x0000, 0x0001, 0x0002, 0x000F, 0x0010, 0x007F, 0x0080, 0x00FF,
        0x0100, 0x7FFE, 0x7FFF, 0x8000, 0x8001, 0xFFFE, 0xFFFF];
    private static readonly uint[] EdgeValues32 = [0x00000000, 0x00000001, 0x00000002, 0x0000000F, 0x00000010,
        0x000000FF, 0x00000100, 0x00007FFF, 0x00008000, 0x0000FFFF, 0x00010000, 0x7FFFFFFE, 0x7FFFFFFF,
        0x80000000, 0x80000001, 0xFFFFFFFE, 0xFFFFFFFF];

    public static TheoryData<string, BitWidth> ExpandedOperations() {
        TheoryData<string, BitWidth> data = new();
        foreach (string operation in new[] { "Add", "Sub", "And", "Or", "Xor", "Inc", "Dec" }) {
            foreach (BitWidth width in new[] { BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32 }) {
                data.Add(operation, width);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ExpandedOperations))]
    public void ExpansionComputesTheSameResultAndFlagsAsTheAlu(string operation, BitWidth width) {
        // Arrange
        Action<State, Memory> expansion = CompileExpansion(operation, width);
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State expansionState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        foreach ((uint left, uint right) in Inputs(operation, width)) {
            foreach (bool preset in new[] { false, true }) {
                SetStatusFlags(expansionState, preset);
                SetStatusFlags(aluState, preset);
                SetOperands(expansionState, width, left, right);
                expansion(expansionState, memory);
                uint expected = RunAlu(aluState, operation, width, left, right);
                string? mismatch = Compare(expected, aluState, ReadResult(expansionState, width), expansionState);
                if (mismatch is not null) {
                    mismatches.Add($"{operation}{(int)width}(0x{left:X}, 0x{right:X}), flags preset {preset}: {mismatch}");
                }
            }
        }

        // Assert
        mismatches.Count.Should().Be(0, string.Join(Environment.NewLine, mismatches.Take(ReportedMismatches)));
    }

    [Theory]
    [MemberData(nameof(ExpandedOperations))]
    public void ExpansionWritesTheFlagsOfTheAluFlagTable(string operation, BitWidth width) {
        // Arrange
        AluExpansion expansion = Expand(operation, width);
        AluFlagTable.TryGetEffect(operation, width, null, out FlagEffect expected);

        // Act
        FlagEffect actual = FlagEffectVisitor.Of(new BlockNode([.. expansion.Statements]));
        StatusFlags assigned = expansion.FlagAssignments.Aggregate(StatusFlags.None, (all, assignment) => all | assignment.Flag);

        // Assert
        actual.Should().Be(expected);
        assigned.Should().Be(expected.MustWrite);
    }

    [Fact]
    public void BinaryOperationReadsEachArgumentOnceAndWritesFlagsInBitOrder() {
        // Arrange
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        VariableReferenceNode left = new(DataType.UINT16, "left");
        VariableReferenceNode right = new(DataType.UINT16, "right");
        VariableReferenceNode result = new(DataType.UINT16, "result");

        // Act
        AluExpansion expansion = AluExpansionBuilder.TryExpand(new MethodCallValueNode(DataType.UINT16, "Alu16", "Sub", ax, bx))
            ?? throw new InvalidOperationException("expected AluExpansion for 16-bit Sub");

        // Assert
        expansion.Operands.Select(operand => operand.VariableName).Should().Equal("left", "right");
        expansion.Operands[0].Initializer.Should().Be(ax);
        expansion.Operands[1].Initializer.Should().Be(bx);
        expansion.Result.VariableName.Should().Be("result");
        expansion.Result.Initializer.Should().Be(new BinaryOperationNode(DataType.UINT16, left, BinaryOperation.MINUS, right));
        expansion.FlagAssignments.Select(assignment => assignment.Flag).Should().Equal(StatusFlags.Carry,
            StatusFlags.Parity, StatusFlags.Auxiliary, StatusFlags.Zero, StatusFlags.Sign, StatusFlags.Overflow);
        expansion.FlagAssignments[0].Value.Should().Be(new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.LESS_THAN, right));
        expansion.FlagAssignments[3].Value.Should().Be(new BinaryOperationNode(DataType.BOOL, result, BinaryOperation.EQUAL,
            new Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant.ConstantNode(DataType.UINT16, 0)));
        expansion.Statements.Should().HaveCount(9);
    }

    [Fact]
    public void IncrementHasOneOperandAndLeavesTheCarryFlagAlone() {
        // Arrange
        RegisterNode cl = new(DataType.UINT8, (int)RegisterIndex.CxIndex);

        // Act
        AluExpansion expansion = AluExpansionBuilder.TryExpand(new MethodCallValueNode(DataType.UINT8, "Alu8", "Inc", cl))
            ?? throw new InvalidOperationException("expected AluExpansion for 8-bit Inc");

        // Assert
        expansion.Operands.Should().ContainSingle().Which.Initializer.Should().Be(cl);
        expansion.FlagAssignments.Select(assignment => assignment.Flag).Should().NotContain(StatusFlags.Carry);
        expansion.Statements.Should().HaveCount(7);
    }

    [Theory]
    [InlineData("Alu16", "Adc", 2)]
    [InlineData("Alu16", "Sbb", 2)]
    [InlineData("Alu16", "Shl", 2)]
    [InlineData("Alu16", "Mul", 2)]
    [InlineData("Alu16", "UpdateFlags", 1)]
    [InlineData("Alu16", "Add", 1)]
    [InlineData("Alu16", "Inc", 2)]
    [InlineData("Stack", "Add", 2)]
    public void OtherCallsAreNotExpanded(string propertyPath, string operation, int argumentCount) {
        // Arrange
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        IVisitableAstNode[] arguments = Enumerable.Repeat<IVisitableAstNode>(ax, argumentCount).ToArray();

        // Act
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(new MethodCallValueNode(DataType.UINT16, propertyPath, operation, arguments));

        // Assert
        expansion.Should().BeNull();
    }

    [Fact]
    public void CallWhoseTypeDoesNotMatchItsAluWidthIsNotExpanded() {
        // Arrange
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);

        // Act
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(new MethodCallValueNode(DataType.UINT8, "Alu16", "Add", ax, ax));

        // Assert
        expansion.Should().BeNull();
    }

    private static AluExpansion Expand(string operation, BitWidth width) {
        DataType type = DataType.UnsignedFromBitWidth(width);
        RegisterNode left = new(type, (int)RegisterIndex.BxIndex);
        RegisterNode right = new(type, (int)RegisterIndex.CxIndex);
        List<IVisitableAstNode> arguments = [left];
        if (operation is not ("Inc" or "Dec")) {
            arguments.Add(right);
        }
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(
            new MethodCallValueNode(type, $"Alu{(int)width}", operation, arguments.ToArray()));
        if (expansion is null) {
            throw new InvalidOperationException($"{operation}{(int)width} was not expanded");
        }
        return expansion;
    }

    private static Action<State, Memory> CompileExpansion(string operation, BitWidth width) {
        DataType type = DataType.UnsignedFromBitWidth(width);
        AluExpansion expansion = Expand(operation, width);
        RegisterNode destination = new(type, (int)RegisterIndex.AxIndex);
        BinaryOperationNode assignResult = new(type, destination, BinaryOperation.ASSIGN, expansion.Result.Reference);
        BlockNode program = new([.. expansion.Statements, assignResult]);
        AstExpressionBuilder builder = new();
        return builder.ToAction(program.Accept(builder)).Compile();
    }

    private static IEnumerable<(uint Left, uint Right)> Inputs(string operation, BitWidth width) {
        if (width == BitWidth.BYTE_8) {
            uint rightCount = operation is "Inc" or "Dec" ? 1u : 256u;
            for (uint left = 0; left < 256; left++) {
                for (uint right = 0; right < rightCount; right++) {
                    yield return (left, right);
                }
            }
            yield break;
        }
        uint[] edges = width == BitWidth.WORD_16 ? EdgeValues16 : EdgeValues32;
        foreach (uint left in edges) {
            foreach (uint right in edges) {
                yield return (left, right);
            }
        }
        uint mask = width == BitWidth.WORD_16 ? 0xFFFFu : 0xFFFFFFFFu;
        Random random = new(RandomSeed);
        for (int i = 0; i < RandomPairs; i++) {
            uint left = (uint)random.NextInt64(0, 1L << 32) & mask;
            uint right = (uint)random.NextInt64(0, 1L << 32) & mask;
            yield return (left, right);
        }
    }

    private static void SetStatusFlags(State state, bool value) {
        state.CarryFlag = value;
        state.ParityFlag = value;
        state.AuxiliaryFlag = value;
        state.ZeroFlag = value;
        state.SignFlag = value;
        state.OverflowFlag = value;
    }

    private static void SetOperands(State state, BitWidth width, uint left, uint right) {
        switch (width) {
            case BitWidth.BYTE_8:
                state.BL = (byte)left;
                state.CL = (byte)right;
                break;
            case BitWidth.WORD_16:
                state.BX = (ushort)left;
                state.CX = (ushort)right;
                break;
            default:
                state.EBX = left;
                state.ECX = right;
                break;
        }
    }

    private static uint ReadResult(State state, BitWidth width) => width switch {
        BitWidth.BYTE_8 => state.AL,
        BitWidth.WORD_16 => state.AX,
        _ => state.EAX
    };

    private static uint RunAlu(State state, string operation, BitWidth width, uint left, uint right) => width switch {
        BitWidth.BYTE_8 => RunAlu(new Alu8(state), operation, (byte)left, (byte)right),
        BitWidth.WORD_16 => RunAlu(new Alu16(state), operation, (ushort)left, (ushort)right),
        _ => RunAlu(new Alu32(state), operation, left, right)
    };

    private static uint RunAlu<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper>(
        Alu<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper> alu, string operation, TUnsigned left, TUnsigned right)
        where TUnsigned : IUnsignedNumber<TUnsigned>
        where TSigned : ISignedNumber<TSigned>
        where TUnsignedUpper : IUnsignedNumber<TUnsignedUpper>
        where TSignedUpper : ISignedNumber<TSignedUpper> {
        TUnsigned result = operation switch {
            "Add" => alu.Add(left, right),
            "Sub" => alu.Sub(left, right),
            "And" => alu.And(left, right),
            "Or" => alu.Or(left, right),
            "Xor" => alu.Xor(left, right),
            "Inc" => alu.Inc(left),
            "Dec" => alu.Dec(left),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
        return uint.CreateTruncating(result);
    }

    private static string? Compare(uint expectedResult, State expected, uint actualResult, State actual) {
        List<string> differences = new();
        if (expectedResult != actualResult) {
            differences.Add($"result 0x{actualResult:X} instead of 0x{expectedResult:X}");
        }
        AddIfDifferent(differences, "CF", expected.CarryFlag, actual.CarryFlag);
        AddIfDifferent(differences, "PF", expected.ParityFlag, actual.ParityFlag);
        AddIfDifferent(differences, "AF", expected.AuxiliaryFlag, actual.AuxiliaryFlag);
        AddIfDifferent(differences, "ZF", expected.ZeroFlag, actual.ZeroFlag);
        AddIfDifferent(differences, "SF", expected.SignFlag, actual.SignFlag);
        AddIfDifferent(differences, "OF", expected.OverflowFlag, actual.OverflowFlag);
        return differences.Count == 0 ? null : string.Join(", ", differences);
    }

    private static void AddIfDifferent(List<string> differences, string flag, bool expected, bool actual) {
        if (expected != actual) {
            differences.Add($"{flag} {actual} instead of {expected}");
        }
    }
}
