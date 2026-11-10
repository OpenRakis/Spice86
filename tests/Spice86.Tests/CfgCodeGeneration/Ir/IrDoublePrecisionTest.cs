namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu;
using Spice86.Tests.CfgCpu.Blocks;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Xunit;

/// <summary>
/// <see cref="IrDoublePrecision"/>'s counted pair-shift loops: a legal pure-IR
/// <c>shl/rcl + dec/jnz</c> loop collapses to one saturated wide shift when the count provably
/// enters non-zero; a zero or unknown count and any event check keep the loop.
/// </summary>
public sealed class IrDoublePrecisionTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Cx = new(DataType.UINT16, 1);
    private static readonly RegisterNode Dx = new(DataType.UINT16, 2);
    private static readonly CpuFlagNode Carry = new(Flags.Carry);

    private static int _nextOffset;

    [Fact]
    public void CountedShiftLoop_ConstantNonZeroCount_Collapses() {
        // Arrange: cx = 3; do { shl ax,1; rcl dx,1; cx-- } while (cx != 0)
        (IrMethod method, IrBlock loop, IrBlock exit) = Program(ConstantAssign(Cx, 3), []);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int rewritten = IrDoublePrecision.Apply(method, ssa);

        // Assert
        rewritten.Should().Be(1);
        loop.Terminator.Should().BeOfType<IrJump>()
            .Which.Edge.Target.Should().BeSameAs(exit);
        loop.Statements.Select(statement => statement.Node)
            .OfType<VariableDeclarationNode>().Should().Contain(declaration =>
                declaration.DataType == DataType.UINT32);
        loop.Statements.Select(statement => statement.Node)
            .OfType<BinaryOperationNode>().Where(assign =>
                assign.BinaryOperation == BinaryOperation.ASSIGN).Should().Contain(assign =>
                assign.Left.Equals(Cx) && assign.Right.Equals(new ConstantNode(DataType.UINT16, 0)));
    }

    [Fact]
    public void CountedShiftLoop_ZeroCount_Stays() {
        // Arrange: the count provably enters as zero; the do-loop would run 65536 times,
        // so nothing may fold it away.
        (IrMethod method, IrBlock loop, _) = Program(ConstantAssign(Cx, 0), []);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        IrDoublePrecision.Apply(method, ssa);

        // Assert: the inner shl/rcl pair may combine, but the loop itself must stay.
        loop.Terminator.Should().BeOfType<IrBranch>();
    }

    [Fact]
    public void CountedShiftLoop_EventCheckInside_Stays() {
        // Arrange: the event check a real generated loop always carries may deliver an
        // interrupt, so the loop must not collapse.
        (IrMethod method, IrBlock loop, _) = Program(ConstantAssign(Cx, 3),
            [new CheckExternalEventsNode(new SegmentedAddress(0x1000, 0x0100),
                new CallEffect(CpuValues.None, CpuValues.None))]);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        IrDoublePrecision.Apply(method, ssa);

        // Assert
        loop.Terminator.Should().BeOfType<IrBranch>();
    }

    [Fact]
    public void CarryAdd_MutableImmediate_Stays() {
        // Arrange: bx = bx + <parsed imm 1>; cx = cx + (ushort)(bx == 0). A mutable
        // immediate is a memory-resident operand, not the constant 1 the carry proof
        // needs, so the add/adc pair must not combine.
        TestInstructionHelper helper = new();
        InstructionFieldNode one = IrAstChildren.Descendants(
                IrTestPrograms.Parse(helper, 0, 0x83, 0xC3, 0x01).ExecutionAst)
            .OfType<InstructionFieldNode>().Single();
        one.Field.UseValue = false;
        RegisterNode bx = new(DataType.UINT16, 3), cx = new(DataType.UINT16, 1);
        IrBlock block = new(0), exit = Leaf();
        CfgInstruction addOrigin = NextInstruction();
        block.Statements.Add(IrStatement.Create(new InstructionStartNode("add"), addOrigin));
        block.Statements.Add(IrStatement.Create(Assign(bx,
            new BinaryOperationNode(DataType.UINT16, bx, BinaryOperation.PLUS, one)),
            addOrigin));
        CfgInstruction adcOrigin = NextInstruction();
        block.Statements.Add(IrStatement.Create(new InstructionStartNode("adc"), adcOrigin));
        block.Statements.Add(IrStatement.Create(Assign(cx,
            new BinaryOperationNode(DataType.UINT16, cx, BinaryOperation.PLUS,
                new TypeConversionNode(DataType.UINT16,
                    new BinaryOperationNode(DataType.BOOL, bx, BinaryOperation.EQUAL,
                        new ConstantNode(DataType.UINT16, 0))))), adcOrigin));
        block.Terminator = new IrJump(NextInstruction(), new IrEdge(exit, false));
        IrMethod method = new("M", [block, exit],
            [new IrEntry(NextInstruction(), block)],
            new HashSet<ICfgNode>(),
            new Dictionary<ICfgNode, IrInstructionShape>());
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int rewritten = IrDoublePrecision.Apply(method, ssa);

        // Assert
        rewritten.Should().Be(0);
        block.Statements.Select(statement => statement.Node).OfType<BinaryOperationNode>()
            .Where(assign => assign.BinaryOperation == BinaryOperation.ASSIGN)
            .Should().Contain(assign => assign.Left.Equals(bx))
            .And.Contain(assign => assign.Left.Equals(cx));
    }

    /// <summary>The collapsed counted pair-shift loop must compute the real one-bit-at-a-time
    /// result for every count, including saturation at and beyond the pair width and the sign
    /// bit of the arithmetic form. The printed initializer is compiled once per pair width and
    /// shift direction, then compared against a reference one-bit loop over the full count and
    /// packed-input table.</summary>
    [Theory]
    [InlineData(8, "left")]
    [InlineData(8, "right")]
    [InlineData(8, "signedRight")]
    [InlineData(16, "left")]
    [InlineData(16, "right")]
    [InlineData(16, "signedRight")]
    public void CountedShiftLoop_CollapsedWideShift_ComputesTheOneBitLoop(int halfBits,
        string direction) {
        // Arrange: a byte pair is the AL/AH halves of AX; a word pair is AX:DX. The count is
        // the disjoint CX word, proven non-zero by a guard on the single outside predecessor so
        // it stays a compiled parameter rather than a folded constant.
        RegisterNode low = new(halfBits == 8 ? DataType.UINT8 : DataType.UINT16, 0);
        RegisterNode high = new(halfBits == 8 ? DataType.UINT8 : DataType.UINT16,
            halfBits == 8 ? 4 : 2);
        (IrMethod method, IrBlock loop, IrBlock exit) = Program(low, high, direction, Cx);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int rewritten = IrDoublePrecision.Apply(method, ssa);

        // Assert: the loop collapsed into one wide result declaration.
        rewritten.Should().Be(1);
        loop.Terminator.Should().BeOfType<IrJump>()
            .Which.Edge.Target.Should().BeSameAs(exit);
        VariableDeclarationNode declaration = loop.Statements
            .Select(statement => statement.Node).OfType<VariableDeclarationNode>()
            .Should().ContainSingle().Subject;
        DataType wide = halfBits == 8 ? DataType.UINT16 : DataType.UINT32;
        declaration.DataType.Should().Be(wide);

        // The printed initializer compiles to a uint function of the three word parameters.
        string initializer = new CSharpExpressionPrinter(new Dictionary<ushort, string>())
            .Print(new TypeConversionNode(wide, declaration.Initializer)).Text;
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile([
            "public static class Shifts { public static uint Shifted(ushort AX, ushort DX,"
            + $" ushort CX) => {initializer}; }}"]);
        using MemoryStream peStream = compilation.PeStream;
        compilation.EmitResult.Success.Should().BeTrue(
            $"collapsed shift '{initializer}' must compile: "
            + string.Join("; ", compilation.EmitResult.Diagnostics));
        CollectibleAssemblyLoadContext context = new();
        try {
            peStream.Position = 0;
            Type compiled = context.LoadFromStream(peStream).GetType("Shifts")
                ?? throw new InvalidOperationException("Compiled Shifts type missing.");
            MethodInfo shifted = compiled.GetMethod("Shifted")
                ?? throw new InvalidOperationException("Compiled Shifted method missing.");
            ulong pairMask = wide.BitWidth == BitWidth.WORD_16 ? 0xFFFFu : 0xFFFFFFFFu;
            foreach (int count in new[] { 1, 15, 16, 31, 32, 33, 65535 }) {
                foreach (ulong input in new ulong[]
                        { 0, 1, 0x8001, 0xFFFF, 0x80000001, 0xFFFFFFFF }) {
                    ulong expected = input & pairMask;
                    for (int i = 0; i < count; i++) {
                        expected = direction switch {
                            "left" => (expected << 1) & pairMask,
                            "right" => expected >> 1,
                            _ => wide.BitWidth == BitWidth.WORD_16 ? (ushort)((short)expected >> 1) : (uint)((int)expected >> 1)
                        };
                    }
                    shifted.Invoke(null,
                            [(ushort)(input & 0xFFFF), (ushort)(input >> 16), (ushort)count])
                        .Should().Be((uint)(expected & pairMask),
                            $"count {count}, input 0x{input:X8}: '{initializer}'");
                }
            }
        } finally {
            context.Unload();
        }
    }

    private static BinaryOperationNode ConstantAssign(RegisterNode target, ulong value) =>
        new(target.DataType, target, BinaryOperation.ASSIGN,
            new ConstantNode(target.DataType, value));

    /// <summary>The <c>shl ax,1; rcl dx,1; dec cx; jnz loop</c> loop of the tests, entered from
    /// a single preheader running <paramref name="preheader"/> then jumping in.
    /// <paramref name="extra"/> appends extra statements to the loop body (own origin).</summary>
    private static (IrMethod Method, IrBlock Loop, IrBlock Exit) Program(
        IVisitableAstNode preheader, IVisitableAstNode[] extra) {
        IrBlock loop = new(1);
        IrBlock exit = Leaf();
        IrBlock pre = new(0);
        pre.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"), NextInstruction()));
        pre.Statements.Add(IrStatement.Create(preheader, NextInstruction()));
        pre.Terminator = new IrJump(NextInstruction(), new IrEdge(loop, false));

        AddShiftRuns(loop, Ax, Dx, "left");
        // dec cx
        CfgInstruction decOrigin = NextInstruction();
        loop.Statements.Add(IrStatement.Create(new InstructionStartNode("dec"), decOrigin));
        loop.Statements.Add(IrStatement.Create(Assign(Cx,
            new BinaryOperationNode(DataType.UINT16, Cx, BinaryOperation.MINUS,
                new ConstantNode(DataType.UINT16, 1))), decOrigin));
        foreach (IVisitableAstNode node in extra) {
            loop.Statements.Add(IrStatement.Create(node, NextInstruction()));
        }
        loop.Terminator = new IrBranch(NextInstruction(),
            new BinaryOperationNode(DataType.BOOL, Cx, BinaryOperation.NOT_EQUAL,
                new ConstantNode(DataType.UINT16, 0)),
            new IrEdge(loop, false), new IrEdge(exit, false));

        IrMethod method = new("M", [pre, loop, exit],
            [new IrEntry(NextInstruction(), pre)],
            new HashSet<ICfgNode>(),
            new Dictionary<ICfgNode, IrInstructionShape>());
        return (method, loop, exit);
    }

    /// <summary>The same counted loop entered through a <c>count != 0</c> guard on the single
    /// outside predecessor, so <paramref name="count"/> stays a runtime value of the collapsed
    /// shift. <paramref name="direction"/> selects the <c>shl/rcl</c>, <c>shr/rcr</c> or
    /// <c>sar/rcr</c> body shape on the <paramref name="low"/>/<paramref name="high"/> halves;
    /// the count is the disjoint <paramref name="count"/> word.</summary>
    private static (IrMethod Method, IrBlock Loop, IrBlock Exit) Program(
        RegisterNode low, RegisterNode high, string direction, RegisterNode count) {
        IrBlock loop = new(1);
        IrBlock exit = Leaf();
        IrBlock pre = new(0);
        pre.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"), NextInstruction()));
        pre.Terminator = new IrBranch(NextInstruction(),
            new BinaryOperationNode(DataType.BOOL, count, BinaryOperation.NOT_EQUAL,
                new ConstantNode(count.DataType, 0)),
            new IrEdge(loop, false), new IrEdge(exit, false));
        AddShiftRuns(loop, low, high, direction);
        CfgInstruction decOrigin = NextInstruction();
        loop.Statements.Add(IrStatement.Create(new InstructionStartNode("dec"), decOrigin));
        loop.Statements.Add(IrStatement.Create(Assign(count,
            new BinaryOperationNode(count.DataType, count, BinaryOperation.MINUS,
                new ConstantNode(count.DataType, 1))), decOrigin));
        loop.Terminator = new IrBranch(NextInstruction(),
            new BinaryOperationNode(DataType.BOOL, count, BinaryOperation.NOT_EQUAL,
                new ConstantNode(count.DataType, 0)),
            new IrEdge(loop, false), new IrEdge(exit, false));
        IrMethod method = new("M", [pre, loop, exit],
            [new IrEntry(NextInstruction(), pre)],
            new HashSet<ICfgNode>(),
            new Dictionary<ICfgNode, IrInstructionShape>());
        return (method, loop, exit);
    }

    /// <summary>Appends the pair-shift runs of the counted loop: for <paramref name="direction"/>
    /// <c>"left"</c> the carry-out + <c>shl low,1</c> run then <c>rcl high,1</c>, otherwise the
    /// carry-out + <c>shr/sar high,1</c> run then <c>rcr low,1</c>. The arithmetic form carries
    /// the signed same-width cast on its shifted operand.</summary>
    private static void AddShiftRuns(IrBlock loop, RegisterNode low, RegisterNode high,
        string direction) {
        DataType half = low.DataType;
        int halfWidth = half.BitWidth == BitWidth.BYTE_8 ? 8 : 16;
        ConstantNode one = new(DataType.INT32, 1);
        CfgInstruction shiftOrigin = NextInstruction();
        loop.Statements.Add(IrStatement.Create(
            new InstructionStartNode(direction == "left" ? "shl"
                : direction == "signedRight" ? "sar" : "shr"), shiftOrigin));
        RegisterNode carryOut = direction == "left" ? low : high;
        loop.Statements.Add(IrStatement.Create(Assign(Carry,
            new BinaryOperationNode(DataType.BOOL,
                new BinaryOperationNode(half, carryOut, BinaryOperation.BITWISE_AND,
                    new ConstantNode(half,
                        (ulong)(direction == "left" ? 1 << (halfWidth - 1) : 1))),
                BinaryOperation.NOT_EQUAL, new ConstantNode(half, 0))), shiftOrigin));
        if (direction == "left") {
            loop.Statements.Add(IrStatement.Create(Assign(low,
                new BinaryOperationNode(half, low, BinaryOperation.LEFT_SHIFT, one)),
                shiftOrigin));
            CfgInstruction rclOrigin = NextInstruction();
            loop.Statements.Add(IrStatement.Create(new InstructionStartNode("rcl"), rclOrigin));
            loop.Statements.Add(IrStatement.Create(Assign(high,
                new BinaryOperationNode(half,
                    new BinaryOperationNode(half, high, BinaryOperation.LEFT_SHIFT, one),
                    BinaryOperation.BITWISE_OR,
                    new TypeConversionNode(half, Carry))), rclOrigin));
        } else {
            DataType signedHalf = DataType.SignedFromBitWidth(half.BitWidth);
            ValueNode shifted = direction == "signedRight"
                ? new TypeConversionNode(half,
                    new BinaryOperationNode(signedHalf,
                        new TypeConversionNode(signedHalf, high),
                        BinaryOperation.RIGHT_SHIFT, one))
                : new BinaryOperationNode(half, high, BinaryOperation.RIGHT_SHIFT, one);
            loop.Statements.Add(IrStatement.Create(Assign(high, shifted), shiftOrigin));
            CfgInstruction rcrOrigin = NextInstruction();
            loop.Statements.Add(IrStatement.Create(new InstructionStartNode("rcr"), rcrOrigin));
            loop.Statements.Add(IrStatement.Create(Assign(low,
                new BinaryOperationNode(half,
                    new BinaryOperationNode(half, low, BinaryOperation.RIGHT_SHIFT, one),
                    BinaryOperation.BITWISE_OR,
                    new BinaryOperationNode(half, new TypeConversionNode(half, Carry),
                        BinaryOperation.LEFT_SHIFT,
                        new ConstantNode(DataType.INT32, (ulong)(halfWidth - 1))))),
                rcrOrigin));
        }
    }

    private static BinaryOperationNode Assign(ValueNode left, ValueNode right) =>
        new(right.DataType, left, BinaryOperation.ASSIGN, right);

    private static IrBlock Leaf() {
        IrBlock block = new(2);
        block.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"),
            NextInstruction()));
        block.Terminator = new IrExit(NextInstruction(), new FailAsUntestedNode([]));
        return block;
    }

    private static CfgInstruction NextInstruction() =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000,
            (ushort)(_nextOffset++ * 4)));
}
