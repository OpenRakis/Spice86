namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary><see cref="IrMethod.ToBlockGraph"/> on a three-block method.</summary>
public sealed class IrMethodTest {
    [Fact]
    public void ToBlockGraph_ExposesEdgesEntriesAndStableOrder() {
        // Arrange
        CfgInstruction origin = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0));
        IrBlock entry = new(0);
        IrBlock whenTrue = new(1);
        IrBlock join = new(2);
        entry.Terminator = new IrBranch(origin, new CpuFlagNode(Flags.Carry),
            new IrEdge(whenTrue, true), new IrEdge(join, true));
        whenTrue.Terminator = new IrJump(origin, new IrEdge(join, true));
        join.Terminator = new IrExit(origin, new ReturnNode(new ConstantNode(DataType.UINT16, 0)));
        IrMethod method = new("M", [entry, whenTrue, join],
            [new IrEntry(origin, entry)], new HashSet<ICfgNode> { origin },
            new Dictionary<ICfgNode, IrInstructionShape>());

        // Act
        BlockGraph<IrBlock> graph = method.ToBlockGraph();

        // Assert
        graph.Blocks.Should().Equal(entry, whenTrue, join);
        graph.EntryBlocks.Should().Equal(entry);
        graph.PrimaryEntryBlock.Should().BeSameAs(entry);
        graph.GetSuccessors(entry).Should().Equal(whenTrue, join);
        graph.GetSuccessors(whenTrue).Should().Equal(join);
        graph.GetSuccessors(join).Should().BeEmpty();
        graph.GetFallthroughSuccessor(entry).Should().BeNull();
        graph.StableOrder.Compare(whenTrue, join).Should().BeNegative();
    }

    [Fact]
    public void ToBlockGraph_DeduplicatesSuccessorsAndEntries() {
        // Arrange
        CfgInstruction origin = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0));
        IrBlock entry = new(0);
        IrBlock target = new(1);
        entry.Terminator = new IrBranch(origin, new CpuFlagNode(Flags.Carry),
            new IrEdge(target, true), new IrEdge(target, true));
        target.Terminator = new IrExit(origin, new ReturnNode(new ConstantNode(DataType.UINT16, 0)));
        IrMethod method = new("M", [entry, target],
            [new IrEntry(origin, entry), new IrEntry(origin, entry)], new HashSet<ICfgNode> { origin },
            new Dictionary<ICfgNode, IrInstructionShape>());

        // Act
        BlockGraph<IrBlock> graph = method.ToBlockGraph();

        // Assert
        graph.GetSuccessors(entry).Should().Equal(target);
        graph.EntryBlocks.Should().Equal(entry);
    }
}
