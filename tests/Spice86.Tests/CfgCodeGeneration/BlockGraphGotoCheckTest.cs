namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

public class BlockGraphGotoCheckTest {
    private readonly CfgInstruction _first = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0000));
    private readonly CfgInstruction _second = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0001));
    private readonly CfgInstruction _successorEntry = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0010));
    private readonly CfgInstruction _strangerEntry = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0020));
    private readonly PartitionBlockGraph _graph;
    private readonly NodeEmissionPlan[] _plans;

    public BlockGraphGotoCheckTest() {
        CfgBlock block = new(1, _first);
        block.Append(_second);
        CfgBlock successor = new(2, _successorEntry);
        CfgBlock stranger = new(3, _strangerEntry);
        _graph = new PartitionBlockGraph([block, successor, stranger], [block],
            new Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> { [block] = [successor] }, new Dictionary<CfgBlock, CfgBlock?>());
        _plans = [
            new NodeEmissionPlan(_first, block, true, false, false),
            new NodeEmissionPlan(_second, block, false, false, false),
            new NodeEmissionPlan(_successorEntry, successor, true, false, false),
            new NodeEmissionPlan(_strangerEntry, stranger, true, false, false)
        ];
    }

    [Fact]
    public void GotosAlongTheBlockAndToASuccessorAreAccepted() {
        // Arrange
        InstructionBoundaryNode[] boundaries = [
            Boundary(_first, new GotoNode(_second)),
            Boundary(_second, new GotoNode(_successorEntry)),
            Boundary(_successorEntry),
            Boundary(_strangerEntry)
        ];

        // Act
        Action check = () => BlockGraphGotoCheck.Check(_plans, boundaries, _graph, "method");

        // Assert
        check.Should().NotThrow();
    }

    [Fact]
    public void GotoToABlockThatIsNotASuccessorThrows() {
        // Arrange
        InstructionBoundaryNode[] boundaries = [
            Boundary(_first, new GotoNode(_second)),
            Boundary(_second, new GotoNode(_strangerEntry)),
            Boundary(_successorEntry),
            Boundary(_strangerEntry)
        ];

        // Act
        Action check = () => BlockGraphGotoCheck.Check(_plans, boundaries, _graph, "method");

        // Assert
        check.Should().Throw<InvalidOperationException>().WithMessage("*not an edge of the block graph*");
    }

    [Fact]
    public void GotoThatSkipsTheRestOfItsBlockThrows() {
        // Arrange
        InstructionBoundaryNode[] boundaries = [
            Boundary(_first, new GotoNode(_successorEntry)),
            Boundary(_second, new GotoNode(_successorEntry)),
            Boundary(_successorEntry),
            Boundary(_strangerEntry)
        ];

        // Act
        Action check = () => BlockGraphGotoCheck.Check(_plans, boundaries, _graph, "method");

        // Assert
        check.Should().Throw<InvalidOperationException>().WithMessage("*not an edge of the block graph*");
    }

    private static InstructionBoundaryNode Boundary(ICfgNode node, params IVisitableAstNode[] statements) =>
        new(node, "", new BlockNode(statements));
}