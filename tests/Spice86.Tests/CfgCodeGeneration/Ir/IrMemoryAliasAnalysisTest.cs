namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// <see cref="IrMemoryAliasAnalysis"/> with the method's <c>IrValueRanges</c>: an offset of one
/// register term whose range at the access is bounded and does not wrap 16 bits is an interval,
/// so two same-base accesses whose intervals cannot meet are <see cref="IrAliasRelation.Disjoint"/>.
/// </summary>
public sealed class IrMemoryAliasAnalysisTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Cx = new(DataType.UINT16, 1);
    private static readonly RegisterNode Bx = new(DataType.UINT16, 3);

    private static readonly ConstantNode KnownSegment = new(DataType.UINT16, 0x2000);

    /// <summary><c>BX = BX &amp; 3</c> bounds the index to 0..3: a store at
    /// <c>0x2000:bx+0x100</c> reaches only offsets 0x100..0x104, so it is disjoint from a read at
    /// the constant offset 0x120 the unbounded offset terms could never separate.</summary>
    [Fact]
    public void MaskedIndex_DisjointFromConstantSlot() {
        (IrMethod method, IrBlock block) = SequenceMethod(
            Assign(Bx, new BinaryOperationNode(DataType.UINT16, Bx,
                BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 3))),
            Store(Bx, 0x100, Ax),
            Assign(Cx, WordPointer(0x120)));
        IrSsa ssa = IrSsa.Build(method);
        IrMemoryAliasAnalysis alias = new(method, ssa);

        IrMemoryAccess write = alias.Writes(block, 2).Should().ContainSingle().Subject;
        IrMemoryAccess read = alias.Reads(block, 3).Should().ContainSingle().Subject;
        write.OffsetRange.Should().Be((0x100u, 0x103u));
        alias.Relation(write, read).Should().Be(IrAliasRelation.Disjoint);
    }

    /// <summary>A <c>cmp bx,5; jb</c> guard whose taken target is the access's only dominating
    /// predecessor bounds the entry version of BX to 0..4 there: the store is disjoint from the
    /// constant slot at 0x20.</summary>
    [Fact]
    public void GuardedIndex_DisjointFromConstantSlot() {
        (IrMethod method, IrBlock use) = GuardedMethod(
            new BinaryOperationNode(DataType.UINT8, Bx, BinaryOperation.LESS_THAN,
                new ConstantNode(DataType.UINT16, 5)),
            Store(Bx, 0, Ax),
            Assign(Cx, WordPointer(0x20)));
        IrSsa ssa = IrSsa.Build(method);
        IrMemoryAliasAnalysis alias = new(method, ssa);

        IrMemoryAccess write = alias.Writes(use, 0).Should().ContainSingle().Subject;
        IrMemoryAccess read = alias.Reads(use, 1).Should().ContainSingle().Subject;
        write.OffsetRange.Should().Be((0u, 4u));
        alias.Relation(write, read).Should().Be(IrAliasRelation.Disjoint);
    }

    /// <summary><c>BX = BX &amp; 0xFF</c> at displacement 0xFF80 gives the interval
    /// 0xFF80..0x1007F, which crosses the 16-bit offset end: without the wrap refusal the
    /// interval would wrap onto the low offsets it claims to leave alone, so the pair must stay
    /// <see cref="IrAliasRelation.MayAlias"/>.</summary>
    [Fact]
    public void IndexRangeWrappingSegmentEnd_StaysMayAlias() {
        (IrMethod method, IrBlock block) = SequenceMethod(
            Assign(Bx, new BinaryOperationNode(DataType.UINT16, Bx,
                BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0xFF))),
            Store(Bx, 0xFF80, Ax),
            Assign(Cx, WordPointer(0x8000)));
        IrSsa ssa = IrSsa.Build(method);
        IrMemoryAliasAnalysis alias = new(method, ssa);

        IrMemoryAccess write = alias.Writes(block, 2).Should().ContainSingle().Subject;
        IrMemoryAccess read = alias.Reads(block, 3).Should().ContainSingle().Subject;
        write.OffsetRange.Should().BeNull();
        alias.Relation(write, read).Should().Be(IrAliasRelation.MayAlias);
    }

    /// <summary>A call re-versions BX: the version the store reads is defined by the call, not
    /// by the masked assignment, so its range is full and the pair stays
    /// <see cref="IrAliasRelation.MayAlias"/>.</summary>
    [Fact]
    public void IndexAcrossCall_StaysMayAlias() {
        (IrMethod method, IrBlock block) = SequenceMethod(
            IrStatement.Create(Assign(Bx, new BinaryOperationNode(DataType.UINT16, Bx,
                BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0xFF))),
                Instruction(2)),
            new IrStatement(new CommentNode("call"), Instruction(3),
                new IrEffects(CpuValues.None, CpuValues.None, CpuValues.Registers,
                    IrTokens.None, IrTokens.Memory, false, false)),
            IrStatement.Create(Store(Bx, 0, Ax), Instruction(4)),
            IrStatement.Create(Assign(Cx, WordPointer(0x40)), Instruction(5)));
        IrSsa ssa = IrSsa.Build(method);
        IrMemoryAliasAnalysis alias = new(method, ssa);

        IrMemoryAccess write = alias.Writes(block, 3).Should().ContainSingle().Subject;
        IrMemoryAccess read = alias.Reads(block, 4).Should().ContainSingle().Subject;
        alias.Relation(write, read).Should().Be(IrAliasRelation.MayAlias);
    }

    private static SegmentedPointerNode WordPointer(int offset) =>
        WordPointer(new ConstantNode(DataType.UINT16, (ulong)offset));

    private static SegmentedPointerNode WordPointer(ValueNode offset) =>
        new(DataType.UINT16, KnownSegment, null, offset);

    private static BinaryOperationNode Assign(ValueNode left, ValueNode right) =>
        new(right.DataType, left, BinaryOperation.ASSIGN, right);

    /// <summary><c>segment:[register + displacement] = value</c>.</summary>
    private static BinaryOperationNode Store(RegisterNode index, int displacement,
        ValueNode value) =>
        new(DataType.UINT16, WordPointer(new BinaryOperationNode(DataType.UINT16, index,
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, (ulong)displacement))),
            BinaryOperation.ASSIGN, value);

    private static (IrMethod Method, IrBlock Block) SequenceMethod(
            params IVisitableAstNode[] nodes) =>
        SequenceMethod([.. nodes.Select(node =>
            IrStatement.Create(node, Instruction(0)))]);

    private static (IrMethod Method, IrBlock Block) SequenceMethod(
            params IrStatement[] statements) {
        CfgInstruction entryInstruction = Instruction(0);
        IrBlock entry = new(0);
        IrBlock sequence = new(1);
        IrEditor.WithoutSsa.SetTerminator(entry, new IrJump(entryInstruction, new IrEdge(sequence, false)));
        IrEditor.WithoutSsa.Append(sequence, IrStatement.Create(new InstructionStartNode("nop"),
            Instruction(1)));
        foreach (IrStatement statement in statements) {
            IrEditor.WithoutSsa.Append(sequence, statement);
        }
        IrEditor.WithoutSsa.SetTerminator(sequence, new IrExit(Instruction(0),
            new ReturnNode(new ConstantNode(DataType.UINT16, 0))));
        IrMethod method = new("M", [entry, sequence],
            [new IrEntry(entryInstruction, entry)],
            new HashSet<ICfgNode> { entryInstruction },
            new Dictionary<ICfgNode, IrInstructionShape>());
        return (method, sequence);
    }

    /// <summary>entry -&gt; guard -(&lt;paramref name="condition"/&gt;)-&gt; use -&gt; exit, with
    /// the false arm to exit: the use block's only predecessor is the guard, so the dominating
    /// branch condition refines the compared register version's range inside it.</summary>
    private static (IrMethod Method, IrBlock Use) GuardedMethod(ValueNode condition,
            params IVisitableAstNode[] useNodes) {
        CfgInstruction entryInstruction = Instruction(0);
        IrBlock entry = new(0);
        IrBlock guard = new(1);
        IrBlock use = new(2);
        IrBlock exit = new(3);
        IrEditor.WithoutSsa.SetTerminator(entry, new IrJump(entryInstruction, new IrEdge(guard, false)));
        IrEditor.WithoutSsa.Append(guard, IrStatement.Create(new InstructionStartNode("nop"),
            Instruction(1)));
        IrEditor.WithoutSsa.SetTerminator(guard, new IrBranch(Instruction(2), condition,
            new IrEdge(use, false), new IrEdge(exit, false)));
        foreach ((IVisitableAstNode node, int i) in useNodes.Select((n, i) => (n, i))) {
            IrEditor.WithoutSsa.Append(use, IrStatement.Create(node,
                Instruction((ushort)(i + 3))));
        }
        IrEditor.WithoutSsa.SetTerminator(use, new IrJump(Instruction(0), new IrEdge(exit, false)));
        IrEditor.WithoutSsa.SetTerminator(exit, new IrExit(Instruction(0),
            new ReturnNode(new ConstantNode(DataType.UINT16, 0))));
        IrMethod method = new("M", [entry, guard, use, exit],
            [new IrEntry(entryInstruction, entry)],
            new HashSet<ICfgNode> { entryInstruction },
            new Dictionary<ICfgNode, IrInstructionShape>());
        return (method, use);
    }

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));
}
