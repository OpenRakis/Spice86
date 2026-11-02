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

using Xunit;

public class LabelScopeCheckTest {
    private static readonly CpuFlagNode Zero = new(Flags.Zero);
    private readonly ICfgNode _target = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0000));

    [Fact]
    public void GotoToALabelOfAnEnclosingBlockIsAccepted() {
        // Arrange
        BlockNode method = new(
            new LabelNode(_target),
            new IfElseNode(Zero, new BlockNode(new GotoNode(_target)), new BlockNode()),
            new InfiniteLoopNode(new BlockNode(new IfElseNode(Zero, new BlockNode(new GotoNode(_target)), new BlockNode()))));

        // Act
        Action verify = () => LabelScopeCheck.Verify(method);

        // Assert
        verify.Should().NotThrow();
    }

    [Fact]
    public void LabelInsideASequenceOrABoundaryBelongsToTheEnclosingBlock() {
        // Arrange
        BlockNode method = new(
            new InstructionBoundaryNode(CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0030)), "", new BlockNode(new GotoNode(_target))),
            new BlockNode(new LabelNode(_target)));

        // Act
        Action verify = () => LabelScopeCheck.Verify(method);

        // Assert
        verify.Should().NotThrow();
    }

    [Fact]
    public void GotoIntoANestedBlockThrows() {
        // Arrange
        BlockNode labeled = new(new LabelNode(_target));
        IVisitableAstNode[] constructs = [
            new IfElseNode(Zero, labeled, new BlockNode()),
            new IfElseNode(Zero, new BlockNode(), labeled),
            new WhileNode(Zero, labeled),
            new InfiniteLoopNode(labeled),
            new DoWhileNode(labeled, Zero),
            new CpuFaultTryCatchNode(labeled, new SegmentedAddress(0x1000, 0x0010), []),
            new SwitchNode(new LoadOffsetParameterNode(), [new SwitchSection(1, labeled)], new BlockNode())
        ];

        // Assert
        foreach (IVisitableAstNode construct in constructs) {
            Action verify = () => LabelScopeCheck.Verify(new BlockNode(new GotoNode(_target), construct));
            verify.Should().Throw<InvalidOperationException>(construct.GetType().Name)
                .WithMessage("*not declared in an enclosing block*");
        }
    }

    [Fact]
    public void GotoWithoutAnyLabelThrows() {
        // Act
        Action verify = () => LabelScopeCheck.Verify(new BlockNode(new GotoNode(_target)));

        // Assert
        verify.Should().Throw<InvalidOperationException>();
    }
}