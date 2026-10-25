namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

public class MethodStructurizerTest {
    private readonly CfgBlock _entry = Block(1, 0x00);
    private readonly CfgBlock _spin = Block(2, 0x10);
    private readonly CfgBlock _exit = Block(3, 0x20);
    private readonly NaturalLoopForest _loops;
    private static readonly CpuFlagNode Zero = new(Flags.Zero);
    private readonly CfgInstruction _spinTail = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0011));

    public MethodStructurizerTest() {
        _spin.Append(_spinTail);
        PartitionBlockGraph graph = new([_entry, _spin, _exit], [_entry],
            new Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> { [_entry] = [_spin], [_spin] = [_spin, _exit] },
            new Dictionary<CfgBlock, CfgBlock?>());
        DepthFirstOrdering<CfgBlock> traversal = new([.. graph.EntryBlocks, .. graph.Blocks], graph.GetSuccessors);
        _loops = NaturalLoopForest.Build(graph, traversal.RetreatingEdges);
    }

    [Fact]
    public void CheckedLoopHeaderGivesAWhileTrueBetweenTheOtherBlocks() {
        // Arrange
        LoweredBlock[] blocks = [Lowered(_entry, true, "entry"), Lowered(_spin, true, "spin"), Lowered(_exit, false, "exit")];

        // Act
        BlockNode body = MethodStructurizer.Structure(blocks, _loops, Entries(), "method");

        // Assert
        body.Statements.Should().HaveCount(3);
        body.Statements[0].Should().Be(new CommentNode("entry"));
        body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which.Body.Statements.Should().Equal(new CommentNode("spin"));
        body.Statements[2].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void LoopHeaderWithoutAnEntryCheckThrows() {
        // Arrange
        LoweredBlock[] blocks = [Lowered(_entry, true, "entry"), Lowered(_spin, false, "spin"), Lowered(_exit, false, "exit")];

        // Act
        Action structure = () => MethodStructurizer.Structure(blocks, _loops, Entries(), "method");

        // Assert
        structure.Should().Throw<InvalidOperationException>().WithMessage("*has no CheckExternalEvents at its entry*");
    }

    private static LoweredBlock Lowered(CfgBlock block, bool hasEntryCheck, string name) =>
        new(block, hasEntryCheck, new BlockNode(new CommentNode(name)));

    private static CfgBlock Block(int id, ushort offset) =>
        new(id, CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset)));

    [Fact]
    public void SelfLoopWithAConditionalBackEdgeBecomesADoWhile() {
        // Arrange
        LoweredBlock spin = Spin(BackEdgeIf(Zero, toHeaderWhenTrue: true, new GotoNode(_exit.Entry)));

        // Act
        BlockNode body = StructureWith(spin, Entries());

        // Assert
        body.Statements.Should().HaveCount(4);
        DoWhileNode loop = body.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        loop.Condition.Should().BeSameAs(Zero);
        InstructionBoundaryNode boundary = loop.Body.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<InstructionBoundaryNode>().Which;
        boundary.Body.Statements.Should().Equal(new CommentNode("spin"));
        body.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(_exit.Entry);
    }

    [Fact]
    public void BackEdgeInTheFalseArmNegatesTheCondition() {
        // Arrange
        LoweredBlock spin = Spin(BackEdgeIf(Zero, toHeaderWhenTrue: false, new GotoNode(_exit.Entry)));

        // Act
        BlockNode body = StructureWith(spin, Entries());

        // Assert
        DoWhileNode loop = body.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        loop.Condition.Should().Be(CodeGenerationAst.NegatedCondition(Zero));
        body.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(_exit.Entry);
    }

    [Fact]
    public void UntestedExitIsPlacedAfterTheLoop() {
        // Arrange
        FailAsUntestedNode untested = new([UntestedMessagePart.Literal("Untested fallthrough at 1000:0010")]);
        LoweredBlock spin = Spin(BackEdgeIf(Zero, toHeaderWhenTrue: true, untested));

        // Act
        BlockNode body = StructureWith(spin, Entries());

        // Assert
        body.Statements[1].Should().BeOfType<DoWhileNode>();
        body.Statements[2].Should().BeSameAs(untested);
    }

    [Fact]
    public void SelfLoopsThatCannotBeADoWhileBecomeAWhileTrue() {
        // Arrange
        VariableDeclarationNode equal = new(DataType.BOOL, "equal", Zero);
        IfElseNode backEdgeToExit = BackEdgeIf(Zero, toHeaderWhenTrue: true, new GotoNode(_exit.Entry));
        IVisitableAstNode[] terminators = [
            BackEdgeIf(equal.Reference, toHeaderWhenTrue: true, new GotoNode(_exit.Entry)),
            new IfElseNode(Zero, new BlockNode(new GotoNode(_spin.Entry)),
                new BlockNode(new MethodCallNode(null, "Foo"), new GotoNode(_exit.Entry))),
            new IfElseNode(Zero, new BlockNode(new GotoNode(_spin.Entry)), new BlockNode(new GotoNode(_spin.Entry))),
            new IfElseNode(Zero, new BlockNode(new GotoNode(_exit.Entry)), new BlockNode(new GotoNode(_entry.Entry))),
            BackEdgeIf(Zero, toHeaderWhenTrue: true, new FailAsUntestedNode([
                UntestedMessagePart.Literal("Untested near jump target 0x"),
                UntestedMessagePart.Hole(new RegisterNode(DataType.UINT16, 0), "X4")])),
            new CpuFaultTryCatchNode(new BlockNode(backEdgeToExit), new SegmentedAddress(0x1000, 0x0010), [])
        ];

        // Assert
        foreach (IVisitableAstNode terminator in terminators) {
            LoweredBlock spin = Spin(terminator);
            BlockNode body = StructureWith(spin, Entries());
            body.Statements.Should().HaveCount(3, terminator.GetType().Name);
            InfiniteLoopNode loop = body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
            loop.Body.Statements.Should().ContainSingle().Which.Should().BeSameAs(spin.Body.Statements[0]);
        }
    }

    [Fact]
    public void BlockWithAPartitionEntryInsideStaysAsItIs() {
        // Arrange
        LoweredBlock spin = Spin(BackEdgeIf(Zero, toHeaderWhenTrue: true, new GotoNode(_exit.Entry)));

        // Act
        BlockNode body = StructureWith(spin, Entries(_spinTail));

        // Assert
        body.Statements.Should().NotContain(statement => statement is DoWhileNode);
    }

    [Fact]
    public void LoopBlocksMoveTogetherIntoAWhileTrueAtTheHeaderPlace() {
        // Arrange
        CfgBlock head = Block(11, 0x40);
        CfgBlock body = Block(12, 0x50);
        CfgBlock latch = Block(13, 0x60);
        CfgBlock exit = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, head, body, latch, exit],
            new() { [_entry] = [head], [head] = [body, exit], [body] = [latch], [latch] = [head] });
        LoweredBlock[] blocks = [
            Lowered(_entry, true, "entry"), Lowered(head, true, "head"), Lowered(exit, false, "exit"),
            Lowered(body, false, "body"), Lowered(latch, false, "latch")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(3);
        method.Statements[0].Should().Be(new CommentNode("entry"));
        InfiniteLoopNode loop = method.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        loop.Body.Statements.Should().Equal(new CommentNode("head"), new CommentNode("body"), new CommentNode("latch"));
        method.Statements[2].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void OnlyLatchLastWithAConditionalBackEdgeGivesAMultiBlockDoWhile() {
        // Arrange
        CfgBlock head = Block(11, 0x40);
        CfgBlock latch = Block(12, 0x50);
        CfgBlock exit = Block(13, 0x60);
        NaturalLoopForest loops = Forest([_entry, head, latch, exit],
            new() { [_entry] = [head], [head] = [latch], [latch] = [head, exit] });
        LoweredBlock latchBlock = Branching(latch, head, new GotoNode(exit.Entry));
        LoweredBlock[] blocks = [Lowered(_entry, true, "entry"), Lowered(head, true, "head"), latchBlock, Lowered(exit, false, "exit")];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(4);
        DoWhileNode loop = method.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        loop.Condition.Should().BeSameAs(Zero);
        loop.Body.Statements.Should().HaveCount(2);
        loop.Body.Statements[0].Should().Be(new CommentNode("head"));
        loop.Body.Statements[1].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("latch"));
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(exit.Entry);
        method.Statements[3].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void LatchThatIsNotTheLastLoopBlockGivesAWhileTrue() {
        // Arrange
        CfgBlock head = Block(11, 0x40);
        CfgBlock latch = Block(12, 0x50);
        CfgBlock side = Block(13, 0x60);
        CfgBlock exit = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, head, latch, side, exit],
            new() { [_entry] = [head], [head] = [latch, side], [side] = [latch], [latch] = [head, exit] });
        LoweredBlock[] blocks = [
            Lowered(_entry, true, "entry"), Lowered(head, true, "head"), Branching(latch, head, new GotoNode(exit.Entry)),
            Lowered(side, false, "side"), Lowered(exit, false, "exit")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(3);
        InfiniteLoopNode loop = method.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        loop.Body.Statements.Should().HaveCount(3);
        loop.Body.Statements[2].Should().Be(new CommentNode("side"));
    }

    [Fact]
    public void ParentLoopContainsItsStructuredChild() {
        // Arrange
        CfgBlock outer = Block(11, 0x40);
        CfgBlock inner = Block(12, 0x50);
        CfgBlock latch = Block(13, 0x60);
        CfgBlock exit = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, outer, inner, latch, exit],
            new() { [_entry] = [outer], [outer] = [inner], [inner] = [inner, latch], [latch] = [outer, exit] });
        LoweredBlock[] blocks = [
            Lowered(_entry, true, "entry"), Lowered(outer, true, "outer"), Branching(inner, inner, new GotoNode(latch.Entry)),
            Lowered(latch, false, "latch"), Lowered(exit, false, "exit")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(3);
        method.Statements[0].Should().Be(new CommentNode("entry"));
        InfiniteLoopNode parent = method.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        parent.Body.Statements.Should().HaveCount(4);
        parent.Body.Statements[0].Should().Be(new CommentNode("outer"));
        parent.Body.Statements[1].Should().BeOfType<DoWhileNode>();
        parent.Body.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(latch.Entry);
        parent.Body.Statements[3].Should().Be(new CommentNode("latch"));
        method.Statements[2].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void LoopHeaderThatIsNotFirstInTheLayoutThrows() {
        // Arrange
        CfgBlock head = Block(11, 0x40);
        CfgBlock body = Block(12, 0x50);
        CfgBlock exit = Block(13, 0x60);
        NaturalLoopForest loops = Forest([_entry, head, body, exit],
            new() { [_entry] = [head], [head] = [body, exit], [body] = [head] });
        LoweredBlock[] blocks = [
            Lowered(_entry, true, "entry"), Lowered(body, false, "body"), Lowered(head, true, "head"), Lowered(exit, false, "exit")
        ];

        // Act
        Action structure = () => MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        structure.Should().Throw<InvalidOperationException>().WithMessage("*is not the first block of its loop in the layout*");
    }

    [Fact]
    public void ParentWithAConditionalBackEdgeAtTheEndIsADoWhileAroundItsChild() {
        // Arrange
        CfgBlock outer = Block(11, 0x40);
        CfgBlock inner = Block(12, 0x50);
        CfgBlock latch = Block(13, 0x60);
        CfgBlock exit = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, outer, inner, latch, exit],
            new() { [_entry] = [outer], [outer] = [inner], [inner] = [inner, latch], [latch] = [outer, exit] });
        LoweredBlock[] blocks = [
            Lowered(_entry, true, "entry"), Lowered(outer, true, "outer"), Branching(inner, inner, new GotoNode(latch.Entry)),
            Branching(latch, outer, new GotoNode(exit.Entry)), Lowered(exit, false, "exit")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(4);
        DoWhileNode parent = method.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        parent.Condition.Should().BeSameAs(Zero);
        parent.Body.Statements.Should().HaveCount(4);
        parent.Body.Statements[0].Should().Be(new CommentNode("outer"));
        parent.Body.Statements[1].Should().BeOfType<DoWhileNode>();
        parent.Body.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(latch.Entry);
        parent.Body.Statements[3].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("latch"));
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(exit.Entry);
        method.Statements[3].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void ParentWhoseLatchIsInsideAChildIsAWhileTrue() {
        // Arrange
        CfgBlock outer = Block(11, 0x40);
        CfgBlock first = Block(12, 0x50);
        CfgBlock second = Block(13, 0x60);
        CfgBlock exit = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, outer, first, second, exit],
            new() { [_entry] = [outer], [outer] = [first], [first] = [second], [second] = [first, outer, exit] });
        LoweredBlock[] blocks = [
            Lowered(_entry, true, "entry"), Lowered(outer, true, "outer"), Lowered(first, true, "first"),
            Branching(second, outer, new GotoNode(exit.Entry)), Lowered(exit, false, "exit")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(3);
        InfiniteLoopNode parent = method.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        parent.Body.Statements.Should().HaveCount(2);
        parent.Body.Statements[0].Should().Be(new CommentNode("outer"));
        InfiniteLoopNode child = parent.Body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        child.Body.Statements[0].Should().Be(new CommentNode("first"));
        method.Statements[2].Should().Be(new CommentNode("exit"));
    }

    private static NaturalLoopForest Forest(IReadOnlyList<CfgBlock> blocks, Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors) {
        PartitionBlockGraph graph = new(blocks, [blocks[0]], successors, new Dictionary<CfgBlock, CfgBlock?>());
        DepthFirstOrdering<CfgBlock> traversal = new([.. graph.EntryBlocks, .. graph.Blocks], graph.GetSuccessors);
        return NaturalLoopForest.Build(graph, traversal.RetreatingEdges);
    }

    private static LoweredBlock Branching(CfgBlock block, CfgBlock header, IVisitableAstNode exit) {
        IfElseNode branch = new(Zero, new BlockNode(new GotoNode(header.Entry)), new BlockNode(exit));
        string name = ReferenceEquals(block, header) ? "inner" : "latch";
        return new(block, ReferenceEquals(block, header),
            new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode(name), branch))));
    }

    [Fact]
    public void TwoBackEdgesToOneHeaderGiveAWhileTrueAroundBothBlocks() {
        // Arrange
        NaturalLoopForest loops = Forest([_entry, _spin, _exit], new() { [_entry] = [_spin], [_spin] = [_spin, _exit], [_exit] = [_spin] });
        LoweredBlock spin = Spin(BackEdgeIf(Zero, toHeaderWhenTrue: true, new GotoNode(_exit.Entry)));
        LoweredBlock[] blocks = [Lowered(_entry, true, "entry"), spin, Lowered(_exit, false, "exit")];

        // Act
        BlockNode body = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        body.Statements.Should().HaveCount(2);
        InfiniteLoopNode loop = body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        loop.Body.Statements.Should().HaveCount(2);
        loop.Body.Statements[0].Should().BeSameAs(spin.Body.Statements[0]);
        loop.Body.Statements[1].Should().Be(new CommentNode("exit"));
    }

    private IfElseNode BackEdgeIf(ValueNode condition, bool toHeaderWhenTrue, IVisitableAstNode exit) {
        BlockNode backEdge = new(new GotoNode(_spin.Entry));
        BlockNode exitArm = new(exit);
        if (toHeaderWhenTrue) {
            return new IfElseNode(condition, backEdge, exitArm);
        }
        return new IfElseNode(condition, exitArm, backEdge);
    }

    private LoweredBlock Spin(IVisitableAstNode terminator) =>
        new(_spin, true, new BlockNode(new InstructionBoundaryNode(_spin.Entry, "", new BlockNode(new CommentNode("spin"), terminator))));

    private BlockNode StructureWith(LoweredBlock spin, IReadOnlySet<ICfgNode> entryNodes) =>
        MethodStructurizer.Structure([Lowered(_entry, true, "entry"), spin, Lowered(_exit, false, "exit")], _loops, entryNodes, "method");

    [Fact]
    public void ArmThatComesBackToTheOtherTargetIsNestedInAnIf() {
        // Arrange
        CfgBlock then = Block(11, 0x40);
        CfgBlock join = Block(12, 0x50);
        NaturalLoopForest loops = Forest([_entry, then, join], new() { [_entry] = [then, join], [then] = [join] });
        LoweredBlock decide = Deciding(_entry, then, join);
        LoweredBlock thenBlock = Jumping(then, "then", join);
        LoweredBlock[] blocks = [decide, thenBlock, Lowered(join, false, "join")];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(4);
        method.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("decide"));
        IfElseNode nested = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        nested.Condition.Should().BeSameAs(Zero);
        nested.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeSameAs(thenBlock.Body.Statements[0]);
        nested.FalseCase.Should().BeOfType<BlockNode>().Which.Statements.Should().BeEmpty();
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(join.Entry);
        method.Statements[3].Should().Be(new CommentNode("join"));
    }

    [Fact]
    public void ArmInTheFalseBranchNegatesTheCondition() {
        // Arrange
        CfgBlock then = Block(11, 0x40);
        CfgBlock join = Block(12, 0x50);
        NaturalLoopForest loops = Forest([_entry, then, join], new() { [_entry] = [then, join], [then] = [join] });
        LoweredBlock[] blocks = [Deciding(_entry, join, then), Jumping(then, "then", join), Lowered(join, false, "join")];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements[1].Should().BeOfType<IfElseNode>().Which.Condition.Should().Be(CodeGenerationAst.NegatedCondition(Zero));
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(join.Entry);
    }

    [Fact]
    public void ArmHeadsTargetedByTwoGotosStayFlat() {
        // Arrange
        CfgBlock then = Block(11, 0x40);
        CfgBlock other = Block(12, 0x50);
        NaturalLoopForest loops = Forest([_entry, then, other],
            new() { [_entry] = [then, other], [then] = [other], [other] = [then] });
        LoweredBlock decide = Deciding(_entry, then, other);
        LoweredBlock[] blocks = [decide, Jumping(then, "then", other), Jumping(other, "other", then)];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().NotContain(statement => statement is IfElseNode);
        method.Statements[0].Should().BeSameAs(decide.Body.Statements[0]);
    }

    [Fact]
    public void ArmThatDoesNotComeBackStaysFlat() {
        // Arrange
        CfgBlock then = Block(11, 0x40);
        CfgBlock join = Block(12, 0x50);
        NaturalLoopForest loops = Forest([_entry, then, join], new() { [_entry] = [then, join] });
        LoweredBlock thenBlock = new(then, false, new BlockNode(new InstructionBoundaryNode(then.Entry, "",
            new BlockNode(new CommentNode("then"), new FailAsUntestedNode([UntestedMessagePart.Literal("Untested fallthrough at 1000:0040")])))));
        LoweredBlock[] blocks = [Deciding(_entry, then, join), thenBlock, Lowered(join, false, "join")];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().NotContain(statement => statement is IfElseNode);
    }

    [Fact]
    public void TwoArmsThatMeetAtAThirdBlockBecomeAnIfElse() {
        // Arrange
        CfgBlock left = Block(11, 0x40);
        CfgBlock right = Block(12, 0x50);
        CfgBlock join = Block(13, 0x60);
        NaturalLoopForest loops = Forest([_entry, left, right, join],
            new() { [_entry] = [left, right], [left] = [join], [right] = [join] });
        LoweredBlock leftBlock = Jumping(left, "left", join);
        LoweredBlock rightBlock = Jumping(right, "right", join);
        LoweredBlock[] blocks = [Deciding(_entry, left, right), leftBlock, rightBlock, Lowered(join, false, "join")];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(4);
        method.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("decide"));
        IfElseNode nested = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        nested.Condition.Should().BeSameAs(Zero);
        nested.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeSameAs(leftBlock.Body.Statements[0]);
        nested.FalseCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeSameAs(rightBlock.Body.Statements[0]);
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(join.Entry);
        method.Statements[3].Should().Be(new CommentNode("join"));
    }

    [Fact]
    public void ArmsThatNeverMeetStayFlat() {
        // Arrange
        CfgBlock left = Block(11, 0x40);
        CfgBlock right = Block(12, 0x50);
        CfgBlock leftTail = Block(13, 0x60);
        CfgBlock rightTail = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, left, right, leftTail, rightTail],
            new() { [_entry] = [left, right], [left] = [leftTail], [right] = [rightTail] });
        LoweredBlock[] blocks = [
            Deciding(_entry, left, right), Jumping(left, "left", leftTail), Jumping(right, "right", rightTail),
            Lowered(leftTail, false, "leftTail"), Lowered(rightTail, false, "rightTail")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().NotContain(statement => statement is IfElseNode);
    }

    [Fact]
    public void ArmsWithTwoCommonExitsStayFlat() {
        // Arrange
        CfgBlock left = Block(11, 0x40);
        CfgBlock right = Block(12, 0x50);
        CfgBlock first = Block(13, 0x60);
        CfgBlock second = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, left, right, first, second],
            new() { [_entry] = [left, right], [left] = [first, second], [right] = [first, second] });
        LoweredBlock[] blocks = [
            Deciding(_entry, left, right), Deciding(left, first, second), Deciding(right, first, second),
            Lowered(first, false, "first"), Lowered(second, false, "second")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().NotContain(statement => statement is IfElseNode);
    }

    [Fact]
    public void DiamondInsideAnArmIsNestedInsideTheOuterIfElse() {
        // Arrange
        CfgBlock left = Block(11, 0x40);
        CfgBlock innerTrue = Block(12, 0x50);
        CfgBlock innerFalse = Block(13, 0x60);
        CfgBlock innerJoin = Block(14, 0x70);
        CfgBlock right = Block(15, 0x80);
        CfgBlock join = Block(16, 0x90);
        NaturalLoopForest loops = Forest([_entry, left, innerTrue, innerFalse, innerJoin, right, join],
            new() {
                [_entry] = [left, right], [left] = [innerTrue, innerFalse], [innerTrue] = [innerJoin],
                [innerFalse] = [innerJoin], [innerJoin] = [join], [right] = [join]
            });
        LoweredBlock innerJoinBlock = Jumping(innerJoin, "innerJoin", join);
        LoweredBlock[] blocks = [
            Deciding(_entry, left, right), Deciding(left, innerTrue, innerFalse), Jumping(innerTrue, "innerTrue", innerJoin),
            Jumping(innerFalse, "innerFalse", innerJoin), innerJoinBlock, Jumping(right, "right", join), Lowered(join, false, "join")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(4);
        IfElseNode outer = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        IReadOnlyList<IVisitableAstNode> thenArm = outer.TrueCase.Should().BeOfType<BlockNode>().Which.Statements;
        thenArm.Should().HaveCount(4);
        IfElseNode inner = thenArm[1].Should().BeOfType<IfElseNode>().Which;
        inner.FalseCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle();
        thenArm[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(innerJoin.Entry);
        thenArm[3].Should().BeSameAs(innerJoinBlock.Body.Statements[0]);
        outer.FalseCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle();
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(join.Entry);
    }

    [Fact]
    public void ArmHoldingTheLatchOfTheEnclosingLoopStaysFlat() {
        // Arrange
        CfgBlock head = Block(11, 0x40);
        CfgBlock body = Block(12, 0x50);
        CfgBlock exit = Block(13, 0x60);
        NaturalLoopForest loops = Forest([_entry, head, body, exit],
            new() { [_entry] = [head], [head] = [body, exit], [body] = [head, exit] });
        LoweredBlock headBlock = Deciding(head, exit, body) with { HasEntryCheck = true };
        LoweredBlock[] blocks = [
            Lowered(_entry, true, "entry"), headBlock, Branching(body, head, new GotoNode(exit.Entry)), Lowered(exit, false, "exit")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        DoWhileNode loop = method.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        loop.Body.Statements[0].Should().BeSameAs(headBlock.Body.Statements[0]);
        loop.Body.Statements.Should().NotContain(statement => statement is IfElseNode);
    }

    [Fact]
    public void LoopInsideAnArmIsStructuredInsideTheIf() {
        // Arrange
        CfgBlock then = Block(11, 0x40);
        CfgBlock spin = Block(12, 0x50);
        CfgBlock join = Block(13, 0x60);
        NaturalLoopForest loops = Forest([_entry, then, spin, join],
            new() { [_entry] = [then, join], [then] = [spin], [spin] = [spin, join] });
        LoweredBlock thenBlock = Jumping(then, "then", spin);
        LoweredBlock[] blocks = [
            Deciding(_entry, then, join), thenBlock, Branching(spin, spin, new GotoNode(join.Entry)), Lowered(join, false, "join")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(4);
        IfElseNode nested = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        IReadOnlyList<IVisitableAstNode> arm = nested.TrueCase.Should().BeOfType<BlockNode>().Which.Statements;
        arm.Should().HaveCount(3);
        arm[0].Should().BeSameAs(thenBlock.Body.Statements[0]);
        arm[1].Should().BeOfType<DoWhileNode>();
        arm[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(join.Entry);
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(join.Entry);
    }

    private static LoweredBlock Deciding(CfgBlock block, CfgBlock whenTrue, CfgBlock whenFalse) {
        IfElseNode branch = new(Zero, new BlockNode(new GotoNode(whenTrue.Entry)), new BlockNode(new GotoNode(whenFalse.Entry)));
        return new(block, false, new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode("decide"), branch))));
    }

    private static LoweredBlock Jumping(CfgBlock block, string name, CfgBlock target) =>
        new(block, false, new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode(name), new GotoNode(target.Entry)))));

    private HashSet<ICfgNode> Entries(params ICfgNode[] more) => [_entry.Entry, .. more];

    private static FailAsUntestedNode Untested() => new([UntestedMessagePart.Literal("Untested near jump target at 1000:0000")]);

    [Fact]
    public void JumpTableArmsThatMeetAtOneJoinAreNestedInTheirCases() {
        // Arrange
        CfgBlock first = Block(11, 0x40);
        CfgBlock second = Block(12, 0x50);
        CfgBlock third = Block(13, 0x60);
        CfgBlock join = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, first, second, third, join],
            new() { [_entry] = [first, second, third], [first] = [join], [second] = [join], [third] = [join] });
        LoweredBlock dispatch = Dispatching(_entry, first, second, third);
        LoweredBlock firstBlock = Jumping(first, "first", join);
        LoweredBlock[] blocks = [dispatch, firstBlock, Jumping(second, "second", join), Jumping(third, "third", join),
            Lowered(join, false, "join")];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(4);
        method.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("dispatch"));
        SwitchNode nested = method.Statements[1].Should().BeOfType<SwitchNode>().Which;
        nested.Sections.Should().HaveCount(3);
        nested.Sections[0].Body.Statements.Should().ContainSingle().Which.Should().BeSameAs(firstBlock.Body.Statements[0]);
        nested.Sections.Should().OnlyContain(section => section.Body.Statements[0] is InstructionBoundaryNode);
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(join.Entry);
        method.Statements[3].Should().Be(new CommentNode("join"));
    }

    [Fact]
    public void JumpTableArmsThatNeverLeaveAreNestedWithoutAJoin() {
        // Arrange
        CfgBlock first = Block(11, 0x40);
        CfgBlock second = Block(12, 0x50);
        NaturalLoopForest loops = Forest([_entry, first, second], new() { [_entry] = [first, second] });
        LoweredBlock[] blocks = [Dispatching(_entry, first, second), Ending(first, "first"), Ending(second, "second")];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements.Should().HaveCount(2);
        method.Statements[1].Should().BeOfType<SwitchNode>()
            .Which.Sections.Should().OnlyContain(section => section.Body.Statements[0] is InstructionBoundaryNode);
    }

    [Fact]
    public void DispatchWithOneNestableArmStaysFlat() {
        // Arrange
        CfgBlock first = Block(11, 0x40);
        CfgBlock second = Block(12, 0x50);
        NaturalLoopForest loops = Forest([_entry, first, second], new() { [_entry] = [first, second], [first] = [second] });
        LoweredBlock dispatch = Dispatching(_entry, first, second);
        LoweredBlock[] blocks = [dispatch, Jumping(first, "first", second), Ending(second, "second")];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements[0].Should().BeSameAs(dispatch.Body.Statements[0]);
        method.Statements.Should().NotContain(statement => statement is SwitchNode);
    }

    [Fact]
    public void JumpTableArmsThatLeaveToDifferentBlocksStayFlat() {
        // Arrange
        CfgBlock first = Block(11, 0x40);
        CfgBlock second = Block(12, 0x50);
        CfgBlock firstExit = Block(13, 0x60);
        CfgBlock secondExit = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, first, second, firstExit, secondExit],
            new() { [_entry] = [first, second, firstExit, secondExit], [first] = [firstExit], [second] = [secondExit] });
        LoweredBlock dispatch = Dispatching(_entry, first, second, firstExit, secondExit);
        LoweredBlock[] blocks = [
            dispatch, Jumping(first, "first", firstExit), Jumping(second, "second", secondExit),
            Ending(firstExit, "firstExit"), Ending(secondExit, "secondExit")
        ];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements[0].Should().BeSameAs(dispatch.Body.Statements[0]);
        method.Statements.Should().NotContain(statement => statement is SwitchNode);
    }

    [Fact]
    public void CallDispatchStaysFlat() {
        // Arrange
        CfgBlock first = Block(11, 0x40);
        CfgBlock second = Block(12, 0x50);
        NaturalLoopForest loops = Forest([_entry, first, second], new() { [_entry] = [first, second] });
        SwitchNode calls = new(new ExplicitCastNode(DataType.UINT16, new RegisterNode(DataType.UINT16, 3)), [
            new SwitchSection(0x0040, new BlockNode(new MethodCallNode(null, "NearCall"), new GotoNode(first.Entry))),
            new SwitchSection(0x0050, new BlockNode(new MethodCallNode(null, "NearCall"), new GotoNode(second.Entry)))
        ], new BlockNode(Untested()));
        LoweredBlock dispatch = new(_entry, false,
            new BlockNode(new InstructionBoundaryNode(_entry.Entry, "", new BlockNode(new CommentNode("dispatch"), calls))));
        LoweredBlock[] blocks = [dispatch, Ending(first, "first"), Ending(second, "second")];

        // Act
        BlockNode method = MethodStructurizer.Structure(blocks, loops, Entries(), "method");

        // Assert
        method.Statements[0].Should().BeSameAs(dispatch.Body.Statements[0]);
        method.Statements.Should().NotContain(statement => statement is SwitchNode);
    }

    private static LoweredBlock Dispatching(CfgBlock block, params CfgBlock[] targets) {
        SwitchNode dispatch = new(new ExplicitCastNode(DataType.UINT16, new RegisterNode(DataType.UINT16, 3)),
            targets.Select(target => new SwitchSection(target.Entry.Address.Offset, new BlockNode(new GotoNode(target.Entry)))).ToList(),
            new BlockNode(Untested()));
        return new(block, false, new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode("dispatch"), dispatch))));
    }

    private static LoweredBlock Ending(CfgBlock block, string name) =>
        new(block, false, new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode(name), Untested()))));

    [Fact]
    public void ArmHeadTargetedByTwoGotosRecordsSharedTargetAndTheOtherArmNeverComesBack() {
        // Arrange
        CfgBlock then = Block(11, 0x40);
        CfgBlock other = Block(12, 0x50);
        CfgBlock join = Block(13, 0x60);
        CfgBlock late = Block(14, 0x70);
        NaturalLoopForest loops = Forest([_entry, then, other, join, late],
            new() { [_entry] = [then, other], [then] = [join], [other] = [join], [late] = [other] });
        GotoNode trueGoto = new(then.Entry);
        GotoNode falseGoto = new(other.Entry);
        LoweredBlock decide = new(_entry, false, new BlockNode(new InstructionBoundaryNode(_entry.Entry, "",
            new BlockNode(new CommentNode("decide"), new IfElseNode(Zero, new BlockNode(trueGoto), new BlockNode(falseGoto))))));
        LoweredBlock[] blocks = [
            decide, Jumping(then, "then", join), Jumping(other, "other", join),
            Jumping(late, "late", other), Lowered(join, false, "join")
        ];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        MethodStructurizer.Structure(blocks, loops, Entries(), "method", refusals);

        // Assert
        refusals.Should().HaveCount(2);
        refusals[trueGoto].Should().Be(GotoKeepReason.ArmNeverComesBack);
        refusals[falseGoto].Should().Be(GotoKeepReason.SharedTarget);
    }

    [Fact]
    public void ArmThatIsNotASingleGotoRecordsOtherArmNotGotoOnTheGotoArm() {
        // Arrange
        GotoNode trueGoto = new(_exit.Entry);
        IfElseNode branch = new(Zero, new BlockNode(trueGoto), new BlockNode(Untested()));
        LoweredBlock decide = new(_entry, false, new BlockNode(new InstructionBoundaryNode(_entry.Entry, "",
            new BlockNode(new CommentNode("decide"), branch))));
        LoweredBlock[] blocks = [decide, Lowered(_spin, true, "spin"), Lowered(_exit, false, "exit")];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        MethodStructurizer.Structure(blocks, _loops, Entries(), "method", refusals);

        // Assert
        refusals.Should().ContainSingle().Which.Should().Be(new KeyValuePair<GotoNode, GotoKeepReason>(trueGoto, GotoKeepReason.OtherArmNotGoto));
    }

    [Fact]
    public void NestedBranchRecordsNoRefusal() {
        // Arrange
        CfgBlock then = Block(11, 0x40);
        CfgBlock join = Block(12, 0x50);
        NaturalLoopForest loops = Forest([_entry, then, join], new() { [_entry] = [then, join], [then] = [join] });
        LoweredBlock[] blocks = [Deciding(_entry, then, join), Jumping(then, "then", join), Lowered(join, false, "join")];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        MethodStructurizer.Structure(blocks, loops, Entries(), "method", refusals);

        // Assert
        refusals.Should().BeEmpty();
    }
}