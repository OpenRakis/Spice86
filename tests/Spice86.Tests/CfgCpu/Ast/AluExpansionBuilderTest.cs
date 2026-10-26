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
        foreach (string operation in new[] { "Add", "Sub", "And", "Or", "Xor", "Inc", "Dec", "Adc", "Sbb" }) {
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
    [InlineData("Alu16", "Adc", 1)]
    [InlineData("Alu16", "Rol", 2)]
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

    public static TheoryData<string, BitWidth> ShiftOperations() {
        TheoryData<string, BitWidth> data = new();
        foreach (string operation in new[] { "Shl", "Shr", "Sar" }) {
            foreach (BitWidth width in new[] { BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32 }) {
                data.Add(operation, width);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ShiftOperations))]
    public void ShiftExpansionComputesTheSameResultAndFlagsAsTheAlu(string operation, BitWidth width) {
        // Arrange
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State expansionState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        for (int count = 1; count <= 33; count++) {
            if (count == 32) {
                continue;
            }
            Action<State, Memory> expansion = CompileShiftExpansion(operation, width, count);
            foreach (uint value in ShiftInputs(width, 2000)) {
                foreach (bool preset in new[] { false, true }) {
                    SetStatusFlags(expansionState, preset);
                    SetStatusFlags(aluState, preset);
                    SetShiftOperand(expansionState, width, value);
                    expansion(expansionState, memory);
                    uint expected = RunAluShift(aluState, operation, width, value, count);
                    string? mismatch = Compare(expected, aluState, ReadResult(expansionState, width), expansionState);
                    if (mismatch is not null) {
                        mismatches.Add($"{operation}{(int)width}(0x{value:X}, {count}), flags preset {preset}: {mismatch}");
                    }
                }
            }
        }

        // Assert
        mismatches.Count.Should().Be(0, string.Join(Environment.NewLine, mismatches.Take(ReportedMismatches)));
    }

    [Theory]
    [MemberData(nameof(ShiftOperations))]
    public void ShiftExpansionWritesTheFlagsOfTheAluFlagTable(string operation, BitWidth width) {
        foreach (int count in new[] { 1, 9, 17 }) {
            // Arrange
            AluExpansion expansion = ExpandShift(operation, width, count);
            AluFlagTable.TryGetEffect(operation, width, (ulong)count, out FlagEffect expected);

            // Act
            FlagEffect actual = FlagEffectVisitor.Of(new BlockNode([.. expansion.Statements]));
            StatusFlags assigned = expansion.FlagAssignments.Aggregate(StatusFlags.None, (all, assignment) => all | assignment.Flag);

            // Assert
            actual.Should().Be(expected);
            assigned.Should().Be(expected.MustWrite);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void ShiftByAMaskedZeroCountIsNotExpanded(int count) {
        // Arrange
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        MethodCallValueNode call = new(DataType.UINT16, "Alu16", "Shl", bx, new ConstantNode(DataType.INT32, (ulong)count));

        // Act
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        ValueNode? value = AluExpansionBuilder.TryExpandShiftValue(call);

        // Assert
        expansion.Should().BeNull();
        value.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(ShiftOperations))]
    public void ShiftValueComputesTheSameResultAsTheAlu(string operation, BitWidth width) {
        // Arrange
        DataType type = DataType.UnsignedFromBitWidth(width);
        RegisterNode operand = new(type, (int)RegisterIndex.BxIndex);
        RegisterNode cl = new(DataType.UINT8, (int)RegisterIndex.CxIndex);
        ValueNode value = AluExpansionBuilder.TryExpandShiftValue(
            new MethodCallValueNode(type, $"Alu{(int)width}", operation, operand, cl))
            ?? throw new InvalidOperationException($"{operation}{(int)width} by CL was not expanded");
        RegisterNode destination = new(type, (int)RegisterIndex.AxIndex);
        BlockNode program = new(new BinaryOperationNode(type, destination, BinaryOperation.ASSIGN, value));
        AstExpressionBuilder builder = new();
        Action<State, Memory> run = builder.ToAction(program.Accept(builder)).Compile();
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State runState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        for (int count = 0; count < 256; count++) {
            foreach (uint input in ShiftInputs(width, 200)) {
                runState.CL = (byte)count;
                SetShiftOperand(runState, width, input);
                run(runState, memory);
                uint expected = RunAluShift(aluState, operation, width, input, count);
                if (ReadResult(runState, width) != expected) {
                    mismatches.Add($"{operation}{(int)width}(0x{input:X}, CL={count}): result 0x{ReadResult(runState, width):X} instead of 0x{expected:X}");
                }
            }
        }

        // Assert
        mismatches.Count.Should().Be(0, string.Join(Environment.NewLine, mismatches.Take(ReportedMismatches)));
    }

    [Fact]
    public void ShiftValueIsOnlyForShiftsWithAVariableCount() {
        // Arrange
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        RegisterNode cl = new(DataType.UINT8, (int)RegisterIndex.CxIndex);

        // Act
        ValueNode? constant = AluExpansionBuilder.TryExpandShiftValue(
            new MethodCallValueNode(DataType.UINT16, "Alu16", "Shl", bx, new ConstantNode(DataType.INT32, 1)));
        ValueNode? rotate = AluExpansionBuilder.TryExpandShiftValue(
            new MethodCallValueNode(DataType.UINT16, "Alu16", "Rol", bx, cl));
        ValueNode? addition = AluExpansionBuilder.TryExpandShiftValue(
            new MethodCallValueNode(DataType.UINT16, "Alu16", "Add", ax, bx));

        // Assert
        constant.Should().BeNull();
        rotate.Should().BeNull();
        addition.Should().BeNull();
    }

    public static TheoryData<string, BitWidth> RotateOperations() {
        TheoryData<string, BitWidth> data = new();
        foreach (string operation in new[] { "Rol", "Ror", "Rcl", "Rcr" }) {
            foreach (BitWidth width in new[] { BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32 }) {
                data.Add(operation, width);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(RotateOperations))]
    public void RotateExpansionComputesTheSameResultAndFlagsAsTheAlu(string operation, BitWidth width) {
        // Arrange
        DataType type = DataType.UnsignedFromBitWidth(width);
        RegisterNode operand = new(type, (int)RegisterIndex.BxIndex);
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State expansionState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        for (int count = 0; count <= 33; count++) {
            MethodCallValueNode call = new(type, $"Alu{(int)width}", operation, operand,
                new ConstantNode(DataType.INT32, (ulong)count));
            AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
            (expansion is not null).Should().Be(ShouldExpandRotate(operation, width, count),
                $"{operation}{(int)width} by {count}");
            if (expansion is null) {
                continue;
            }
            Action<State, Memory> run = CompileProgram(expansion, width);
            foreach (uint value in ShiftInputs(width, 2000)) {
                foreach (bool preset in new[] { false, true }) {
                    SetStatusFlags(expansionState, preset);
                    SetStatusFlags(aluState, preset);
                    SetShiftOperand(expansionState, width, value);
                    run(expansionState, memory);
                    uint expected = RunAluRotate(aluState, operation, width, value, count);
                    string? mismatch = Compare(expected, aluState, ReadResult(expansionState, width), expansionState);
                    if (mismatch is not null) {
                        mismatches.Add($"{operation}{(int)width}(0x{value:X}, {count}), flags preset {preset}: {mismatch}");
                    }
                }
            }
        }

        // Assert
        mismatches.Count.Should().Be(0, string.Join(Environment.NewLine, mismatches.Take(ReportedMismatches)));
    }

    [Theory]
    [MemberData(nameof(RotateOperations))]
    public void RotateExpansionWritesTheFlagsOfTheAluFlagTable(string operation, BitWidth width) {
        foreach (int count in new[] { 1, 4, 9 }) {
            // Arrange
            DataType type = DataType.UnsignedFromBitWidth(width);
            RegisterNode operand = new(type, (int)RegisterIndex.BxIndex);
            AluExpansion? expansion = AluExpansionBuilder.TryExpand(
                new MethodCallValueNode(type, $"Alu{(int)width}", operation, operand, new ConstantNode(DataType.INT32, (ulong)count)));
            if (expansion is null) {
                continue;
            }
            AluFlagTable.TryGetEffect(operation, width, (ulong)count, out FlagEffect expected);

            // Act
            FlagEffect actual = FlagEffectVisitor.Of(new BlockNode([.. expansion.Statements]));
            StatusFlags assigned = expansion.FlagAssignments.Aggregate(StatusFlags.None, (all, assignment) => all | assignment.Flag);

            // Assert
            actual.Should().Be(expected);
            assigned.Should().Be(expected.MustWrite);
        }
    }

    private static bool ShouldExpandRotate(string operation, BitWidth width, int count) {
        int w = (int)width;
        int m = count & 0x1F;
        if (m == 0) {
            return false;
        }
        if (operation is "Rol" or "Ror") {
            return m % w != 0;
        }
        return w == 32 || m % (w + 1) != 0;
    }

    private static Action<State, Memory> CompileProgram(AluExpansion expansion, BitWidth width) {
        DataType type = DataType.UnsignedFromBitWidth(width);
        RegisterNode destination = new(type, (int)RegisterIndex.AxIndex);
        BinaryOperationNode assignResult = new(type, destination, BinaryOperation.ASSIGN, expansion.Result.Reference);
        BlockNode program = new([.. expansion.Statements, assignResult]);
        AstExpressionBuilder builder = new();
        return builder.ToAction(program.Accept(builder)).Compile();
    }

    private static uint RunAluRotate(State state, string operation, BitWidth width, uint value, int count) => width switch {
        BitWidth.BYTE_8 => RunAluRotate(new Alu8(state), operation, (byte)value, count),
        BitWidth.WORD_16 => RunAluRotate(new Alu16(state), operation, (ushort)value, count),
        _ => RunAluRotate(new Alu32(state), operation, value, count)
    };

    private static uint RunAluRotate<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper>(
        Alu<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper> alu, string operation, TUnsigned value, int count)
        where TUnsigned : IUnsignedNumber<TUnsigned>
        where TSigned : ISignedNumber<TSigned>
        where TUnsignedUpper : IUnsignedNumber<TUnsignedUpper>
        where TSignedUpper : ISignedNumber<TSignedUpper> {
        TUnsigned result = operation switch {
            "Rol" => alu.Rol(value, (byte)count),
            "Ror" => alu.Ror(value, count),
            "Rcl" => alu.Rcl(value, (byte)count),
            _ => alu.Rcr(value, count)
        };
        return uint.CreateTruncating(result);
    }

    public static TheoryData<string, BitWidth> MultiplyOperations() {
        TheoryData<string, BitWidth> data = new();
        foreach (string operation in new[] { "Mul", "Imul" }) {
            foreach (BitWidth width in new[] { BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32 }) {
                data.Add(operation, width);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(MultiplyOperations))]
    public void MultiplyExpansionComputesTheSameResultAndFlagsAsTheAlu(string operation, BitWidth width) {
        // Arrange
        Action<State, Memory> expansion = CompileMultiplyExpansion(operation, width);
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State expansionState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        foreach ((uint left, uint right) in MultiplyInputs(width)) {
            foreach (bool preset in new[] { false, true }) {
                SetStatusFlags(expansionState, preset);
                SetStatusFlags(aluState, preset);
                SetOperands(expansionState, width, left, right);
                expansion(expansionState, memory);
                (uint expectedLow, uint expectedHigh) = RunAluMultiply(aluState, operation, width, left, right);
                string? mismatch = Compare(expectedLow, aluState, ReadResult(expansionState, width), expansionState);
                uint actualHigh = ReadHighResult(expansionState, width);
                if (expectedHigh != actualHigh) {
                    mismatch = $"{mismatch}; high 0x{actualHigh:X} instead of 0x{expectedHigh:X}".TrimStart("; ".ToCharArray());
                }
                if (mismatch is not null) {
                    mismatches.Add($"{operation}{(int)width}(0x{left:X}, 0x{right:X}), flags preset {preset}: {mismatch}");
                }
            }
        }

        // Assert
        mismatches.Count.Should().Be(0, string.Join(Environment.NewLine, mismatches.Take(ReportedMismatches)));
    }

    [Theory]
    [MemberData(nameof(MultiplyOperations))]
    public void MultiplyExpansionWritesTheFlagsOfTheAluFlagTable(string operation, BitWidth width) {
        // Arrange
        AluExpansion expansion = ExpandMultiply(operation, width);
        AluFlagTable.TryGetEffect(operation, width, null, out FlagEffect expected);

        // Act
        FlagEffect actual = FlagEffectVisitor.Of(new BlockNode([.. expansion.Statements]));
        StatusFlags assigned = expansion.FlagAssignments.Aggregate(StatusFlags.None, (all, assignment) => all | assignment.Flag);

        // Assert
        actual.Should().Be(expected);
        assigned.Should().Be(expected.MustWrite);
    }

    private static AluExpansion ExpandMultiply(string operation, BitWidth width) {
        DataType type = DataType.UnsignedFromBitWidth(width);
        DataType signed = DataType.SignedFromBitWidth(width);
        BitWidth doubleWidth = (BitWidth)(2 * (int)width);
        RegisterNode left = new(type, (int)RegisterIndex.BxIndex);
        RegisterNode right = new(type, (int)RegisterIndex.CxIndex);
        DataType resultType = operation == "Mul"
            ? DataType.UnsignedFromBitWidth(doubleWidth)
            : DataType.SignedFromBitWidth(doubleWidth);
        MethodCallValueNode call = operation == "Mul"
            ? new(resultType, $"Alu{(int)width}", operation, left, right)
            : new(resultType, $"Alu{(int)width}", operation, new TypeConversionNode(signed, left),
                new TypeConversionNode(signed, right));
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(call);
        if (expansion is null) {
            throw new InvalidOperationException($"{operation}{(int)width} was not expanded");
        }
        return expansion;
    }

    private static Action<State, Memory> CompileMultiplyExpansion(string operation, BitWidth width) {
        AluExpansion expansion = ExpandMultiply(operation, width);
        DataType type = DataType.UnsignedFromBitWidth(width);
        VariableReferenceNode result = expansion.Result.Reference;
        RegisterNode lowRegister = new(type, (int)RegisterIndex.AxIndex);
        RegisterNode highRegister = width switch {
            BitWidth.BYTE_8 => new RegisterNode(DataType.UINT8, 4),
            _ => new RegisterNode(type, (int)RegisterIndex.DxIndex)
        };
        BinaryOperationNode assignLow = new(type, lowRegister, BinaryOperation.ASSIGN,
            new TypeConversionNode(type, result));
        BinaryOperationNode assignHigh = new(type, highRegister, BinaryOperation.ASSIGN,
            new TypeConversionNode(type, new BinaryOperationNode(result.DataType, result, BinaryOperation.RIGHT_SHIFT,
                new ConstantNode(DataType.INT32, (ulong)(int)width))));
        BlockNode program = new([.. expansion.Statements, assignLow, assignHigh]);
        AstExpressionBuilder builder = new();
        return builder.ToAction(program.Accept(builder)).Compile();
    }

    private static IEnumerable<(uint Left, uint Right)> MultiplyInputs(BitWidth width) {
        if (width == BitWidth.BYTE_8) {
            for (uint left = 0; left < 256; left++) {
                for (uint right = 0; right < 256; right++) {
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
        for (int i = 0; i < 20000; i++) {
            uint left = (uint)random.NextInt64(0, 1L << 32) & mask;
            uint right = (uint)random.NextInt64(0, 1L << 32) & mask;
            yield return (left, right);
        }
    }

    private static uint ReadHighResult(State state, BitWidth width) => width switch {
        BitWidth.BYTE_8 => state.AH,
        BitWidth.WORD_16 => state.DX,
        _ => state.EDX
    };

    private static (uint Low, uint High) RunAluMultiply(State state, string operation, BitWidth width,
        uint left, uint right) => width switch {
        BitWidth.BYTE_8 => RunAluMultiply(new Alu8(state), operation, (int)width, (byte)left, (byte)right),
        BitWidth.WORD_16 => RunAluMultiply(new Alu16(state), operation, (int)width, (ushort)left, (ushort)right),
        _ => RunAluMultiply(new Alu32(state), operation, (int)width, left, right)
    };

    private static (uint Low, uint High) RunAluMultiply<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper>(
        Alu<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper> alu, string operation, int width,
        TUnsigned left, TUnsigned right)
        where TUnsigned : IUnsignedNumber<TUnsigned>
        where TSigned : ISignedNumber<TSigned>
        where TUnsignedUpper : IUnsignedNumber<TUnsignedUpper>
        where TSignedUpper : ISignedNumber<TSignedUpper> {
        ulong product = operation == "Mul"
            ? ulong.CreateTruncating(alu.Mul(left, right))
            : ulong.CreateTruncating(alu.Imul(TSigned.CreateTruncating(left), TSigned.CreateTruncating(right)));
        ulong lowMask = (1UL << width) - 1;
        return ((uint)(product & lowMask), (uint)((product >> width) & lowMask));
    }

    private static AluExpansion ExpandShift(string operation, BitWidth width, int count) {
        DataType type = DataType.UnsignedFromBitWidth(width);
        RegisterNode operand = new(type, (int)RegisterIndex.BxIndex);
        AluExpansion? expansion = AluExpansionBuilder.TryExpand(
            new MethodCallValueNode(type, $"Alu{(int)width}", operation, operand, new ConstantNode(DataType.INT32, (ulong)count)));
        if (expansion is null) {
            throw new InvalidOperationException($"{operation}{(int)width} was not expanded");
        }
        return expansion;
    }

    private static Action<State, Memory> CompileShiftExpansion(string operation, BitWidth width, int count) {
        DataType type = DataType.UnsignedFromBitWidth(width);
        AluExpansion expansion = ExpandShift(operation, width, count);
        RegisterNode destination = new(type, (int)RegisterIndex.AxIndex);
        BinaryOperationNode assignResult = new(type, destination, BinaryOperation.ASSIGN, expansion.Result.Reference);
        BlockNode program = new([.. expansion.Statements, assignResult]);
        AstExpressionBuilder builder = new();
        return builder.ToAction(program.Accept(builder)).Compile();
    }

    private static IEnumerable<uint> ShiftInputs(BitWidth width, int randomCount) {
        if (width == BitWidth.BYTE_8) {
            for (uint value = 0; value < 256; value++) {
                yield return value;
            }
            yield break;
        }
        uint[] edges = width == BitWidth.WORD_16 ? EdgeValues16 : EdgeValues32;
        foreach (uint value in edges) {
            yield return value;
        }
        uint mask = width == BitWidth.WORD_16 ? 0xFFFFu : 0xFFFFFFFFu;
        Random random = new(RandomSeed);
        for (int i = 0; i < randomCount; i++) {
            yield return (uint)random.NextInt64(0, 1L << 32) & mask;
        }
    }

    private static void SetShiftOperand(State state, BitWidth width, uint value) {
        switch (width) {
            case BitWidth.BYTE_8:
                state.BL = (byte)value;
                break;
            case BitWidth.WORD_16:
                state.BX = (ushort)value;
                break;
            default:
                state.EBX = value;
                break;
        }
    }

    private static uint RunAluShift(State state, string operation, BitWidth width, uint value, int count) => width switch {
        BitWidth.BYTE_8 => RunAluShift(new Alu8(state), operation, (byte)value, count),
        BitWidth.WORD_16 => RunAluShift(new Alu16(state), operation, (ushort)value, count),
        _ => RunAluShift(new Alu32(state), operation, value, count)
    };

    private static uint RunAluShift<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper>(
        Alu<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper> alu, string operation, TUnsigned value, int count)
        where TUnsigned : IUnsignedNumber<TUnsigned>
        where TSigned : ISignedNumber<TSigned>
        where TUnsignedUpper : IUnsignedNumber<TUnsignedUpper>
        where TSignedUpper : ISignedNumber<TSignedUpper> {
        TUnsigned result = operation switch {
            "Shl" => alu.Shl(value, count),
            "Shr" => alu.Shr(value, count),
            _ => alu.Sar(value, count)
        };
        return uint.CreateTruncating(result);
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
            "Adc" => alu.Adc(left, right),
            "Sbb" => alu.Sbb(left, right),
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
