namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class FallthroughGotoRemoverTest {
    private static readonly CpuFlagNode Zero = new(Flags.Zero);
    private readonly ICfgNode _first = Substitute.For<ICfgNode>();
    private readonly ICfgNode _second = Substitute.For<ICfgNode>();
    private readonly ICfgNode _third = Substitute.For<ICfgNode>();
    private readonly ICfgNode _fourth = Substitute.For<ICfgNode>();

    [Fact]
    public void GotoAfterAStatementThatNeverCompletesIsDropped() {
        // Arrange
        FailAsUntestedNode failure = new([UntestedMessagePart.Literal("Untested fallthrough at 1000:0010")]);
        BlockNode body = new(
            Boundary(_first, "first", new IfElseNode(Zero, new BlockNode(failure), new BlockNode(failure))),
            new GotoNode(_third),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        result.Statements.Should().HaveCount(2);
        result.Statements.Should().NotContain(statement => statement is GotoNode);
    }

    [Fact]
    public void GotoToTheNextBoundaryIsRemoved() {
        // Arrange
        BlockNode body = new(
            Boundary(_first, "first", new GotoNode(_second)),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines("// first", "// second", "goto L_first;"));
    }

    [Fact]
    public void GotoToANodeThatDoesNotFollowIsKept() {
        // Arrange
        BlockNode body = new(
            Boundary(_first, "first", new GotoNode(_third)),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines("// first", "goto L_third;", "// second", "goto L_first;"));
    }

    [Fact]
    public void TakenArmReachingTheNextBoundaryNegatesTheCondition() {
        // Arrange
        BlockNode body = new(
            Boundary(_first, "first", new IfElseNode(Zero, new BlockNode(new GotoNode(_second)), new BlockNode(new GotoNode(_third)))),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines(
            "// first",
            "if (!ZeroFlag) {",
            "    goto L_third;",
            "}",
            "// second",
            "goto L_first;"));
    }

    [Fact]
    public void NotTakenArmReachingTheNextBoundaryLeavesNoElse() {
        // Arrange
        BlockNode body = new(
            Boundary(_first, "first", new IfElseNode(Zero, new BlockNode(new GotoNode(_third)), new BlockNode(new GotoNode(_second)))),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines(
            "// first",
            "if (ZeroFlag) {",
            "    goto L_third;",
            "}",
            "// second",
            "goto L_first;"));
    }

    [Fact]
    public void ConditionalWhoseArmsBothReachTheNextBoundaryDisappears() {
        // Arrange
        BlockNode body = new(
            Boundary(_first, "first", new IfElseNode(Zero, new BlockNode(new GotoNode(_second)), new BlockNode(new GotoNode(_second)))),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines("// first", "// second", "goto L_first;"));
    }

    [Fact]
    public void DataConditionalWithAnEmptyTrueArmIsNotInverted() {
        // Arrange
        IfElseNode data = new(Zero, new BlockNode(), new BlockNode(new MethodCallNode(null, "Foo")));
        BlockNode body = new(
            Boundary(_first, "first", data, new GotoNode(_second)),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines(
            "// first",
            "if (ZeroFlag) {",
            "}",
            "else {",
            "    Foo();",
            "}",
            "// second",
            "goto L_first;"));
    }

    [Fact]
    public void SwitchSectionEndingWithAGotoToTheNextBoundaryEndsWithBreak() {
        // Arrange
        SwitchNode dispatch = new(new LoadOffsetParameterNode(),
            [
                new SwitchSection(0x0010, new BlockNode(new MethodCallNode(null, "Foo"), new GotoNode(_second))),
                new SwitchSection(0x0020, new BlockNode(new GotoNode(_third)))
            ],
            new BlockNode(new FailAsUntestedNode([UntestedMessagePart.Literal("Untested near jump")])));
        BlockNode body = new(
            Boundary(_first, "first", dispatch),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines(
            "// first",
            "switch (loadOffset) {",
            "    case 0x0010:",
            "        Foo();",
            "        break;",
            "    case 0x0020:",
            "        goto L_third;",
            "    default:",
            "        throw FailAsUntested(\"Untested near jump\");",
            "}",
            "// second",
            "goto L_first;"));
    }

    [Fact]
    public void GotoToTheSwitchFollowInsideASectionBecomesBreak() {
        // Arrange
        FailAsUntestedNode failure = new([UntestedMessagePart.Literal("Untested near jump target at 1000:0000")]);
        SwitchNode dispatch = new(new LoadOffsetParameterNode(),
            [new SwitchSection(1, new BlockNode(Boundary(_second, "second", new MethodCallNode(null, "Foo"), new GotoNode(_third))))],
            new BlockNode(failure));
        BlockNode body = new(Boundary(_first, "first", dispatch), Boundary(_third, "third", failure));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        SwitchNode rewritten = result.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements[^1].Should().BeOfType<SwitchNode>().Which;
        rewritten.Sections[0].Body.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements[^1].Should().BeOfType<BreakNode>();
    }

    [Fact]
    public void GotoToTheSwitchFollowInsideALoopInsideASectionStaysAGoto() {
        // Arrange
        FailAsUntestedNode failure = new([UntestedMessagePart.Literal("Untested near jump target at 1000:0000")]);
        InfiniteLoopNode loop = new(new BlockNode(Boundary(_second, "second", Branch(_third, _second))));
        SwitchNode dispatch = new(new LoadOffsetParameterNode(), [new SwitchSection(1, new BlockNode(loop))], new BlockNode(failure));
        BlockNode body = new(Boundary(_first, "first", dispatch), Boundary(_third, "third", failure));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        SwitchNode rewritten = result.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements[^1].Should().BeOfType<SwitchNode>().Which;
        InfiniteLoopNode rewrittenLoop = rewritten.Sections[0].Body.Statements[0].Should().BeOfType<InfiniteLoopNode>().Which;
        Arm(BranchOf(rewrittenLoop.Body.Statements[0]).TrueCase).Should().ContainSingle()
            .Which.Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(_third);
    }

    [Fact]
    public void GotoFollowedByAnotherStatementOfTheSameBodyIsKept() {
        // Arrange
        BlockNode body = new(
            Boundary(_first, "first",
                new IfElseNode(Zero, new BlockNode(new GotoNode(_second)), new BlockNode()),
                new FailAsUntestedNode([UntestedMessagePart.Literal("Untested far jump")])),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines(
            "// first",
            "if (ZeroFlag) {",
            "    goto L_second;",
            "}",
            "throw FailAsUntested(\"Untested far jump\");",
            "// second",
            "goto L_first;"));
    }

    [Fact]
    public void GotoEndingAFaultTryBodyReachingTheNextBoundaryIsRemoved() {
        // Arrange
        CpuFaultTryCatchNode tryCatch = new(new BlockNode(new MethodCallNode(null, "Foo"), new GotoNode(_second)),
            new SegmentedAddress(0x1000, 0x0010), []);
        BlockNode body = new(
            Boundary(_first, "first", tryCatch),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines(
            "// first",
            "try {",
            "    Foo();",
            "}",
            "catch (CpuException cpuException) {",
            "    return DispatchCpuFault(cpuException, cs1, 0x0010, []);",
            "}",
            "// second",
            "goto L_first;"));
    }

    [Fact]
    public void GotoInsideAWhileBodyIsKept() {
        // Arrange
        BlockNode body = new(
            Boundary(_first, "first", new WhileNode(Zero, new BlockNode(new GotoNode(_second)))),
            Boundary(_second, "second", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        Print(result).Should().Be(Lines(
            "// first",
            "while (ZeroFlag) {",
            "    goto L_second;",
            "}",
            "// second",
            "goto L_first;"));
    }

    [Fact]
    public void GotoToTheStatementAfterAnInfiniteLoopBecomesBreak() {
        // Arrange
        BlockNode body = new(
            new InfiniteLoopNode(new BlockNode(
                Boundary(_first, "first", Branch(_third, _second)),
                Boundary(_second, "second", new GotoNode(_first)))),
            Boundary(_third, "third", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        InfiniteLoopNode loop = result.Statements[0].Should().BeOfType<InfiniteLoopNode>().Which;
        IfElseNode branch = BranchOf(loop.Body.Statements[0]);
        Arm(branch.TrueCase).Should().ContainSingle().Which.Should().BeOfType<BreakNode>();
        Arm(branch.FalseCase).Should().BeEmpty();
        loop.Body.Statements[1].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("second"));
    }

    [Fact]
    public void GotoToTheStartOfAnInfiniteLoopInTheMiddleOfItsBodyBecomesContinue() {
        // Arrange
        BlockNode body = new(
            new InfiniteLoopNode(new BlockNode(
                Boundary(_first, "first", Branch(_first, _second)),
                Boundary(_second, "second", new MethodCallNode(null, "Foo"), Untested()))));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        InfiniteLoopNode loop = result.Statements[0].Should().BeOfType<InfiniteLoopNode>().Which;
        IfElseNode branch = BranchOf(loop.Body.Statements[0]);
        Arm(branch.TrueCase).Should().ContainSingle().Which.Should().BeOfType<ContinueNode>();
        Arm(branch.FalseCase).Should().BeEmpty();
    }

    [Fact]
    public void GotoToTheStartOfADoWhileStaysAGoto() {
        // Arrange
        BlockNode body = new(
            new DoWhileNode(new BlockNode(
                Boundary(_first, "first", Branch(_first, _second)),
                Boundary(_second, "second", new MethodCallNode(null, "Foo"))), Zero));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        DoWhileNode loop = result.Statements[0].Should().BeOfType<DoWhileNode>().Which;
        IfElseNode branch = BranchOf(loop.Body.Statements[0]);
        Arm(branch.TrueCase).Should().ContainSingle().Which.Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(_first);
    }

    [Fact]
    public void GotoToTheStatementAfterADoWhileBecomesBreak() {
        // Arrange
        BlockNode body = new(
            new DoWhileNode(new BlockNode(
                Boundary(_first, "first", Branch(_third, _second)),
                Boundary(_second, "second", new MethodCallNode(null, "Foo"))), Zero),
            Boundary(_third, "third", Untested()));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        DoWhileNode loop = result.Statements[0].Should().BeOfType<DoWhileNode>().Which;
        IfElseNode branch = BranchOf(loop.Body.Statements[0]);
        Arm(branch.TrueCase).Should().ContainSingle().Which.Should().BeOfType<BreakNode>();
    }

    [Fact]
    public void ExitGotoAfterADoWhileGivesItsBreakTarget() {
        // Arrange
        BlockNode body = new(
            new DoWhileNode(new BlockNode(
                Boundary(_first, "first", Branch(_third, _second)),
                Boundary(_second, "second", new MethodCallNode(null, "Foo"))), Zero),
            new GotoNode(_third),
            Boundary(_fourth, "fourth", Untested()));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        DoWhileNode loop = result.Statements[0].Should().BeOfType<DoWhileNode>().Which;
        IfElseNode branch = BranchOf(loop.Body.Statements[0]);
        Arm(branch.TrueCase).Should().ContainSingle().Which.Should().BeOfType<BreakNode>();
        result.Statements[1].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(_third);
    }

    [Fact]
    public void GotoInsideASwitchInsideALoopStaysAGoto() {
        // Arrange
        SwitchNode dispatch = new(new LoadOffsetParameterNode(),
            [new SwitchSection(0x0010, new BlockNode(new GotoNode(_third)))], new BlockNode(Untested()));
        BlockNode body = new(
            new InfiniteLoopNode(new BlockNode(
                Boundary(_first, "first", dispatch),
                Boundary(_second, "second", new GotoNode(_first)))),
            Boundary(_third, "third", Untested()));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        InfiniteLoopNode loop = result.Statements[0].Should().BeOfType<InfiniteLoopNode>().Which;
        SwitchNode rewritten = loop.Body.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements[^1].Should().BeOfType<SwitchNode>().Which;
        rewritten.Sections[0].Body.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(_third);
    }

    [Fact]
    public void GotoInsideAnInstructionLoopInsideALoopStaysAGoto() {
        // Arrange
        BlockNode body = new(
            new InfiniteLoopNode(new BlockNode(
                Boundary(_first, "first", new WhileNode(Zero, new BlockNode(new GotoNode(_third)))),
                Boundary(_second, "second", new GotoNode(_first)))),
            Boundary(_third, "third", Untested()));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        InfiniteLoopNode loop = result.Statements[0].Should().BeOfType<InfiniteLoopNode>().Which;
        WhileNode instructionLoop = loop.Body.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements[^1].Should().BeOfType<WhileNode>().Which;
        instructionLoop.Body.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(_third);
    }

    [Fact]
    public void BreakOfAnInnerLoopOnlyLeavesTheInnerLoop() {
        // Arrange
        InfiniteLoopNode inner = new(new BlockNode(Boundary(_second, "second", Branch(_third, _fourth))));
        BlockNode body = new(
            new InfiniteLoopNode(new BlockNode(
                Boundary(_first, "first", new MethodCallNode(null, "Foo")),
                inner,
                Boundary(_third, "third", new GotoNode(_first)))),
            Boundary(_fourth, "fourth", Untested()));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        InfiniteLoopNode outer = result.Statements[0].Should().BeOfType<InfiniteLoopNode>().Which;
        InfiniteLoopNode rewrittenInner = outer.Body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        IfElseNode branch = BranchOf(rewrittenInner.Body.Statements[0]);
        Arm(branch.TrueCase).Should().ContainSingle().Which.Should().BeOfType<BreakNode>();
        Arm(branch.FalseCase).Should().ContainSingle()
            .Which.Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(_fourth);
        outer.Body.Statements[2].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("third"));
    }

    private static InstructionBoundaryNode Boundary(ICfgNode node, string name, params IVisitableAstNode[] statements) =>
        new(node, "", new BlockNode([new CommentNode(name), .. statements]));

    private string Print(IVisitableAstNode node) {
        CSharpSourceWriter writer = new();
        CSharpStatementPrinter printer = new(writer, new CSharpExpressionPrinter(new Dictionary<ushort, string> { [0x1000] = "cs1" }), LabelOf);
        printer.Print(node);
        return writer.ToString();
    }

    private string LabelOf(ICfgNode node) {
        if (ReferenceEquals(node, _first)) {
            return "L_first";
        }
        if (ReferenceEquals(node, _second)) {
            return "L_second";
        }
        if (ReferenceEquals(node, _third)) {
            return "L_third";
        }
        throw new InvalidOperationException("Unexpected goto target.");
    }

    [Fact]
    public void GotoEndingALoopBodyToItsFirstBoundaryIsRemoved() {
        // Arrange
        InfiniteLoopNode loop = new(new BlockNode(
            Boundary(_first, "first", new GotoNode(_second)),
            Boundary(_second, "second", new GotoNode(_first))));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(new BlockNode(loop));

        // Assert
        result.Statements.Should().ContainSingle().Which.Should().BeOfType<InfiniteLoopNode>();
        GotoTargetCollector.Collect(result).Labels.Should().BeEmpty();
    }

    [Fact]
    public void GotoEndingADoWhileBodyIsKept() {
        // Arrange
        DoWhileNode loop = new(new BlockNode(Boundary(_first, "first", new GotoNode(_first))), Zero);

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(new BlockNode(loop));

        // Assert
        GotoTargetCollector.Collect(result).Labels.Should().ContainSingle().Which.Should().BeSameAs(_first);
    }

    [Fact]
    public void GotoToTheEntryOfAFollowingLoopIsRemovedAndAGotoLeavingTheLoopBecomesBreak() {
        // Arrange
        BlockNode body = new(
            Boundary(_first, "first", new GotoNode(_second)),
            new InfiniteLoopNode(new BlockNode(Boundary(_second, "second", new GotoNode(_third)))),
            Boundary(_third, "third", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        GotoTargets targets = GotoTargetCollector.Collect(result);
        targets.Labels.Should().HaveCount(1);
        targets.Labels.Should().Contain(label => ReferenceEquals(label, _first));
        result.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which.Body.Statements[0]
            .Should().BeOfType<InstructionBoundaryNode>().Which.Body.Statements[^1].Should().BeOfType<BreakNode>();
    }

    [Fact]
    public void GotoToTheEntryOfAFollowingDoWhileIsRemoved() {
        // Arrange
        BlockNode body = new(
            Boundary(_first, "first", new GotoNode(_second)),
            new DoWhileNode(new BlockNode(Boundary(_second, "second", new GotoNode(_third))), Zero),
            Boundary(_third, "third", new GotoNode(_first)));

        // Act
        BlockNode result = FallthroughGotoRemover.Remove(body);

        // Assert
        GotoTargets targets = GotoTargetCollector.Collect(result);
        targets.Labels.Should().HaveCount(1);
        targets.Labels.Should().Contain(label => ReferenceEquals(label, _first));
        result.Statements[1].Should().BeOfType<DoWhileNode>().Which.Body.Statements[0]
            .Should().BeOfType<InstructionBoundaryNode>().Which.Body.Statements[^1].Should().BeOfType<BreakNode>();
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;

    private static IfElseNode Branch(ICfgNode whenTrue, ICfgNode whenFalse) =>
        new(Zero, new BlockNode(new GotoNode(whenTrue)), new BlockNode(new GotoNode(whenFalse)));

    private static IfElseNode BranchOf(IVisitableAstNode boundary) =>
        boundary.Should().BeOfType<InstructionBoundaryNode>().Which.Body.Statements[^1].Should().BeOfType<IfElseNode>().Which;

    private static IReadOnlyList<IVisitableAstNode> Arm(IVisitableAstNode arm) =>
        arm.Should().BeOfType<BlockNode>().Which.Statements;

    private static FailAsUntestedNode Untested() => new([UntestedMessagePart.Literal("Untested fallthrough at 1000:0010")]);
}
