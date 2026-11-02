namespace Spice86.Tests.CfgCodeGeneration.Relations;

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
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Relations;
using Spice86.Shared.Emulator.Memory;

using System.Numerics;

using Xunit;

public class FlagRelationRulesTest {
    private const int RandomPairs = 20000;
    private const int RandomSeed = 20240607;
    private const int ReportedMismatches = 20;
    private const int LeftOperand = 1;
    private const int RightOperand = 2;
    private const int ResultOperand = 4;
    private static readonly string[] Operations = ["Sub", "And", "Or", "Xor", "Add", "Inc", "Dec", "Adc", "Sbb",
        "Shl", "Shr", "Sar", "Rol", "Ror"];
    private static readonly BitWidth[] Widths = [BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32];
    private static readonly AstBuilder Builder = new();
    private static readonly object CountsLock = new();
    private static readonly Lazy<Action<State, Memory>[]> ConditionPrograms = new(BuildConditionPrograms);
    private static readonly uint[] EdgeValues16 = [0x0000, 0x0001, 0x0002, 0x000F, 0x0010, 0x007F, 0x0080, 0x00FF,
        0x0100, 0x7FFE, 0x7FFF, 0x8000, 0x8001, 0xFFFE, 0xFFFF];
    private static readonly uint[] EdgeValues32 = [0x00000000, 0x00000001, 0x00000002, 0x0000000F, 0x00000010,
        0x000000FF, 0x00000100, 0x00007FFF, 0x00008000, 0x0000FFFF, 0x00010000, 0x7FFFFFFE, 0x7FFFFFFF,
        0x80000000, 0x80000001, 0xFFFFFFFE, 0xFFFFFFFF];

