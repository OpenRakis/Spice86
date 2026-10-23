namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

using Xunit;

public class AluExpansionCleanupTest {
    private const ushort MemoryOffset = 0x10;
    private const ushort SecondMemoryOffset = 0x20;
    private const int ReportedMismatches = 20;
    private static readonly uint[] EdgeValues8 = [0x00, 0x01, 0x02, 0x0F, 0x10, 0x7F, 0x80, 0x81, 0xFE, 0xFF];
    private static readonly uint[] EdgeValues16 = [0x0000, 0x0001, 0x0002, 0x000F, 0x0010, 0x007F, 0x0080, 0x00FF,
        0x0100, 0x7FFE, 0x7FFF, 0x8000, 0x8001, 0xFFFE, 0xFFFF];
    private static readonly uint[] EdgeValues32 = [0x00000000, 0x00000001, 0x00000002, 0x0000000F, 0x00000010,
        0x000000FF, 0x00000100, 0x00007FFF, 0x00008000, 0x0000FFFF, 0x00010000, 0x7FFFFFFE, 0x7FFFFFFF,
        0x80000000, 0x80000001, 0xFFFFFFFE, 0xFFFFFFFF];
    private static readonly StatusFlags[] LiveSets = [StatusFlags.None, StatusFlags.Zero,
        StatusFlags.Carry | StatusFlags.Zero, StatusFlags.Parity | StatusFlags.Sign | StatusFlags.Overflow,
        StatusFlags.Auxiliary, StatusFlags.All];
    private static readonly RegisterNode Ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
    private static readonly RegisterNode Bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
    private static readonly RegisterNode Al = new(DataType.UINT8, (int)RegisterIndex.AxIndex);
    private static readonly VariableReferenceNode Left16 = new(DataType.UINT16, "left");
    private static readonly VariableReferenceNode Right16 = new(DataType.UINT16, "right");
    private static readonly VariableReferenceNode Result16 = new(DataType.UINT16, "result");

