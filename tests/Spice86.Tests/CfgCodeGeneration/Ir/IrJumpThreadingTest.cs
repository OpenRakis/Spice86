namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// Trampoline removal on hand-built IR methods: an empty jump block disappears and its incoming edges
/// are retargeted and retyped, a chain collapses to the last target, and an entry block, a self-jump
/// and a block with several predecessors stay.
/// </summary>
public sealed class IrJumpThreadingTest {
    [Fact]
    public void AnEmptyJumpBlockIsRemovedAndTheEdgeRetargets() {
        // Arrange: source -> trampoline -> target
        CfgInstruction instruction = Instruction(0);
        IrBlock source = Block(0);
        IrBlock trampoline = Block(1);
        IrBlock target = Block(2);
        source.Terminator = new IrJump(instruction, new IrEdge(trampoline, false));
        trampoline.Terminator = new IrJump(instruction, new IrEdge(target, false));
        target.Statements.Add(Comment(instruction));
        target.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, source)], [instruction],
            source, trampoline, target);

        // Act
        IrJumpThreading.Apply(method);

        // Assert
        method.Blocks.Should().NotContain(trampoline);
        IrJump jump = (IrJump)source.Terminator;
        jump.Edge.Target.Should().BeSameAs(target);
    }

    [Fact]
    public void AChainOfTwoTrampolinesCollapsesToTheLastTarget() {
        // Arrange: source -> first -> second -> target
        CfgInstruction instruction = Instruction(0);
        IrBlock source = Block(0);
        IrBlock first = Block(1);
        IrBlock second = Block(2);
        IrBlock target = Block(3);
        source.Terminator = new IrJump(instruction, new IrEdge(first, false));
        first.Terminator = new IrJump(instruction, new IrEdge(second, false));
        second.Terminator = new IrJump(instruction, new IrEdge(target, false));
        target.Statements.Add(Comment(instruction));
        target.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, source)], [instruction],
            source, first, second, target);

        // Act
        IrJumpThreading.Apply(method);

        // Assert
        method.Blocks.Should().HaveCount(2);
        ((IrJump)source.Terminator).Edge.Target.Should().BeSameAs(target);
    }

    [Fact]
    public void AnEntryBlockAndASelfJumpStay() {
        // Arrange: the entry is an empty jump block; another block jumps to itself
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock self = Block(1);
        IrBlock target = Block(2);
        entry.Terminator = new IrJump(instruction, new IrEdge(self, false));
        self.Terminator = new IrJump(instruction, new IrEdge(self, false));
        target.Statements.Add(Comment(instruction));
        target.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, self, target);

        // Act
        IrJumpThreading.Apply(method);

        // Assert
        method.Blocks.Should().Contain(entry);
        method.Blocks.Should().Contain(self);
    }

    [Fact]
    public void RetargetedEdgesRecomputeInsideInstruction() {
        // Arrange: i1's source jumps to a trampoline; the trampoline points at a mid-instruction
        // block of i1 (inside) and at a block starting i2 (outside)
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        IrBlock sourceInside = Block(0);
        IrBlock trampolineInside = Block(1);
        IrBlock insideTarget = Block(2);
        sourceInside.Terminator = new IrJump(first, new IrEdge(trampolineInside, true));
        trampolineInside.Terminator = new IrJump(first, new IrEdge(insideTarget, true));
        insideTarget.Statements.Add(Comment(first));
        insideTarget.Terminator = new IrExit(first, ReturnValue());
        IrMethod inside = Method([Entry(first, sourceInside)], [first],
            sourceInside, trampolineInside, insideTarget);

        IrBlock sourceOutside = Block(0);
        IrBlock trampolineOutside = Block(1);
        IrBlock outsideTarget = Block(2);
        sourceOutside.Terminator = new IrJump(first, new IrEdge(trampolineOutside, false));
        trampolineOutside.Terminator = new IrJump(first, new IrEdge(outsideTarget, false));
        outsideTarget.Statements.Add(new IrStatement(new InstructionStartNode("start"),
            second,
            new IrEffects(IrValues.None, IrValues.None, IrValues.None, IrTokens.None,
                IrTokens.None, false, false)));
        outsideTarget.Terminator = new IrExit(second, ReturnValue());
        IrMethod outside = Method([Entry(first, sourceOutside)], [first, second],
            sourceOutside, trampolineOutside, outsideTarget);

        // Act
        IrJumpThreading.Apply(inside);
        IrJumpThreading.Apply(outside);

        // Assert
        ((IrJump)inside.Blocks[0].Terminator).Edge.InsideInstruction.Should().BeTrue();
        ((IrJump)outside.Blocks[0].Terminator).Edge.InsideInstruction.Should().BeFalse();
    }

    private static IrMethod Method(IrEntry[] entries, ICfgNode[] nodes, params IrBlock[] blocks) =>
        new("M", [.. blocks], entries, new HashSet<ICfgNode>(nodes),
            new Dictionary<ICfgNode, IrInstructionShape>());

    private static IrEntry Entry(ICfgNode node, IrBlock block) => new(node, block);

    private static IrBlock Block(int id) => new(id);

    private static IrStatement Comment(ICfgNode origin) => IrStatement.Create(new CommentNode("s"), origin);

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));
}
