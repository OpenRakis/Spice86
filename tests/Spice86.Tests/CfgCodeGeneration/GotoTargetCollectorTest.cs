namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class GotoTargetCollectorTest {
    private static readonly MethodCallValueNode Condition = new(DataType.BOOL, null, "Condition");

    [Fact]
    public void TreeWithoutJumpHasNoTarget() {
        // Act
        GotoTargets targets = GotoTargetCollector.Collect(new BlockNode(new MethodCallNode(null, "Foo")));

        // Assert
        targets.Labels.Should().BeEmpty();
        targets.EntryDispatcher.Should().BeFalse();
    }

    [Fact]
    public void CollectsGotosInEveryNestedPosition() {
        // Arrange
        ICfgNode[] nodes = Enumerable.Range(0, 7).Select(_ => Node()).ToArray();
        BlockNode tree = new(
            new InstructionBoundaryNode(Node(), "", new BlockNode(new GotoNode(nodes[0]))),
            new IfElseNode(Condition, new BlockNode(new GotoNode(nodes[1])), new BlockNode(new GotoNode(nodes[2]))),
            new WhileNode(Condition, new BlockNode(new GotoNode(nodes[3]))),
            new SwitchNode(new LoadOffsetParameterNode(), [new SwitchSection(1, new BlockNode(new GotoNode(nodes[4])))],
                new BlockNode(new GotoNode(nodes[5]))),
            new CpuFaultTryCatchNode(new BlockNode(new GotoNode(nodes[6])), new SegmentedAddress(0x1000, 0x0010), []));

        // Act
        GotoTargets targets = GotoTargetCollector.Collect(tree);

        // Assert
        targets.Labels.Should().HaveCount(nodes.Length);
        nodes.Should().OnlyContain(node => targets.Labels.Any(label => ReferenceEquals(label, node)));
        targets.EntryDispatcher.Should().BeFalse();
    }

    [Fact]
    public void CyclicJumpNeedsTheEntryDispatcherLabel() {
        // Arrange
        IfElseNode tree = new(Condition, new BlockNode(new CyclicJumpNode("loop_1000_0020_10020", 0x0020)), new BlockNode());

        // Act
        GotoTargets targets = GotoTargetCollector.Collect(tree);

        // Assert
        targets.Labels.Should().BeEmpty();
        targets.EntryDispatcher.Should().BeTrue();
    }

    private static ICfgNode Node() {
        return Substitute.For<ICfgNode>();
    }
}
