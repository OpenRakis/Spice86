namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using System.Collections.Immutable;

using Xunit;

public class LabelPlacerTest {
    private static readonly CpuFlagNode Zero = new(Flags.Zero);
    private readonly ICfgNode _first = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0000));
    private readonly ICfgNode _second = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0010));
    private readonly ICfgNode _third = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0020));

    [Fact]
    public void LabelsGoBeforeTargetedBoundariesOnly() {
        // Arrange
        InstructionBoundaryNode first = Boundary(_first);
        InstructionBoundaryNode second = Boundary(_second);
        InstructionBoundaryNode third = Boundary(_third);

        // Act
        BlockNode result = LabelPlacer.Place(new BlockNode(first, second, third), ImmutableHashSet.Create<IGotoTarget>(new CfgNodeGotoTarget(_third)));

        // Assert
        result.Statements.Should().HaveCount(4);
        result.Statements[0].Should().BeSameAs(first);
        result.Statements[1].Should().BeSameAs(second);
        result.Statements[2].Should().BeOfType<LabelNode>().Which.Target.Should().Be(new CfgNodeGotoTarget(_third));
        result.Statements[3].Should().BeSameAs(third);
    }

    [Fact]
    public void LabelOfTheFirstNodeOfALoopGoesBeforeTheLoop() {
        // Arrange
        InfiniteLoopNode loop = new(new BlockNode(Boundary(_second), Boundary(_third)));

        // Act
        BlockNode result = LabelPlacer.Place(new BlockNode(Boundary(_first), loop), ImmutableHashSet.Create<IGotoTarget>(new CfgNodeGotoTarget(_second)));

        // Assert
        result.Statements.Should().HaveCount(3);
        result.Statements[1].Should().BeOfType<LabelNode>().Which.Target.Should().Be(new CfgNodeGotoTarget(_second));
        InfiniteLoopNode placedLoop = result.Statements[2].Should().BeOfType<InfiniteLoopNode>().Which;
        placedLoop.Body.Statements.Should().NotContain(statement => statement is LabelNode);
    }

    [Fact]
    public void LabelInsideAnIfArmGoesBeforeItsBoundary() {
        // Arrange
        IfElseNode ifElse = new(Zero, new BlockNode(Boundary(_second), Boundary(_third)), new BlockNode());

        // Act
        BlockNode result = LabelPlacer.Place(new BlockNode(Boundary(_first), ifElse), ImmutableHashSet.Create<IGotoTarget>(new CfgNodeGotoTarget(_third)));

        // Assert
        IfElseNode placed = result.Statements[1].Should().BeOfType<IfElseNode>().Which;
        BlockNode trueArm = placed.TrueCase.Should().BeOfType<BlockNode>().Which;
        trueArm.Statements.Should().HaveCount(3);
        trueArm.Statements[1].Should().BeOfType<LabelNode>().Which.Target.Should().Be(new CfgNodeGotoTarget(_third));
    }

    [Fact]
    public void TargetThatNoStatementStartsAtThrows() {
        // Act
        Action place = () => LabelPlacer.Place(new BlockNode(Boundary(_first)), ImmutableHashSet.Create<IGotoTarget>(new CfgNodeGotoTarget(_third)));

        // Assert
        place.Should().Throw<InvalidOperationException>().WithMessage("*label cannot be placed*");
    }

    private static InstructionBoundaryNode Boundary(ICfgNode node) =>
        new(node, "", new BlockNode(new MethodCallNode(null, "Foo")));
}