    public static TheoryData<string, BitWidth, int, string> RelationCases() {
        TheoryData<string, BitWidth, int, string> data = new();
        foreach (string operation in Operations) {
            foreach (BitWidth width in Widths) {
                for (int code = 0; code < 16; code++) {
                    foreach (string form in new[] { "result", "operands", "both", "resright" }) {
                        data.Add(operation, width, code, form);
                    }
                }
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(RelationCases))]
    public void RelationMatchesTheFlagConditionOfTheSetter(string operation, BitWidth width, int code, string form) {
        // Arrange
        DataType type = DataType.UnsignedFromBitWidth(width);
        RegisterNode left = new(type, (int)RegisterIndex.BxIndex);
        RegisterNode right = new(type, (int)RegisterIndex.CxIndex);
        RegisterNode result = new(type, (int)RegisterIndex.DxIndex);
        FlagRelationOperands operands = form switch {
            "result" => new FlagRelationOperands(null, null, result),
            "operands" => new FlagRelationOperands(left, HasRight(operation) ? right : null, null),
            "resright" => new FlagRelationOperands(null, HasRight(operation) ? right : null, result),
            _ => new FlagRelationOperands(left, HasRight(operation) ? right : null, result)
        };
        Action<State, Memory> condition = ConditionPrograms.Value[code];
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State aluState = new(CpuModel.INTEL_80286);
        State runState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();
        Dictionary<int, Action<State, Memory>> programs = new();
        long compared = 0;
        long skipped = 0;

        // Act
        foreach ((uint l, uint r, int? count) in Inputs(operation, width)) {
            ValueNode? relation = FlagRelationRules.TryRelation(
                new FlagSetter(Enum.Parse<AluOperation>(operation), width, count), code, operands);
            if (relation is null) {
                skipped++;
                continue;
            }
            int key = count ?? -1;
            if (!programs.TryGetValue(key, out Action<State, Memory>? program)) {
                program = CompileAssign(relation);
                programs.Add(key, program);
            }
            foreach (bool preset in CarryPresets(operation)) {
                aluState.CarryFlag = preset;
                uint expected = RunAlu(aluState, operation, width, l, r, count);
                condition(aluState, memory);
                bool expectedCondition = aluState.AuxiliaryFlag;
                SetRegisters(runState, width, l, r, expected);
                program(runState, memory);
                compared++;
                if (runState.AuxiliaryFlag != expectedCondition) {
                    mismatches.Add($"{operation}{(int)width}(0x{l:X}, 0x{r:X}, {count}), code {code}, {form}, " +
                        $"cf {preset}: {runState.AuxiliaryFlag} instead of {expectedCondition}");
                }
            }
        }
        RecordCounts(operation, width, code, form, compared, skipped);

        // Assert
        mismatches.Count.Should().Be(0, string.Join(Environment.NewLine, mismatches.Take(ReportedMismatches)));
    }

    public static TheoryData<string, int, int, int?, bool, bool> CoverageCases() {
        TheoryData<string, int, int, int?, bool, bool> data = new();
        void Add(string operation, int code, int mask, int? count, bool neg, bool removable) =>
            data.Add(operation, code, mask, count, neg, removable);
        int lr = LeftOperand | RightOperand;
        int res = ResultOperand;
        int resLeft = res | LeftOperand;
        int resRight = res | RightOperand;
        Add("Sub", 2, lr, null, false, true); Add("Sub", 2, res, null, true, false); Add("Sub", 2, resRight, null, false, true);
        Add("Sub", 3, lr, null, false, true); Add("Sub", 3, res, null, true, false); Add("Sub", 3, resRight, null, false, true);
        foreach (int code in new[] { 4, 5, 8, 9 }) {
            Add("Sub", code, res, null, false, true); Add("Sub", code, lr, null, false, true);
        }
        foreach (int code in new[] { 6, 7, 12, 13, 14, 15 }) {
            Add("Sub", code, lr, null, false, true);
        }
        foreach (int code in new[] { 0, 1, 2, 3 }) {
            Add("And", code, 0, null, false, false);
            Add("Or", code, 0, null, false, false);
            Add("Xor", code, 0, null, false, false);
        }
        foreach (int code in new[] { 4, 5, 6, 7, 8, 9, 12, 13, 14, 15 }) {
            Add("And", code, res, null, false, true); Add("And", code, lr, null, false, true);
            Add("Or", code, res, null, false, true); Add("Xor", code, res, null, false, true);
        }
        foreach (int code in new[] { 4, 5, 8, 9 }) {
            Add("Add", code, res, null, false, true);
            Add("Inc", code, res, null, false, true); Add("Dec", code, res, null, false, true);
            Add("Adc", code, res, null, false, true); Add("Sbb", code, res, null, false, true);
        }
        Add("Add", 2, resLeft, null, false, true); Add("Add", 3, resLeft, null, false, true);
        Add("Add", 2, resRight, null, false, true); Add("Add", 3, resRight, null, false, true);
        Add("Inc", 0, res, null, false, true); Add("Inc", 1, res, null, false, true);
        Add("Dec", 0, res, null, false, true); Add("Dec", 1, res, null, false, true);
        foreach (string operation in new[] { "Shl", "Shr" }) {
            Add(operation, 2, LeftOperand, 1, false, true); Add(operation, 3, LeftOperand, 1, false, true);
            Add(operation, 0, LeftOperand, 1, false, true); Add(operation, 1, LeftOperand, 1, false, true);
        }
        Add("Sar", 2, LeftOperand, 1, false, true); Add("Sar", 3, LeftOperand, 1, false, true);
        Add("Sar", 0, 0, 1, false, false); Add("Sar", 1, 0, 1, false, false);
        foreach (string operation in new[] { "Shl", "Shr", "Sar" }) {
            foreach (int code in new[] { 4, 5, 8, 9 }) {
                Add(operation, code, res, 1, false, true);
            }
        }
        Add("Rol", 2, res, 1, false, true); Add("Rol", 3, res, 1, false, true);
        Add("Ror", 2, res, 1, false, true); Add("Ror", 3, res, 1, false, true);
        return data;
    }

    [Theory]
    [MemberData(nameof(CoverageCases))]
    public void RelationIsAvailableExactlyWhenItsOperandsAre(string operation, int code, int mask, int? count,
        bool neg, bool removable) {
        // Arrange
        FlagSetter setter = new(Enum.Parse<AluOperation>(operation), BitWidth.WORD_16, count);

        // Act & Assert
        FlagRelationRules.TryRelation(setter, code, Operands(mask, neg)).Should().NotBeNull(
            $"{operation} code {code} with mask {mask}");
        if (removable) {
            foreach (int bit in new[] { LeftOperand, RightOperand, ResultOperand }) {
                if ((mask & bit) == 0) {
                    continue;
                }
                FlagRelationRules.TryRelation(setter, code, Operands(mask & ~bit, neg)).Should().BeNull(
                    $"{operation} code {code} without operand bit {bit}");
            }
        }
    }

    [Fact]
    public void OfReadsTheOperationWidthAndConstantCountOfAnAluCall() {
        // Arrange
        RegisterNode al = new(DataType.UINT8, (int)RegisterIndex.AxIndex);
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        RegisterNode cl = new(DataType.UINT8, (int)RegisterIndex.CxIndex);

        // Act & Assert
        FlagSetter.Of(new AluOperationNode(
                new MethodCallValueNode(DataType.UINT8, "Alu8", "Sub", al,
                    new ConstantNode(DataType.UINT8, 7)),
                FlagEffect.None, AluOperation.Sub, BitWidth.BYTE_8))
            .Should().Be(new FlagSetter(AluOperation.Sub, BitWidth.BYTE_8, null));
        FlagSetter.Of(new AluOperationNode(
                new MethodCallValueNode(DataType.UINT16, "Alu16", "Shl", ax,
                    new ConstantNode(DataType.INT32, 1)),
                FlagEffect.None, AluOperation.Shl, BitWidth.WORD_16))
            .Should().Be(new FlagSetter(AluOperation.Shl, BitWidth.WORD_16, 1));
        FlagSetter.Of(new AluOperationNode(
                new MethodCallValueNode(DataType.UINT16, "Alu16", "Shl", ax, cl),
                FlagEffect.None, AluOperation.Shl, BitWidth.WORD_16))
            .Should().Be(new FlagSetter(AluOperation.Shl, BitWidth.WORD_16, null));
        FlagSetter.Of(new AluOperationNode(
                new MethodCallValueNode(DataType.UINT32, "Alu16", "Mul", ax, bx),
                FlagEffect.None, AluOperation.Mul, BitWidth.WORD_16))
            .Should().BeNull();
    }

    [Fact]
    public void ConditionCodeOfReadsSetConditionsAndSingleFlagReads() {
        // Act & Assert
        for (int code = 0; code < 16; code++) {
            FlagRelationRules.ConditionCodeOf(Builder.Flag.BuildSetCondition(code)).Should().Be(code);
        }
        FlagRelationRules.ConditionCodeOf(new CpuFlagNode(Flags.Carry)).Should().Be(2);
        FlagRelationRules.ConditionCodeOf(new CpuFlagNode(Flags.Zero)).Should().Be(4);
        FlagRelationRules.ConditionCodeOf(new CpuFlagNode(Flags.Sign)).Should().Be(8);
        FlagRelationRules.ConditionCodeOf(new CpuFlagNode(Flags.Overflow)).Should().Be(0);
        FlagRelationRules.ConditionCodeOf(new CpuFlagNode(Flags.Parity)).Should().Be(10);
        FlagRelationRules.ConditionCodeOf(new ConstantNode(DataType.BOOL, 1)).Should().BeNull();
    }

    [Fact]
    public void TestAlAlRelationIsAlEqualZeroWithoutAMask() {
        // Arrange
        RegisterNode al = new(DataType.UINT8, (int)RegisterIndex.AxIndex);
        FlagSetter setter = new(AluOperation.And, BitWidth.BYTE_8, null);

        // Act
        ValueNode? relation = FlagRelationRules.TryRelation(setter, 4, new FlagRelationOperands(al, al, null));

        // Assert
        relation.Should().Be(new BinaryOperationNode(DataType.BOOL, al, BinaryOperation.EQUAL,
            new ConstantNode(DataType.UINT8, 0)));
    }

    private static FlagRelationOperands Operands(int mask, bool neg) {
        DataType type = DataType.UINT16;
        ValueNode? left = (mask & LeftOperand) != 0 || neg
            ? neg ? new ConstantNode(type, 0) : new RegisterNode(type, (int)RegisterIndex.BxIndex)
            : null;
        ValueNode? right = (mask & RightOperand) != 0 ? new RegisterNode(type, (int)RegisterIndex.CxIndex) : null;
        ValueNode? result = (mask & ResultOperand) != 0 ? new RegisterNode(type, (int)RegisterIndex.DxIndex) : null;
        return new FlagRelationOperands(left, right, result);
    }

    private static bool HasRight(string operation) => operation is not ("Inc" or "Dec");

    private static bool[] CarryPresets(string operation) =>
        operation is "Adc" or "Sbb" ? [false, true] : [false];

    private static Action<State, Memory>[] BuildConditionPrograms() {
        Action<State, Memory>[] programs = new Action<State, Memory>[16];
        for (int code = 0; code < 16; code++) {
            programs[code] = CompileAssign(Builder.Flag.BuildSetCondition(code));
        }
        return programs;
    }

    private static Action<State, Memory> CompileAssign(ValueNode expression) {
        BlockNode program = new(new BinaryOperationNode(DataType.BOOL, new CpuFlagNode(Flags.Auxiliary),
            BinaryOperation.ASSIGN, expression));
        AstExpressionBuilder builder = new();
        return builder.ToAction(program.Accept(builder)).Compile();
    }

    private static void RecordCounts(string operation, BitWidth width, int code, string form, long compared,
        long skipped) {
        string? path = Environment.GetEnvironmentVariable("SPICE86_RELATION_COUNTS");
        if (path is null) {
            return;
        }
        lock (CountsLock) {
            File.AppendAllText(path,
                $"{operation}\t{(int)width}\t{code}\t{form}\t{compared}\t{skipped}{Environment.NewLine}");
        }
    }

    private static IEnumerable<(uint Left, uint Right, int? Count)> Inputs(string operation, BitWidth width) {
        if (operation is "Shl" or "Shr" or "Sar" or "Rol" or "Ror") {
            foreach (int count in Counts(width)) {
                foreach (uint value in Values(width)) {
                    yield return (value, 0, count);
                }
            }
            yield break;
        }
        if (operation is "Inc" or "Dec") {
            foreach (uint value in Values(width)) {
                yield return (value, 0, null);
            }
            yield break;
        }
        foreach ((uint left, uint right) in Pairs(width)) {
            yield return (left, right, null);
        }
    }

    private static IEnumerable<int> Counts(BitWidth width) {
        int w = (int)width;
        if (width == BitWidth.BYTE_8) {
            for (int count = 1; count <= w - 1; count++) {
                yield return count;
            }
            yield break;
        }
        foreach (int count in new[] { 1, 2, w / 2, w - 1 }) {
            yield return count;
        }
    }

    private static IEnumerable<uint> Values(BitWidth width) {
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
        for (int i = 0; i < RandomPairs; i++) {
            yield return (uint)random.NextInt64(0, 1L << 32) & mask;
        }
    }

    private static IEnumerable<(uint Left, uint Right)> Pairs(BitWidth width) {
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
        for (int i = 0; i < RandomPairs; i++) {
            uint left = (uint)random.NextInt64(0, 1L << 32) & mask;
            uint right = (uint)random.NextInt64(0, 1L << 32) & mask;
            yield return (left, right);
        }
    }

    private static void SetRegisters(State state, BitWidth width, uint left, uint right, uint result) {
        switch (width) {
            case BitWidth.BYTE_8:
                state.BL = (byte)left;
                state.CL = (byte)right;
                state.DL = (byte)result;
                break;
            case BitWidth.WORD_16:
                state.BX = (ushort)left;
                state.CX = (ushort)right;
                state.DX = (ushort)result;
                break;
            default:
                state.EBX = left;
                state.ECX = right;
                state.EDX = result;
                break;
        }
    }

    private static uint RunAlu(State state, string operation, BitWidth width, uint left, uint right, int? count) =>
        width switch {
            BitWidth.BYTE_8 => RunAlu(new Alu8(state), operation, (byte)left, (byte)right, count),
            BitWidth.WORD_16 => RunAlu(new Alu16(state), operation, (ushort)left, (ushort)right, count),
            _ => RunAlu(new Alu32(state), operation, left, right, count)
        };

    private static uint RunAlu<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper>(
        Alu<TUnsigned, TSigned, TUnsignedUpper, TSignedUpper> alu, string operation,
        TUnsigned left, TUnsigned right, int? count)
        where TUnsigned : IUnsignedNumber<TUnsigned>
        where TSigned : ISignedNumber<TSigned>
        where TUnsignedUpper : IUnsignedNumber<TUnsignedUpper>
        where TSignedUpper : ISignedNumber<TSignedUpper> {
        TUnsigned result = operation switch {
            "Add" => alu.Add(left, right),
            "Adc" => alu.Adc(left, right),
            "Sub" => alu.Sub(left, right),
            "Sbb" => alu.Sbb(left, right),
            "And" => alu.And(left, right),
            "Or" => alu.Or(left, right),
            "Xor" => alu.Xor(left, right),
            "Inc" => alu.Inc(left),
            "Dec" => alu.Dec(left),
            "Shl" => alu.Shl(left, count ?? 0),
            "Shr" => alu.Shr(left, count ?? 0),
            "Sar" => alu.Sar(left, count ?? 0),
            "Rol" => alu.Rol(left, (byte)(count ?? 0)),
            "Ror" => alu.Ror(left, count ?? 0),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
        return uint.CreateTruncating(result);
    }
}
