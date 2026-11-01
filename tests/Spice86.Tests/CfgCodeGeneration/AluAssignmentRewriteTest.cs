namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;
using Spice86.Tests.CfgCpu.Blocks;

using System.Numerics;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The shared ALU assignment rewrite on real parsed instructions: a declaration or conversion over an
/// ALU call becomes plain code when the call's flags are dead, and keeps the call when the cleaned
/// expansion still needs its temps or its flag write reads more than the result.
/// </summary>
public sealed class AluAssignmentRewriteTest {
    private static IrMethod LoweredAlu(params CfgBlock[] blocks) {
        IrMethod method = Lift(blocks);
        IrPassPipeline.Run(method, IrPass.AluLowering, verify: true);
        return method;
    }

    private static bool IsAluCall(IVisitableAstNode node) => node is AluOperationNode or AluStatementNode;

    private static bool DeclaresPlainAddition(IVisitableAstNode node) =>
        node is VariableDeclarationNode {
            Initializer: BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS } };

    [Fact]
    public void XaddWithDeadFlags_DeclaresTheSumAsPlainAddition() {
        // Arrange: xadd ax, bx / nop - the sum declaration's call has no live flag
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(helper,
            (0x0000, [0x0F, 0xC1, 0xD8]),
            (0x0003, [0x90]));

        // Act
        IrMethod method = LoweredAlu(block);

        // Assert
        Nodes(method).Should().NotContain(node => IsAluCall(node));
        Nodes(method).Should().Contain(node => DeclaresPlainAddition(node));
    }

    [Fact]
    public void XaddWithLiveSignFlag_KeepsTheCall() {
        // Arrange: xadd ax, bx / js - the live flag reads the result temp, so the cleaned
        // expansion keeps two statements and the wrapped rewrite refuses
        TestInstructionHelper helper = new();
        CfgInstruction xadd = Parse(helper, 0x0000, 0x0F, 0xC1, 0xD8);
        CfgInstruction js = Parse(helper, 0x0003, 0x78, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0005, 0x90);
        CfgInstruction taken = Parse(helper, 0x0007, 0x90);
        Chain(xadd, js);
        CfgBlock xaddBlock = Block(1, xadd, js);
        Branch(js, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(xaddBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }

    [Fact]
    public void MulWithLiveCarry_KeepsTheCall() {
        // Arrange: mul cl / jc - the carry formula reads the result temp
        TestInstructionHelper helper = new();
        CfgInstruction mul = Parse(helper, 0x0000, 0xF6, 0xE1);
        CfgInstruction jc = Parse(helper, 0x0002, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0004, 0x90);
        CfgInstruction taken = Parse(helper, 0x0006, 0x90);
        Chain(mul, jc);
        CfgBlock mulBlock = Block(1, mul, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(mulBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }

    [Fact]
    public void ImulWithImmediateAndLiveOverflow_KeepsTheCall() {
        // Arrange: imul ax, bx, 5 / jo - a conversion over the call whose live flag reads the
        // result temp
        TestInstructionHelper helper = new();
        CfgInstruction imul = Parse(helper, 0x0000, 0x6B, 0xC3, 0x05);
        CfgInstruction jo = Parse(helper, 0x0003, 0x70, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0005, 0x90);
        CfgInstruction taken = Parse(helper, 0x0007, 0x90);
        Chain(imul, jo);
        CfgBlock imulBlock = Block(1, imul, jo);
        Branch(jo, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(imulBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }

    [Fact]
    public void SubFromMemoryWithLiveCarry_KeepsTheCall() {
        // Arrange: sub ax, word [0] / jc - the memory operand is read by the result and the carry
        // formula, so the cleaned expansion keeps a statement the rewrite does not handle
        TestInstructionHelper helper = new();
        CfgInstruction add = Parse(helper, 0x0000, 0x2B, 0x06, 0x00, 0x00);
        CfgInstruction jc = Parse(helper, 0x0004, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0006, 0x90);
        CfgInstruction taken = Parse(helper, 0x0008, 0x90);
        Chain(add, jc);
        CfgBlock addBlock = Block(1, add, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(addBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }

    [Fact]
    public void AdcWithLiveCarry_KeepsTheCall() {
        // Arrange: adc ax, bx / jc - the carry formula reads the carry flag itself, so the flag
        // write cannot be rebuilt from the destination
        TestInstructionHelper helper = new();
        CfgInstruction adc = Parse(helper, 0x0000, 0x11, 0xD8);
        CfgInstruction jc = Parse(helper, 0x0002, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0004, 0x90);
        CfgInstruction taken = Parse(helper, 0x0006, 0x90);
        Chain(adc, jc);
        CfgBlock adcBlock = Block(1, adc, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(adcBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluCall(node));
    }

    private const ushort MemoryOffset = 0x10;
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

    public static TheoryData<string, BitWidth> RewrittenOperations() {
        TheoryData<string, BitWidth> data = new();
        foreach (string operation in new[] { "Add", "Sub", "And", "Or", "Xor", "Inc", "Dec" }) {
            foreach (BitWidth width in new[] { BitWidth.BYTE_8, BitWidth.WORD_16, BitWidth.DWORD_32 }) {
                data.Add(operation, width);
            }
        }
        return data;
    }

    /// <summary>The statements the rewrite emits, compiled and run, reproduce the ALU call's result and
    /// live flags for every input; a call the rewrite refuses keeps its own semantics.</summary>
    [Theory]
    [MemberData(nameof(RewrittenOperations))]
    public void RewrittenStatementsMatchTheAluCall(string operation, BitWidth width) {
        // Arrange
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State rewrittenState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        foreach (bool leftInMemory in new[] { false, true }) {
            foreach (bool valueUsed in new[] { true, false }) {
                foreach (StatusFlags live in LiveSets) {
                    Action<State, Memory>? rewritten = CompileRewritten(operation, width, leftInMemory,
                        valueUsed, live, sameOperand: false);
                    if (rewritten is null) {
                        continue;
                    }
                    foreach ((uint left, uint right) in Inputs(operation, width)) {
                        foreach (bool preset in new[] { false, true }) {
                            SetStatusFlags(rewrittenState, preset);
                            SetStatusFlags(aluState, preset);
                            SetOperands(rewrittenState, memory, width, leftInMemory, left, right);
                            rewritten(rewrittenState, memory);
                            uint expected = RunAlu(aluState, operation, width, left, right);
                            string? mismatch = Compare(live, preset, valueUsed, expected, aluState,
                                ReadResult(rewrittenState, width), rewrittenState);
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

    [Theory]
    [InlineData("Xor", BitWidth.BYTE_8)]
    [InlineData("Xor", BitWidth.WORD_16)]
    [InlineData("Xor", BitWidth.DWORD_32)]
    [InlineData("Sub", BitWidth.BYTE_8)]
    [InlineData("Sub", BitWidth.WORD_16)]
    [InlineData("Sub", BitWidth.DWORD_32)]
    public void SelfCancellingRewriteMatchesTheAluCall(string operation, BitWidth width) {
        // Arrange
        uint[] values = width switch {
            BitWidth.BYTE_8 => EdgeValues8,
            BitWidth.WORD_16 => EdgeValues16,
            _ => EdgeValues32
        };
        Memory memory = new(new(), new Ram(64), new(), new RealModeMmu386(), false);
        State rewrittenState = new(CpuModel.INTEL_80286);
        State aluState = new(CpuModel.INTEL_80286);
        List<string> mismatches = new();

        // Act
        foreach (bool valueUsed in new[] { true, false }) {
            foreach (StatusFlags live in LiveSets) {
                Action<State, Memory>? rewritten = CompileRewritten(operation, width, leftInMemory: false,
                    valueUsed, live, sameOperand: true);
                if (rewritten is null) {
                    continue;
                }
                foreach (uint value in values) {
                    foreach (bool preset in new[] { false, true }) {
                        SetStatusFlags(rewrittenState, preset);
                        SetStatusFlags(aluState, preset);
                        SetOperands(rewrittenState, memory, width, false, value, value);
                        rewritten(rewrittenState, memory);
                        uint expected = RunAlu(aluState, operation, width, value, value);
                        string? mismatch = Compare(live, preset, valueUsed, expected, aluState,
                            ReadResult(rewrittenState, width), rewrittenState);
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

    /// <summary>The statement <c>AX = AluN.Op(left, right)</c> (or the bare call when the value is unused)
    /// passed through <see cref="AluAssignmentRewrite.TryRewriteStatement"/>, compiled to an action; null when
    /// the rewrite refuses.</summary>
    private static Action<State, Memory>? CompileRewritten(string operation, BitWidth width, bool leftInMemory,
        bool valueUsed, StatusFlags live, bool sameOperand) {
        DataType type = DataType.UnsignedFromBitWidth(width);
        ValueNode left = leftInMemory ? MemoryOperand(type) : new RegisterNode(type, (int)RegisterIndex.BxIndex);
        ValueNode right = sameOperand ? left : new RegisterNode(type, (int)RegisterIndex.CxIndex);
        AluCallValueNode call = new(type, Enum.Parse<AluOperation>(operation), type.BitWidth,
            operation is "Inc" or "Dec" ? [left] : [left, right]);
        IVisitableAstNode statement = StatementOf(valueUsed, type, call);
        if (AluAssignmentRewrite.TryRewriteStatement(statement,
                CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0)), live)
            is not { } nodes) {
            return null;
        }
        BlockNode program = new([.. nodes]);
        AstExpressionBuilder builder = new();
        return builder.ToAction(program.Accept(builder)).Compile();
    }

    private static IVisitableAstNode StatementOf(bool valueUsed, DataType type, AluCallValueNode call) {
        if (valueUsed) {
            return new BinaryOperationNode(type, new RegisterNode(type, (int)RegisterIndex.AxIndex),
                BinaryOperation.ASSIGN, call);
        }
        return call;
    }

    private static SegmentedPointerNode MemoryOperand(DataType type) =>
        new(type, new ConstantNode(DataType.UINT16, 0), null, new ConstantNode(DataType.UINT16, MemoryOffset));

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
