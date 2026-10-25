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
    public void SwitchCompletesWhenASectionBreaksAtAnyDepthOutsideLoops() {
        // Arrange
        BlockNode failure = new(new FailAsUntestedNode([UntestedMessagePart.Literal("x")]));
        BlockNode deepBreak = new(new IfElseNode(Condition, new BlockNode(new BreakNode()), new BlockNode(_jump)));
        BlockNode loopBreak = new(new InfiniteLoopNode(new BlockNode(new BreakNode())), _jump);
        SwitchNode breaksDeep = new(new LoadOffsetParameterNode(), [new SwitchSection(1, deepBreak)], failure);
        SwitchNode breaksInLoop = new(new LoadOffsetParameterNode(), [new SwitchSection(1, loopBreak)], failure);

        // Assert
        CompletesNormallyVisitor.CompletesNormally(breaksDeep).Should().BeTrue();
        CompletesNormallyVisitor.CompletesNormally(breaksInLoop).Should().BeFalse();
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

    [Fact]
    public void ContinueDoesNotCompleteNormally() {
        // Assert
        CompletesNormallyVisitor.CompletesNormally(new ContinueNode()).Should().BeFalse();
    }

    [Fact]
    public void LoopWithoutBreakNeverCompletes() {
        // Assert
        CompletesNormallyVisitor.CompletesNormally(new InfiniteLoopNode(new BlockNode(Foo))).Should().BeFalse();
        CompletesNormallyVisitor.CompletesNormally(new InfiniteLoopNode(new BlockNode(Foo, _jump))).Should().BeFalse();
        CompletesNormallyVisitor.CompletesNormally(new BlockNode(new InfiniteLoopNode(new BlockNode(Foo)))).Should().BeFalse();
    }

    [Fact]
    public void LoopCompletesWhenABreakLeavesIt() {
        // Arrange
        BreakNode leave = new();
        BlockNode[] bodies = [
            new BlockNode(new IfElseNode(Condition, new BlockNode(leave), new BlockNode()), _jump),
            new BlockNode(new InstructionBoundaryNode(Substitute.For<ICfgNode>(), "", new BlockNode(leave)), _jump),
            new BlockNode(new CpuFaultTryCatchNode(new BlockNode(leave), At, []), _jump)
        ];

        // Assert
        foreach (BlockNode body in bodies) {
            CompletesNormallyVisitor.CompletesNormally(new InfiniteLoopNode(body)).Should().BeTrue();
        }
    }

    [Fact]
    public void BreakCapturedByAnInnerConstructDoesNotLeaveTheLoop() {
        // Arrange
        BlockNode breaking = new(new BreakNode());
        IVisitableAstNode[] inner = [
            new SwitchNode(new LoadOffsetParameterNode(), [new SwitchSection(1, breaking)], new BlockNode(_jump)),
            new WhileNode(Condition, breaking),
            new InfiniteLoopNode(breaking),
            new DoWhileNode(breaking, Condition)
        ];

        // Assert
        foreach (IVisitableAstNode construct in inner) {
            CompletesNormallyVisitor.CompletesNormally(new InfiniteLoopNode(new BlockNode(construct, _jump)))
                .Should().BeFalse(construct.GetType().Name);
        }
    }

    [Fact]
    public void DoWhileCompletesWhenItsBodyCompletes() {
        // Assert
        CompletesNormallyVisitor.CompletesNormally(new DoWhileNode(new BlockNode(Foo), Condition)).Should().BeTrue();
        CompletesNormallyVisitor.CompletesNormally(new DoWhileNode(new BlockNode(Foo, _jump), Condition)).Should().BeFalse();
    }

    [Fact]
    public void DoWhileCompletesThroughABreakOrAContinue() {
        // Arrange
        BlockNode leaving = new(new IfElseNode(Condition, new BlockNode(new BreakNode()), new BlockNode()), _jump);
        BlockNode restarting = new(new IfElseNode(Condition, new BlockNode(new ContinueNode()), new BlockNode()), _jump);
        BlockNode restartingFromASwitch = new(
            new SwitchNode(new LoadOffsetParameterNode(), [new SwitchSection(1, new BlockNode(new ContinueNode()))], new BlockNode(_jump)),
            _jump);

        // Assert
        CompletesNormallyVisitor.CompletesNormally(new DoWhileNode(leaving, Condition)).Should().BeTrue();
        CompletesNormallyVisitor.CompletesNormally(new DoWhileNode(restarting, Condition)).Should().BeTrue();
        CompletesNormallyVisitor.CompletesNormally(new DoWhileNode(restartingFromASwitch, Condition)).Should().BeTrue();
    }

    [Fact]
    public void ContinueOfAnInnerLoopDoesNotReachTheOuterCondition() {
        // Arrange
        BlockNode body = new(new WhileNode(Condition, new BlockNode(new ContinueNode())), _jump);

        // Assert
        CompletesNormallyVisitor.CompletesNormally(new DoWhileNode(body, Condition)).Should().BeFalse();
    }
}