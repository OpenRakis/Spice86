namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class CompletesNormallyVisitorTest {
    private static readonly SegmentedAddress At = new(0x1000, 0x0010);
    private static readonly MethodCallNode Foo = new(null, "Foo");
    private static readonly MethodCallValueNode Condition = new(DataType.BOOL, null, "Condition");
    private readonly GotoNode _jump = new(Substitute.For<ICfgNode>());

    [Fact]
    public void EmptyBlockAndExpressionStatementCompleteNormally() {
        // Assert
        CompletesNormallyVisitor.CompletesNormally(new BlockNode()).Should().BeTrue();
        CompletesNormallyVisitor.CompletesNormally(Foo).Should().BeTrue();
    }

    [Fact]
    public void DivergingStatementsDoNotCompleteNormally() {
        // Arrange
        IVisitableAstNode[] diverging = [
            new ReturnNode(new MethodCallNode(null, "NearRet")),
            _jump,
            new ThrowNode(typeof(InvalidOperationException), "x"),
            new FailAsUntestedNode([UntestedMessagePart.Literal("x")]),
            new CyclicJumpNode("loop_1000_0020_10020", 0x0020),
            new BreakNode()
        ];

        // Assert
        foreach (IVisitableAstNode node in diverging) {
            CompletesNormallyVisitor.CompletesNormally(node).Should().BeFalse(node.GetType().Name);
        }
    }

    [Fact]
    public void SequenceFollowsItsLastStatement() {
        // Assert
        CompletesNormallyVisitor.CompletesNormally(new BlockNode(_jump, Foo)).Should().BeTrue();
        CompletesNormallyVisitor.CompletesNormally(new BlockNode(Foo, _jump)).Should().BeFalse();
    }

    [Fact]
    public void IfCompletesWhenOneArmCompletes() {
        // Assert
        CompletesNormallyVisitor.CompletesNormally(new IfElseNode(Condition, new BlockNode(_jump), new BlockNode())).Should().BeTrue();
        CompletesNormallyVisitor.CompletesNormally(new IfElseNode(Condition, new BlockNode(_jump), new BlockNode(_jump))).Should().BeFalse();
    }

    [Fact]
    public void WhileCompletesNormallyEvenWhenItsBodyDiverges() {
        // Assert
        CompletesNormallyVisitor.CompletesNormally(new WhileNode(Condition, new BlockNode(_jump))).Should().BeTrue();
    }

    [Fact]
    public void SwitchCompletesWhenASectionBreaksOrTheDefaultCompletes() {
        // Arrange
        BlockNode failure = new(new FailAsUntestedNode([UntestedMessagePart.Literal("x")]));
        SwitchNode breaks = new(new LoadOffsetParameterNode(), [new SwitchSection(1, new BlockNode(Foo, new BreakNode()))], failure);
        SwitchNode diverges = new(new LoadOffsetParameterNode(), [new SwitchSection(1, new BlockNode(_jump))], failure);
        SwitchNode defaultCompletes = new(new LoadOffsetParameterNode(), [new SwitchSection(1, new BlockNode(_jump))], new BlockNode(Foo));

        // Assert
        CompletesNormallyVisitor.CompletesNormally(breaks).Should().BeTrue();
        CompletesNormallyVisitor.CompletesNormally(diverges).Should().BeFalse();
        CompletesNormallyVisitor.CompletesNormally(defaultCompletes).Should().BeTrue();
    }

    [Fact]
    public void TryCatchAndBoundaryFollowTheirBody() {
        // Assert
        CompletesNormallyVisitor.CompletesNormally(new CpuFaultTryCatchNode(new BlockNode(Foo), At, [])).Should().BeTrue();
        CompletesNormallyVisitor.CompletesNormally(new CpuFaultTryCatchNode(new BlockNode(_jump), At, [])).Should().BeFalse();
        CompletesNormallyVisitor.CompletesNormally(new InstructionBoundaryNode(Substitute.For<ICfgNode>(), "", new BlockNode(_jump))).Should().BeFalse();
    }

    [Fact]
    public void UnloweredControlFlowIsRejected() {
        // Arrange
        MoveIpNextNode moveIpNext = new(new ConstantNode(DataType.UINT16, 2));

        // Act
        Action analyze = () => CompletesNormallyVisitor.CompletesNormally(moveIpNext);

        // Assert
        analyze.Should().Throw<InvalidOperationException>();
    }
}