    public static TheoryData<string, BitWidth> CleanedOperations() {
        TheoryData<string, BitWidth> data = new();
        foreach (string operation in new[] { "Add", "Sub", "And", "Or", "Xor", "Inc", "Dec" }) {
            foreach (BitWidth width in new[] { BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32 }) {
                data.Add(operation, width);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(CleanedOperations))]
    public void CleanedExpansionKeepsTheResultAndLiveFlagsAndLeavesDeadFlags(string operation, BitWidth width) {
        // Arrange
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State cleanedState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        foreach (bool leftInMemory in new[] { false, true }) {
            foreach (bool valueUsed in new[] { true, false }) {
                foreach (StatusFlags live in LiveSets) {
                    Action<State, Memory> cleaned = CompileCleaned(operation, width, leftInMemory, valueUsed, live);
                    foreach ((uint left, uint right) in Inputs(operation, width)) {
                        foreach (bool preset in new[] { false, true }) {
                            SetStatusFlags(cleanedState, preset);
                            SetStatusFlags(aluState, preset);
                            SetOperands(cleanedState, memory, width, leftInMemory, left, right);
                            cleaned(cleanedState, memory);
                            uint expected = RunAlu(aluState, operation, width, left, right);
                            string? mismatch = Compare(live, preset, valueUsed, expected, aluState,
                                ReadResult(cleanedState, width), cleanedState);
                            if (mismatch is not null) {
                                mismatches.Add($"{operation}{(int)width}(0x{left:X}, 0x{right:X}), left in memory {leftInMemory}, " +
                                    $"value used {valueUsed}, live {live}, flags preset {preset}: {mismatch}");
                            }
                        }
                    }
                }
            }
        }

        // Assert
        mismatches.Count.Should().Be(0, string.Join(Environment.NewLine, mismatches.Take(ReportedMismatches)));
    }

    [Fact]
    public void AddOfRegistersWithNoLiveFlagIsOnlyTheSum() {
        // Arrange
        AluExpansion expansion = Expand(DataType.UINT16, "Add", Ax, Bx);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.None, true);

        // Assert
        cleaned.Statements.Should().BeEmpty();
        cleaned.Value.Should().Be(new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.PLUS, Bx));
    }

    [Fact]
    public void CompareWithOnlyZeroLiveTestsTheDifference() {
        // Arrange
        AluExpansion expansion = Expand(DataType.UINT16, "Sub", Ax, Bx);
        BinaryOperationNode difference = new(DataType.UINT16, Ax, BinaryOperation.MINUS, Bx);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.Zero, false);

        // Assert
        cleaned.Statements.Should().Equal(
            FlagAssignment(StatusFlags.Zero, new BinaryOperationNode(DataType.BOOL, difference, BinaryOperation.EQUAL,
                new ConstantNode(DataType.UINT16, 0))));
        cleaned.Value.Should().BeNull();
    }

    [Fact]
    public void XorOfARegisterWithItselfIsZero() {
        // Arrange
        AluExpansion expansion = Expand(DataType.UINT16, "Xor", Ax, Ax);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.None, true);

        // Assert
        cleaned.Statements.Should().BeEmpty();
        cleaned.Value.Should().Be(new ConstantNode(DataType.UINT16, 0));
    }

    [Fact]
    public void SubOfARegisterFromItselfIsZero() {
        // Arrange
        AluExpansion expansion = Expand(DataType.UINT8, "Sub", Al, Al);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.None, true);

        // Assert
        cleaned.Statements.Should().BeEmpty();
        cleaned.Value.Should().Be(new ConstantNode(DataType.UINT8, 0));
    }

    [Fact]
    public void XorOfTwoRegistersIsNotZero() {
        // Arrange
        AluExpansion expansion = Expand(DataType.UINT16, "Xor", Ax, Bx);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.None, true);

        // Assert
        cleaned.Value.Should().Be(new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.BITWISE_XOR, Bx));
    }

    [Fact]
    public void LiveFlagReadsTheZeroResult() {
        // Arrange
        AluExpansion expansion = Expand(DataType.UINT16, "Xor", Ax, Ax);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.Zero, true);

        // Assert
        cleaned.Statements.Should().Equal(
            new VariableDeclarationNode(DataType.UINT16, "result", new ConstantNode(DataType.UINT16, 0)),
            FlagAssignment(StatusFlags.Zero, new BinaryOperationNode(DataType.BOOL, Result16, BinaryOperation.EQUAL,
                new ConstantNode(DataType.UINT16, 0))));
        cleaned.Value.Should().Be(Result16);
    }

    [Theory]
    [InlineData("Xor", BitWidth.BYTE_8)]
    [InlineData("Xor", BitWidth.WORD_16)]
    [InlineData("Xor", BitWidth.DWORD_32)]
    [InlineData("Sub", BitWidth.BYTE_8)]
    [InlineData("Sub", BitWidth.WORD_16)]
    [InlineData("Sub", BitWidth.DWORD_32)]
    public void SelfCancellingOperationKeepsTheResultAndLiveFlags(string operation, BitWidth width) {
        // Arrange
        DataType type = DataType.UnsignedFromBitWidth(width);
        RegisterNode operand = new(type, (int)RegisterIndex.BxIndex);
        uint[] values = width switch {
            BitWidth.BYTE_8 => EdgeValues8,
            BitWidth.WORD_16 => EdgeValues16,
            _ => EdgeValues32
        };
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State cleanedState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        foreach (bool valueUsed in new[] { true, false }) {
            foreach (StatusFlags live in LiveSets) {
                CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(Expand(type, operation, operand, operand), live, valueUsed);
                Action<State, Memory> program = CompileProgram(cleaned, type, valueUsed);
                foreach (uint value in values) {
                    foreach (bool preset in new[] { false, true }) {
                        SetStatusFlags(cleanedState, preset);
                        SetStatusFlags(aluState, preset);
                        SetOperands(cleanedState, memory, width, false, value, value);
                        program(cleanedState, memory);
                        uint expected = RunAlu(aluState, operation, width, value, value);
                        string? mismatch = Compare(live, preset, valueUsed, expected, aluState,
                            ReadResult(cleanedState, width), cleanedState);
                        if (mismatch is not null) {
                            mismatches.Add($"{operation}{(int)width}(0x{value:X}, same), value used {valueUsed}, " +
                                $"live {live}, flags preset {preset}: {mismatch}");
                        }
                    }
                }
            }
        }

        // Assert
        mismatches.Count.Should().Be(0, string.Join(Environment.NewLine, mismatches.Take(ReportedMismatches)));
    }

    [Fact]
    public void MemoryOperandReadTwiceStaysATemp() {
        // Arrange
        SegmentedPointerNode memoryOperand = MemoryOperand(DataType.UINT16, MemoryOffset);
        AluExpansion expansion = Expand(DataType.UINT16, "Sub", memoryOperand, Ax);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.Carry, true);

        // Assert
        cleaned.Statements.Should().Equal(
            new VariableDeclarationNode(DataType.UINT16, "left", memoryOperand),
            FlagAssignment(StatusFlags.Carry, new BinaryOperationNode(DataType.BOOL, Left16, BinaryOperation.LESS_THAN, Ax)));
        cleaned.Value.Should().Be(new BinaryOperationNode(DataType.UINT16, Left16, BinaryOperation.MINUS, Ax));
    }

    [Fact]
    public void UnreadMemoryOperandIsStillRead() {
        // Arrange
        SegmentedPointerNode memoryOperand = MemoryOperand(DataType.UINT16, MemoryOffset);
        AluExpansion expansion = Expand(DataType.UINT16, "Sub", memoryOperand, Ax);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.None, false);

        // Assert
        cleaned.Statements.Should().Equal(new VariableDeclarationNode(DataType.UINT16, "left", memoryOperand));
        cleaned.Value.Should().BeNull();
    }

    [Fact]
    public void ResultReadSeveralTimesStaysATemp() {
        // Arrange
        AluExpansion expansion = Expand(DataType.UINT8, "Inc", Al);
        VariableReferenceNode result = new(DataType.UINT8, "result");
        DataType signed = DataType.SignedFromBitWidth(BitWidth.BYTE_8);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.Zero | StatusFlags.Sign, true);

        // Assert
        cleaned.Statements.Should().Equal(
            new VariableDeclarationNode(DataType.UINT8, "result",
                new BinaryOperationNode(DataType.UINT8, Al, BinaryOperation.PLUS, new ConstantNode(DataType.UINT8, 1))),
            FlagAssignment(StatusFlags.Zero, new BinaryOperationNode(DataType.BOOL, result, BinaryOperation.EQUAL,
                new ConstantNode(DataType.UINT8, 0))),
            FlagAssignment(StatusFlags.Sign, new BinaryOperationNode(DataType.BOOL, new TypeConversionNode(signed, result),
                BinaryOperation.LESS_THAN, new ConstantNode(signed, 0))));
        cleaned.Value.Should().Be(result);
    }

    [Fact]
    public void SingleReadMemoryOperandMovesIntoTheResult() {
        // Arrange
        SegmentedPointerNode memoryOperand = MemoryOperand(DataType.UINT16, MemoryOffset);
        AluExpansion expansion = Expand(DataType.UINT16, "Add", memoryOperand, Ax);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.Zero, true);

        // Assert
        cleaned.Statements.Should().Equal(
            new VariableDeclarationNode(DataType.UINT16, "result",
                new BinaryOperationNode(DataType.UINT16, memoryOperand, BinaryOperation.PLUS, Ax)),
            FlagAssignment(StatusFlags.Zero, new BinaryOperationNode(DataType.BOOL, Result16, BinaryOperation.EQUAL,
                new ConstantNode(DataType.UINT16, 0))));
        cleaned.Value.Should().Be(Result16);
    }

    [Fact]
    public void CallOperandIsNeverMoved() {
        // Arrange
        MethodCallValueNode peek = new(DataType.UINT16, "Stack", "Peek16", new ConstantNode(DataType.UINT32, 0));
        AluExpansion expansion = Expand(DataType.UINT16, "Add", peek, Ax);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.None, true);

        // Assert
        cleaned.Statements.Should().Equal(new VariableDeclarationNode(DataType.UINT16, "left", peek));
        cleaned.Value.Should().Be(new BinaryOperationNode(DataType.UINT16, Left16, BinaryOperation.PLUS, Ax));
    }

    [Fact]
    public void TwoMemoryOperandsStayTempsToKeepTheirReadOrder() {
        // Arrange
        SegmentedPointerNode first = MemoryOperand(DataType.UINT16, MemoryOffset);
        SegmentedPointerNode second = MemoryOperand(DataType.UINT16, SecondMemoryOffset);
        AluExpansion expansion = Expand(DataType.UINT16, "Sub", first, second);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.Carry, false);

        // Assert
        cleaned.Statements.Should().Equal(
            new VariableDeclarationNode(DataType.UINT16, "left", first),
            new VariableDeclarationNode(DataType.UINT16, "right", second),
            FlagAssignment(StatusFlags.Carry, new BinaryOperationNode(DataType.BOOL, Left16, BinaryOperation.LESS_THAN, Right16)));
        cleaned.Value.Should().BeNull();
    }

    [Fact]
    public void NarrowerOperandIsConvertedToTheTempType() {
        // Arrange
        AluExpansion expansion = Expand(DataType.UINT16, "Add", Al, Bx);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.None, true);

        // Assert
        cleaned.Statements.Should().BeEmpty();
        cleaned.Value.Should().Be(new BinaryOperationNode(DataType.UINT16, new TypeConversionNode(DataType.UINT16, Al),
            BinaryOperation.PLUS, Bx));
    }

    [Fact]
    public void UnreadComputedRegisterOperandIsDropped() {
        // Arrange
        BinaryOperationNode computed = new(DataType.UINT16, Ax, BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 2));
        AluExpansion expansion = Expand(DataType.UINT16, "Sub", computed, Bx);

        // Act
        CleanedAluExpansion cleaned = AluExpansionCleanup.Clean(expansion, StatusFlags.None, false);

        // Assert
        cleaned.Statements.Should().BeEmpty();
        cleaned.Value.Should().BeNull();
    }

    private static AluExpansion Expand(DataType type, string operation, params ValueNode[] arguments) {
        MethodCallValueNode call = new(type, $"Alu{(int)type.BitWidth}", operation, arguments.Cast<IVisitableAstNode>().ToArray());
        return AluExpansionBuilder.TryExpand(call) ?? throw new InvalidOperationException($"{operation} was not expanded");
    }

    private static SegmentedPointerNode MemoryOperand(DataType type, ushort offset) =>
        new(type, new ConstantNode(DataType.UINT16, 0), null, new ConstantNode(DataType.UINT16, offset));

    private static BinaryOperationNode FlagAssignment(StatusFlags flag, ValueNode value) =>
        new(DataType.BOOL, new CpuFlagNode((ushort)flag), BinaryOperation.ASSIGN, value);

    private static Action<State, Memory> CompileCleaned(string operation, BitWidth width, bool leftInMemory,
        bool valueUsed, StatusFlags live) {
        DataType type = DataType.UnsignedFromBitWidth(width);
        ValueNode left = leftInMemory ? MemoryOperand(type, MemoryOffset) : new RegisterNode(type, (int)RegisterIndex.BxIndex);
        RegisterNode right = new(type, (int)RegisterIndex.CxIndex);
        AluExpansion expansion = operation is "Inc" or "Dec" ? Expand(type, operation, left) : Expand(type, operation, left, right);
        return CompileProgram(AluExpansionCleanup.Clean(expansion, live, valueUsed), type, valueUsed);
    }

    private static Action<State, Memory> CompileProgram(CleanedAluExpansion cleaned, DataType type, bool valueUsed) {
        List<IVisitableAstNode> statements = [.. cleaned.Statements];
        if (valueUsed) {
            ValueNode value = cleaned.Value ?? throw new InvalidOperationException("Used value is missing");
            statements.Add(new BinaryOperationNode(type, new RegisterNode(type, (int)RegisterIndex.AxIndex),
                BinaryOperation.ASSIGN, value));
        }
        BlockNode program = new([.. statements]);
        AstExpressionBuilder builder = new();
        return builder.ToAction(program.Accept(builder)).Compile();
    }

    private static IEnumerable<(uint Left, uint Right)> Inputs(string operation, BitWidth width) {
        uint[] edges = width switch {
            BitWidth.BYTE_8 => EdgeValues8,
            BitWidth.WORD_16 => EdgeValues16,
            _ => EdgeValues32
        };
        uint[] rights = edges;
        if (operation is "Inc" or "Dec") {
            rights = [0];
        }
        foreach (uint left in edges) {
            foreach (uint right in rights) {
                yield return (left, right);
            }
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

    private static void SetOperands(State state, Memory memory, BitWidth width, bool leftInMemory, uint left, uint right) {
        switch (width) {
            case BitWidth.BYTE_8:
                memory.UInt8[0, MemoryOffset] = (byte)left;
                state.BL = (byte)left;
                state.CL = (byte)right;
                break;
            case BitWidth.WORD_16:
                memory.UInt16[0, MemoryOffset] = (ushort)left;
                state.BX = (ushort)left;
                state.CX = (ushort)right;
                break;
            default:
                memory.UInt32[0, MemoryOffset] = left;
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

    private static string? Compare(StatusFlags live, bool preset, bool valueUsed, uint expectedResult, State alu,
        uint actualResult, State actual) {
        List<string> differences = new();
        if (valueUsed && expectedResult != actualResult) {
            differences.Add($"result 0x{actualResult:X} instead of 0x{expectedResult:X}");
        }
        AddIfDifferent(differences, "CF", Expected(live, StatusFlags.Carry, alu.CarryFlag, preset), actual.CarryFlag);
        AddIfDifferent(differences, "PF", Expected(live, StatusFlags.Parity, alu.ParityFlag, preset), actual.ParityFlag);
        AddIfDifferent(differences, "AF", Expected(live, StatusFlags.Auxiliary, alu.AuxiliaryFlag, preset), actual.AuxiliaryFlag);
        AddIfDifferent(differences, "ZF", Expected(live, StatusFlags.Zero, alu.ZeroFlag, preset), actual.ZeroFlag);
        AddIfDifferent(differences, "SF", Expected(live, StatusFlags.Sign, alu.SignFlag, preset), actual.SignFlag);
        AddIfDifferent(differences, "OF", Expected(live, StatusFlags.Overflow, alu.OverflowFlag, preset), actual.OverflowFlag);
        return differences.Count == 0 ? null : string.Join(", ", differences);
    }

    private static bool Expected(StatusFlags live, StatusFlags flag, bool aluValue, bool preset) =>
        (live & flag) != StatusFlags.None ? aluValue : preset;

    private static void AddIfDifferent(List<string> differences, string flag, bool expected, bool actual) {
        if (expected != actual) {
            differences.Add($"{flag} {actual} instead of {expected}");
        }
    }
}
