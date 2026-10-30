namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
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
    private readonly CfgBlock _entry = CfgTestHelpers.CreateBlock(1, 0x00);
    private readonly CfgBlock _spin = CfgTestHelpers.CreateBlock(2, 0x10);
    private readonly CfgBlock _exit = CfgTestHelpers.CreateBlock(3, 0x20);
    private readonly NaturalLoopForest<CfgBlock> _loops;
    private static readonly CpuFlagNode Zero = new(Flags.Zero);
    private readonly CfgInstruction _spinTail = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0011));

    public MethodStructurizerTest() {
        _spin.Append(_spinTail);
        _loops = CfgTestHelpers.BuildLoopForest([_entry, _spin, _exit],
            new() { [_entry] = [_spin], [_spin] = [_spin, _exit] });
    }

    [Fact]
    public void CheckedLoopHeaderGivesAWhileTrueBetweenTheOtherBlocks() {
        // Arrange
        LoweredBlock<CfgBlock>[] blocks = [Lowered(_entry, true, true, "entry"), Lowered(_spin, false, true, "spin"), Lowered(_exit, false, false, "exit")];

        // Act
        BlockNode body = Structure(blocks, _loops, Entries());

        // Assert
        body.Statements.Should().HaveCount(3);
        body.Statements[0].Should().Be(new CommentNode("entry"));
        body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which.Body.Statements.Should().Equal(new CommentNode("spin"));
        body.Statements[2].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void LoopHeaderWithoutAnEntryCheckThrows() {
        // Arrange
        LoweredBlock<CfgBlock>[] blocks = [Lowered(_entry, true, true, "entry"), Lowered(_spin, false, false, "spin"), Lowered(_exit, false, false, "exit")];

        // Act
        Action structure = () => Structure(blocks, _loops, Entries());

        // Assert
        structure.Should().Throw<InvalidOperationException>().WithMessage("*has no CheckExternalEvents at its entry*");
    }

    [Fact]
    public void SelfLoopWithAConditionalBackEdgeBecomesADoWhile() {
        // Arrange
        LoweredBlock<CfgBlock> spin = Spin(BackEdgeIf(Zero, toHeaderWhenTrue: true, new GotoNode(Target(_exit))));

        // Act
        BlockNode body = StructureWith(spin, Entries());

        // Assert
        body.Statements.Should().HaveCount(4);
        DoWhileNode loop = body.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        loop.Condition.Should().BeSameAs(Zero);
        InstructionBoundaryNode boundary = loop.Body.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<InstructionBoundaryNode>().Which;
        boundary.Body.Statements.Should().Equal(new CommentNode("spin"));
        body.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(_exit));
    }

    [Fact]
    public void BackEdgeInTheFalseArmNegatesTheCondition() {
        // Arrange
        LoweredBlock<CfgBlock> spin = Spin(BackEdgeIf(Zero, toHeaderWhenTrue: false, new GotoNode(Target(_exit))));

        // Act
        BlockNode body = StructureWith(spin, Entries());

        // Assert
        DoWhileNode loop = body.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        loop.Condition.Should().Be(CodeGenerationAst.NegatedCondition(Zero));
        body.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(_exit));
    }

    [Fact]
    public void UntestedExitIsPlacedAfterTheLoop() {
        // Arrange
        FailAsUntestedNode untested = new([UntestedMessagePart.Literal("Untested fallthrough at 1000:0010")]);
        LoweredBlock<CfgBlock> spin = Spin(BackEdgeIf(Zero, toHeaderWhenTrue: true, untested));

        // Act
        BlockNode body = StructureWith(spin, Entries());

        // Assert
        body.Statements[1].Should().BeOfType<DoWhileNode>();
        body.Statements[2].Should().BeSameAs(untested);
    }

    [Theory]
    [InlineData("conditionReadsLocal")]
    [InlineData("exitArmHasCode")]
    [InlineData("bothArmsRestart")]
    [InlineData("noBackEdge")]
    [InlineData("untestedMessageReadsRegister")]
    [InlineData("tryCatch")]
    public void SelfLoopThatCannotBeADoWhileBecomesAWhileTrue(string terminator) {
        // Arrange
        LoweredBlock<CfgBlock> spin = Spin(Terminator(terminator));

        // Act
        BlockNode body = StructureWith(spin, Entries());

        // Assert
        body.Statements.Should().HaveCount(3);
        InfiniteLoopNode loop = body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        loop.Body.Statements.Should().ContainSingle().Which.Should().BeSameAs(spin.Body.Statements[0]);
    }

    [Fact]
    public void BlockWithAPartitionEntryInsideStaysAsItIs() {
        // Arrange
        LoweredBlock<CfgBlock> spin = new(_spin, Target(_spin), true, true, true, new BlockNode(
            new InstructionBoundaryNode(_spin.Entry, "", new BlockNode(new CommentNode("spin"))),
            new InstructionBoundaryNode(_spinTail, "", new BlockNode(BackEdgeIf(Zero, toHeaderWhenTrue: true, new GotoNode(Target(_exit)))))));

        // Act
        BlockNode body = StructureWith(spin, Entries(_spinTail));

        // Assert
        body.Statements.Should().NotContain(statement => statement is DoWhileNode);
    }

    [Fact]
    public void LoopBlocksMoveTogetherIntoAWhileTrueAtTheHeaderPlace() {
        // Arrange
        CfgBlock head = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock body = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock latch = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock exit = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, head, body, latch, exit],
            new() { [_entry] = [head], [head] = [body, exit], [body] = [latch], [latch] = [head] });
        LoweredBlock<CfgBlock>[] blocks = [
            Lowered(_entry, true, true, "entry"), Lowered(head, false, true, "head"), Lowered(exit, false, false, "exit"),
            Lowered(body, false, false, "body"), Lowered(latch, false, false, "latch")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

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
        CfgBlock head = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock latch = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock exit = CfgTestHelpers.CreateBlock(13, 0x60);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, head, latch, exit],
            new() { [_entry] = [head], [head] = [latch], [latch] = [head, exit] });
        LoweredBlock<CfgBlock> latchBlock = Branching(latch, head, new GotoNode(Target(exit)));
        LoweredBlock<CfgBlock>[] blocks = [Lowered(_entry, true, true, "entry"), Lowered(head, false, true, "head"), latchBlock, Lowered(exit, false, false, "exit")];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(4);
        DoWhileNode loop = method.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        loop.Condition.Should().BeSameAs(Zero);
        loop.Body.Statements.Should().HaveCount(2);
        loop.Body.Statements[0].Should().Be(new CommentNode("head"));
        loop.Body.Statements[1].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("latch"));
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(exit));
        method.Statements[3].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void LatchThatIsNotTheLastLoopBlockGivesAWhileTrue() {
        // Arrange
        CfgBlock head = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock latch = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock side = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock exit = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, head, latch, side, exit],
            new() { [_entry] = [head], [head] = [latch, side], [side] = [latch], [latch] = [head, exit] });
        LoweredBlock<CfgBlock>[] blocks = [
            Lowered(_entry, true, true, "entry"), Lowered(head, false, true, "head"), Branching(latch, head, new GotoNode(Target(exit))),
            Lowered(side, false, false, "side"), Lowered(exit, false, false, "exit")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(3);
        InfiniteLoopNode loop = method.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        loop.Body.Statements.Should().HaveCount(3);
        loop.Body.Statements[2].Should().Be(new CommentNode("side"));
    }

    [Fact]
    public void ParentLoopContainsItsStructuredChild() {
        // Arrange
        CfgBlock outer = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock inner = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock latch = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock exit = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, outer, inner, latch, exit],
            new() { [_entry] = [outer], [outer] = [inner], [inner] = [inner, latch], [latch] = [outer, exit] });
        LoweredBlock<CfgBlock>[] blocks = [
            Lowered(_entry, true, true, "entry"), Lowered(outer, false, true, "outer"), Branching(inner, inner, new GotoNode(Target(latch))),
            Lowered(latch, false, false, "latch"), Lowered(exit, false, false, "exit")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(3);
        method.Statements[0].Should().Be(new CommentNode("entry"));
        InfiniteLoopNode parent = method.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        parent.Body.Statements.Should().HaveCount(4);
        parent.Body.Statements[0].Should().Be(new CommentNode("outer"));
        parent.Body.Statements[1].Should().BeOfType<DoWhileNode>();
        parent.Body.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(latch));
        parent.Body.Statements[3].Should().Be(new CommentNode("latch"));
        method.Statements[2].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void LoopHeaderThatIsNotFirstInTheLayoutThrows() {
        // Arrange
        CfgBlock head = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock body = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock exit = CfgTestHelpers.CreateBlock(13, 0x60);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, head, body, exit],
            new() { [_entry] = [head], [head] = [body, exit], [body] = [head] });
        LoweredBlock<CfgBlock>[] blocks = [
            Lowered(_entry, true, true, "entry"), Lowered(body, false, false, "body"), Lowered(head, false, true, "head"), Lowered(exit, false, false, "exit")
        ];

        // Act
        Action structure = () => Structure(blocks, loops, Entries());

        // Assert
        structure.Should().Throw<InvalidOperationException>().WithMessage("*is not the first block of its loop in the layout*");
    }

    [Fact]
    public void ParentWithAConditionalBackEdgeAtTheEndIsADoWhileAroundItsChild() {
        // Arrange
        CfgBlock outer = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock inner = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock latch = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock exit = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, outer, inner, latch, exit],
            new() { [_entry] = [outer], [outer] = [inner], [inner] = [inner, latch], [latch] = [outer, exit] });
        LoweredBlock<CfgBlock>[] blocks = [
            Lowered(_entry, true, true, "entry"), Lowered(outer, false, true, "outer"), Branching(inner, inner, new GotoNode(Target(latch))),
            Branching(latch, outer, new GotoNode(Target(exit))), Lowered(exit, false, false, "exit")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(4);
        DoWhileNode parent = method.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        parent.Condition.Should().BeSameAs(Zero);
        parent.Body.Statements.Should().HaveCount(4);
        parent.Body.Statements[0].Should().Be(new CommentNode("outer"));
        parent.Body.Statements[1].Should().BeOfType<DoWhileNode>();
        parent.Body.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(latch));
        parent.Body.Statements[3].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("latch"));
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(exit));
        method.Statements[3].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void ParentWhoseLatchIsInsideAChildIsAWhileTrue() {
        // Arrange
        CfgBlock outer = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock first = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock second = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock exit = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, outer, first, second, exit],
            new() { [_entry] = [outer], [outer] = [first], [first] = [second], [second] = [first, outer, exit] });
        LoweredBlock<CfgBlock>[] blocks = [
            Lowered(_entry, true, true, "entry"), Lowered(outer, false, true, "outer"), Lowered(first, false, true, "first"),
            Branching(second, outer, new GotoNode(Target(exit))), Lowered(exit, false, false, "exit")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(3);
        InfiniteLoopNode parent = method.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        parent.Body.Statements.Should().HaveCount(2);
        parent.Body.Statements[0].Should().Be(new CommentNode("outer"));
        InfiniteLoopNode child = parent.Body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        child.Body.Statements[0].Should().Be(new CommentNode("first"));
        method.Statements[2].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void TwoBackEdgesToOneHeaderGiveAWhileTrueAroundBothBlocks() {
        // Arrange
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, _spin, _exit], new() { [_entry] = [_spin], [_spin] = [_spin, _exit], [_exit] = [_spin] });
        LoweredBlock<CfgBlock> spin = Spin(BackEdgeIf(Zero, toHeaderWhenTrue: true, new GotoNode(Target(_exit))));
        LoweredBlock<CfgBlock>[] blocks = [Lowered(_entry, true, true, "entry"), spin, Lowered(_exit, false, false, "exit")];

        // Act
        BlockNode body = Structure(blocks, loops, Entries());

        // Assert
        body.Statements.Should().HaveCount(2);
        InfiniteLoopNode loop = body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        loop.Body.Statements.Should().HaveCount(2);
        loop.Body.Statements[0].Should().BeSameAs(spin.Body.Statements[0]);
        loop.Body.Statements[1].Should().Be(new CommentNode("exit"));
    }

    [Fact]
    public void ArmThatComesBackToTheOtherTargetIsNestedInAnIf() {
        // Arrange
        CfgBlock then = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock join = CfgTestHelpers.CreateBlock(12, 0x50);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, then, join], new() { [_entry] = [then, join], [then] = [join] });
        LoweredBlock<CfgBlock> decide = Deciding(_entry, then, join);
        LoweredBlock<CfgBlock> thenBlock = Jumping(then, "then", join);
        LoweredBlock<CfgBlock>[] blocks = [decide, thenBlock, Lowered(join, false, false, "join")];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(4);
        method.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("decide"));
        IfElseNode nested = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        nested.Condition.Should().BeSameAs(Zero);
        nested.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeSameAs(thenBlock.Body.Statements[0]);
        nested.FalseCase.Should().BeOfType<BlockNode>().Which.Statements.Should().BeEmpty();
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(join));
        method.Statements[3].Should().Be(new CommentNode("join"));
    }

    [Fact]
    public void ArmInTheFalseBranchNegatesTheCondition() {
        // Arrange
        CfgBlock then = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock join = CfgTestHelpers.CreateBlock(12, 0x50);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, then, join], new() { [_entry] = [then, join], [then] = [join] });
        LoweredBlock<CfgBlock>[] blocks = [Deciding(_entry, join, then), Jumping(then, "then", join), Lowered(join, false, false, "join")];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements[1].Should().BeOfType<IfElseNode>().Which.Condition.Should().Be(CodeGenerationAst.NegatedCondition(Zero));
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(join));
    }

    [Fact]
    public void ArmHeadsTargetedByTwoGotosStayFlat() {
        // Arrange
        CfgBlock then = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock other = CfgTestHelpers.CreateBlock(12, 0x50);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, then, other],
            new() { [_entry] = [then, other], [then] = [other], [other] = [then] });
        LoweredBlock<CfgBlock> decide = Deciding(_entry, then, other);
        LoweredBlock<CfgBlock>[] blocks = [decide, Jumping(then, "then", other), Jumping(other, "other", then)];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().NotContain(statement => statement is IfElseNode);
        method.Statements[0].Should().BeSameAs(decide.Body.Statements[0]);
    }

    [Fact]
    public void LaterDivergingArmIsNestedAndTheEarlierStaysFlat() {
        // Arrange
        CfgBlock then = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock join = CfgTestHelpers.CreateBlock(12, 0x50);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, then, join], new() { [_entry] = [then, join] });
        LoweredBlock<CfgBlock> thenBlock = new(then, Target(then), false, false, true, new BlockNode(new InstructionBoundaryNode(then.Entry, "",
            new BlockNode(new CommentNode("then"), new FailAsUntestedNode([UntestedMessagePart.Literal("Untested fallthrough at 1000:0040")])))));
        LoweredBlock<CfgBlock> joinBlock = Lowered(join, false, false, "join");
        LoweredBlock<CfgBlock>[] blocks = [Deciding(_entry, then, join), thenBlock, joinBlock];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(4);
        IfElseNode nested = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        nested.Condition.Should().BeOfType<UnaryOperationNode>();
        nested.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeSameAs(joinBlock.Body.Statements[0]);
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(then));
    }

    [Fact]
    public void TwoArmsThatMeetAtAThirdBlockBecomeAnIfElse() {
        // Arrange
        CfgBlock left = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock right = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock join = CfgTestHelpers.CreateBlock(13, 0x60);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, left, right, join],
            new() { [_entry] = [left, right], [left] = [join], [right] = [join] });
        LoweredBlock<CfgBlock> leftBlock = Jumping(left, "left", join);
        LoweredBlock<CfgBlock> rightBlock = Jumping(right, "right", join);
        LoweredBlock<CfgBlock>[] blocks = [Deciding(_entry, left, right), leftBlock, rightBlock, Lowered(join, false, false, "join")];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

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
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(join));
        method.Statements[3].Should().Be(new CommentNode("join"));
    }

    [Fact]
    public void DivergingArmsNestOnlyTheArmWithTheLaterHead() {
        // Arrange
        CfgBlock left = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock right = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock leftTail = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock rightTail = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, left, right, leftTail, rightTail],
            new() { [_entry] = [left, right], [left] = [leftTail], [right] = [rightTail] });
        LoweredBlock<CfgBlock>[] blocks = [
            Deciding(_entry, left, right), Jumping(left, "left", leftTail), Jumping(right, "right", rightTail),
            Lowered(leftTail, false, false, "leftTail"), Lowered(rightTail, false, false, "rightTail")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(5);
        IfElseNode nested = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        nested.Condition.Should().BeOfType<UnaryOperationNode>();
        nested.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().HaveCount(2);
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(left));
    }

    [Fact]
    public void ArmsWithTwoCommonExitsStayFlat() {
        // Arrange
        CfgBlock left = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock right = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock first = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock second = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, left, right, first, second],
            new() { [_entry] = [left, right], [left] = [first, second], [right] = [first, second] });
        LoweredBlock<CfgBlock>[] blocks = [
            Deciding(_entry, left, right), Deciding(left, first, second), Deciding(right, first, second),
            Lowered(first, false, false, "first"), Lowered(second, false, false, "second")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().NotContain(statement => statement is IfElseNode);
    }

    [Fact]
    public void DiamondInsideAnArmIsNestedInsideTheOuterIfElse() {
        // Arrange
        CfgBlock left = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock innerTrue = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock innerFalse = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock innerJoin = CfgTestHelpers.CreateBlock(14, 0x70);
        CfgBlock right = CfgTestHelpers.CreateBlock(15, 0x80);
        CfgBlock join = CfgTestHelpers.CreateBlock(16, 0x90);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, left, innerTrue, innerFalse, innerJoin, right, join],
            new() {
                [_entry] = [left, right], [left] = [innerTrue, innerFalse], [innerTrue] = [innerJoin],
                [innerFalse] = [innerJoin], [innerJoin] = [join], [right] = [join]
            });
        LoweredBlock<CfgBlock> innerJoinBlock = Jumping(innerJoin, "innerJoin", join);
        LoweredBlock<CfgBlock>[] blocks = [
            Deciding(_entry, left, right), Deciding(left, innerTrue, innerFalse), Jumping(innerTrue, "innerTrue", innerJoin),
            Jumping(innerFalse, "innerFalse", innerJoin), innerJoinBlock, Jumping(right, "right", join), Lowered(join, false, false, "join")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(4);
        IfElseNode outer = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        IReadOnlyList<IVisitableAstNode> thenArm = outer.TrueCase.Should().BeOfType<BlockNode>().Which.Statements;
        thenArm.Should().HaveCount(4);
        IfElseNode inner = thenArm[1].Should().BeOfType<IfElseNode>().Which;
        inner.FalseCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle();
        thenArm[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(innerJoin));
        thenArm[3].Should().BeSameAs(innerJoinBlock.Body.Statements[0]);
        outer.FalseCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle();
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(join));
    }

    [Fact]
    public void ArmHoldingTheLatchOfTheEnclosingLoopStaysFlat() {
        // Arrange
        CfgBlock head = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock body = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock exit = CfgTestHelpers.CreateBlock(13, 0x60);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, head, body, exit],
            new() { [_entry] = [head], [head] = [body, exit], [body] = [head, exit] });
        LoweredBlock<CfgBlock> headBlock = Deciding(head, exit, body) with { HasEntryCheck = true };
        LoweredBlock<CfgBlock>[] blocks = [
            Lowered(_entry, true, true, "entry"), headBlock, Branching(body, head, new GotoNode(Target(exit))), Lowered(exit, false, false, "exit")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        DoWhileNode loop = method.Statements[1].Should().BeOfType<DoWhileNode>().Which;
        loop.Body.Statements[0].Should().BeSameAs(headBlock.Body.Statements[0]);
        loop.Body.Statements.Should().NotContain(statement => statement is IfElseNode);
    }

    [Fact]
    public void LoopInsideAnArmIsStructuredInsideTheIf() {
        // Arrange
        CfgBlock then = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock spin = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock join = CfgTestHelpers.CreateBlock(13, 0x60);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, then, spin, join],
            new() { [_entry] = [then, join], [then] = [spin], [spin] = [spin, join] });
        LoweredBlock<CfgBlock> thenBlock = Jumping(then, "then", spin);
        LoweredBlock<CfgBlock>[] blocks = [
            Deciding(_entry, then, join), thenBlock, Branching(spin, spin, new GotoNode(Target(join))), Lowered(join, false, false, "join")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(4);
        IfElseNode nested = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        IReadOnlyList<IVisitableAstNode> arm = nested.TrueCase.Should().BeOfType<BlockNode>().Which.Statements;
        arm.Should().HaveCount(3);
        arm[0].Should().BeSameAs(thenBlock.Body.Statements[0]);
        arm[1].Should().BeOfType<DoWhileNode>();
        arm[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(join));
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(join));
    }

    [Fact]
    public void JumpTableArmsThatMeetAtOneJoinAreNestedInTheirCases() {
        // Arrange
        CfgBlock first = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock second = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock third = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock join = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, first, second, third, join],
            new() { [_entry] = [first, second, third], [first] = [join], [second] = [join], [third] = [join] });
        LoweredBlock<CfgBlock> dispatch = Dispatching(_entry, first, second, third);
        LoweredBlock<CfgBlock> firstBlock = Jumping(first, "first", join);
        LoweredBlock<CfgBlock>[] blocks = [dispatch, firstBlock, Jumping(second, "second", join), Jumping(third, "third", join),
            Lowered(join, false, false, "join")];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(4);
        method.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Equal(new CommentNode("dispatch"));
        SwitchNode nested = method.Statements[1].Should().BeOfType<SwitchNode>().Which;
        nested.Sections.Should().HaveCount(3);
        nested.Sections[0].Body.Statements.Should().ContainSingle().Which.Should().BeSameAs(firstBlock.Body.Statements[0]);
        nested.Sections.Should().OnlyContain(section => section.Body.Statements[0] is InstructionBoundaryNode);
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(join));
        method.Statements[3].Should().Be(new CommentNode("join"));
    }

    [Fact]
    public void JumpTableArmsThatNeverLeaveAreNestedWithoutAJoin() {
        // Arrange
        CfgBlock first = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock second = CfgTestHelpers.CreateBlock(12, 0x50);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, first, second], new() { [_entry] = [first, second] });
        LoweredBlock<CfgBlock>[] blocks = [Dispatching(_entry, first, second), Ending(first, "first"), Ending(second, "second")];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements.Should().HaveCount(2);
        method.Statements[1].Should().BeOfType<SwitchNode>()
            .Which.Sections.Should().OnlyContain(section => section.Body.Statements[0] is InstructionBoundaryNode);
    }

    [Fact]
    public void DispatchWithOneNestableArmStaysFlat() {
        // Arrange
        CfgBlock first = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock second = CfgTestHelpers.CreateBlock(12, 0x50);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, first, second], new() { [_entry] = [first, second], [first] = [second] });
        LoweredBlock<CfgBlock> dispatch = Dispatching(_entry, first, second);
        LoweredBlock<CfgBlock>[] blocks = [dispatch, Jumping(first, "first", second), Ending(second, "second")];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements[0].Should().BeSameAs(dispatch.Body.Statements[0]);
        method.Statements.Should().NotContain(statement => statement is SwitchNode);
    }

    [Fact]
    public void JumpTableArmsThatLeaveToDifferentBlocksStayFlat() {
        // Arrange
        CfgBlock first = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock second = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock firstExit = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock secondExit = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, first, second, firstExit, secondExit],
            new() { [_entry] = [first, second, firstExit, secondExit], [first] = [firstExit], [second] = [secondExit] });
        LoweredBlock<CfgBlock> dispatch = Dispatching(_entry, first, second, firstExit, secondExit);
        LoweredBlock<CfgBlock>[] blocks = [
            dispatch, Jumping(first, "first", firstExit), Jumping(second, "second", secondExit),
            Ending(firstExit, "firstExit"), Ending(secondExit, "secondExit")
        ];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements[0].Should().BeSameAs(dispatch.Body.Statements[0]);
        method.Statements.Should().NotContain(statement => statement is SwitchNode);
    }

    [Fact]
    public void CallDispatchStaysFlat() {
        // Arrange
        CfgBlock first = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock second = CfgTestHelpers.CreateBlock(12, 0x50);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, first, second], new() { [_entry] = [first, second] });
        SwitchNode calls = new(new ExplicitCastNode(DataType.UINT16, new RegisterNode(DataType.UINT16, 3)), [
            new SwitchSection(0x0040, new BlockNode(new ProcedureCallHelperNode(CallDistance.Near, BitWidth.WORD_16), new GotoNode(Target(first)))),
            new SwitchSection(0x0050, new BlockNode(new ProcedureCallHelperNode(CallDistance.Near, BitWidth.WORD_16), new GotoNode(Target(second))))
        ], new BlockNode(Untested()));
        LoweredBlock<CfgBlock> dispatch = new(_entry, Target(_entry), false, false, false,
            new BlockNode(new InstructionBoundaryNode(_entry.Entry, "", new BlockNode(new CommentNode("dispatch"), calls))));
        LoweredBlock<CfgBlock>[] blocks = [dispatch, Ending(first, "first"), Ending(second, "second")];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        method.Statements[0].Should().BeSameAs(dispatch.Body.Statements[0]);
        method.Statements.Should().NotContain(statement => statement is SwitchNode);
    }

    [Fact]
    public void ArmHeadTargetedByTwoGotosRecordsSharedTargetAndTheOtherArmNeverComesBack() {
        // Arrange
        CfgBlock then = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock other = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock join = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock late = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, then, other, join, late],
            new() { [_entry] = [then, other], [then] = [join], [other] = [join], [late] = [other] });
        GotoNode trueGoto = new(then.Entry);
        GotoNode falseGoto = new(other.Entry);
        LoweredBlock<CfgBlock> decide = new(_entry, Target(_entry), false, false, true, new BlockNode(new InstructionBoundaryNode(_entry.Entry, "",
            new BlockNode(new CommentNode("decide"), new IfElseNode(Zero, new BlockNode(trueGoto), new BlockNode(falseGoto))))));
        LoweredBlock<CfgBlock>[] blocks = [
            decide, Jumping(then, "then", join), Jumping(other, "other", join),
            Jumping(late, "late", other), Lowered(join, false, false, "join")
        ];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        MethodStructurizer.Structure(blocks, loops, "method", Entries(), refusals, placeLoopExits: false, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);

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
        LoweredBlock<CfgBlock> decide = new(_entry, Target(_entry), false, false, true, new BlockNode(new InstructionBoundaryNode(_entry.Entry, "",
            new BlockNode(new CommentNode("decide"), branch))));
        LoweredBlock<CfgBlock>[] blocks = [decide, Lowered(_spin, false, true, "spin"), Lowered(_exit, false, false, "exit")];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        MethodStructurizer.Structure(blocks, _loops, "method", Entries(), refusals, placeLoopExits: false, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);

        // Assert
        refusals.Should().ContainSingle().Which.Should().Be(new KeyValuePair<GotoNode, GotoKeepReason>(trueGoto, GotoKeepReason.OtherArmNotGoto));
    }

    [Fact]
    public void NestedBranchRecordsNoRefusal() {
        // Arrange
        CfgBlock then = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock join = CfgTestHelpers.CreateBlock(12, 0x50);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, then, join], new() { [_entry] = [then, join], [then] = [join] });
        LoweredBlock<CfgBlock>[] blocks = [Deciding(_entry, then, join), Jumping(then, "then", join), Lowered(join, false, false, "join")];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        MethodStructurizer.Structure(blocks, loops, "method", Entries(), refusals, placeLoopExits: false, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);

        // Assert
        refusals.Should().BeEmpty();
    }

    [Fact]
    public void AConditionReadingAMethodScopeVariableNestsLikeMachineState() {
        // Arrange
        CfgBlock left = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock right = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock join = CfgTestHelpers.CreateBlock(13, 0x60);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, left, right, join],
            new() { [_entry] = [left, right], [left] = [join], [right] = [join] });
        IfElseNode branch = new(new BinaryOperationNode(DataType.BOOL,
                new MethodScopedVariableReferenceNode(DataType.UINT8, "flagOperand_0001"),
                BinaryOperation.NOT_EQUAL, new ConstantNode(DataType.UINT8, 0)),
            new BlockNode(new GotoNode(Target(left))), new BlockNode(new GotoNode(Target(right))));
        LoweredBlock<CfgBlock> decide = new(_entry, Target(_entry), false, false, true, new BlockNode(new InstructionBoundaryNode(_entry.Entry, "",
            new BlockNode(new CommentNode("decide"), branch))));
        LoweredBlock<CfgBlock> leftBlock = Jumping(left, "left", join);
        LoweredBlock<CfgBlock> rightBlock = Jumping(right, "right", join);
        LoweredBlock<CfgBlock>[] blocks = [decide, leftBlock, rightBlock, Lowered(join, false, false, "join")];

        // Act
        BlockNode method = Structure(blocks, loops, Entries());

        // Assert
        IfElseNode nested = method.Statements[1].Should().BeOfType<IfElseNode>().Which;
        nested.Condition.Should().BeSameAs(branch.Condition);
        nested.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeSameAs(leftBlock.Body.Statements[0]);
        nested.FalseCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeSameAs(rightBlock.Body.Statements[0]);
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().Be(Target(join));
    }

    [Fact]
    public void DivergingTrueArmIsNestedAndTheOtherTargetFollowsFlat() {
        // Arrange
        CfgBlock a = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock c = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock late = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock end = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, a, c, late, end],
            new() { [_entry] = [a, c, late], [c] = [end], [late] = [end] });
        LoweredBlock<CfgBlock> deadGoto = new(late, Target(late), false, false, true, new BlockNode(new InstructionBoundaryNode(late.Entry, "",
            new BlockNode(new CommentNode("late"), Untested(), new GotoNode(Target(c))))));
        LoweredBlock<CfgBlock>[] blocks = [
            BranchingTo(_entry, a, c), Returning(a, "a"), Ending(c, "c"), deadGoto, Lowered(end, false, false, "end")
        ];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        BlockNode structured = MethodStructurizer.Structure(blocks, loops, "method", Entries(), refusals, placeLoopExits: false, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);
        BlockNode body = FallthroughGotoRemover.Remove(structured);

        // Assert
        IfElseNode branch = body.Statements[1].Should().BeOfType<IfElseNode>().Which;
        branch.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(a.Entry);
        body.Statements[2].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(c.Entry);
        body.Statements[3].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(late.Entry);
        CountNodes<GotoNode>(body, _ => true).Should().Be(0);
        refusals.Should().BeEmpty();
    }

    [Fact]
    public void DivergingFalseArmIsNestedUnderTheNegatedCondition() {
        // Arrange
        CfgBlock b = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock c = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock late = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock end = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, b, c, late, end],
            new() { [_entry] = [c, b, late], [c] = [end], [late] = [end] });
        LoweredBlock<CfgBlock> deadGoto = new(late, Target(late), false, false, true, new BlockNode(new InstructionBoundaryNode(late.Entry, "",
            new BlockNode(new CommentNode("late"), Untested(), new GotoNode(Target(c))))));
        LoweredBlock<CfgBlock>[] blocks = [
            BranchingTo(_entry, c, b), Returning(b, "b"), Ending(c, "c"), deadGoto, Lowered(end, false, false, "end")
        ];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        BlockNode structured = MethodStructurizer.Structure(blocks, loops, "method", Entries(), refusals, placeLoopExits: false, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);
        BlockNode body = FallthroughGotoRemover.Remove(structured);

        // Assert
        IfElseNode branch = body.Statements[1].Should().BeOfType<IfElseNode>().Which;
        branch.Condition.Should().BeOfType<UnaryOperationNode>();
        branch.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(b.Entry);
        CountNodes<GotoNode>(body, _ => true).Should().Be(0);
        refusals.Should().BeEmpty();
    }

    [Fact]
    public void TwoDivergingArmsNestOnlyTheLaterArm() {
        // Arrange
        CfgBlock a = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock b = CfgTestHelpers.CreateBlock(12, 0x50);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, a, b], new() { [_entry] = [a, b] });
        LoweredBlock<CfgBlock>[] blocks = [BranchingTo(_entry, a, b), Returning(a, "a"), Returning(b, "b")];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        BlockNode structured = MethodStructurizer.Structure(blocks, loops, "method", Entries(), refusals, placeLoopExits: false, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);
        BlockNode body = FallthroughGotoRemover.Remove(structured);

        // Assert
        body.Statements.Should().HaveCount(3);
        IfElseNode branch = body.Statements[1].Should().BeOfType<IfElseNode>().Which;
        branch.Condition.Should().BeOfType<UnaryOperationNode>();
        branch.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(b.Entry);
        body.Statements[2].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(a.Entry);
        CountNodes<GotoNode>(body, _ => true).Should().Be(0);
        refusals.Should().BeEmpty();
    }

    [Fact]
    public void DivergingArmHeadTargetedByTwoGotosIsNotNested() {
        // Arrange
        CfgBlock late = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock a = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock c = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock end = CfgTestHelpers.CreateBlock(14, 0x70);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, late, a, c, end],
            new() { [_entry] = [a, c, late], [late] = [a, end], [c] = [end] });
        GotoNode trueGoto = new(a.Entry);
        LoweredBlock<CfgBlock> decide = new(_entry, Target(_entry), false, false, true, new BlockNode(new InstructionBoundaryNode(_entry.Entry, "",
            new BlockNode(new CommentNode("decide"), new IfElseNode(Zero, new BlockNode(trueGoto), new BlockNode(new GotoNode(Target(c))))))));
        LoweredBlock<CfgBlock>[] blocks = [
            decide, Jumping(late, "late", a), Returning(a, "a"),
            Ending(c, "c"), Lowered(end, false, false, "end")
        ];
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        BlockNode body = MethodStructurizer.Structure(blocks, loops, "method", Entries(), refusals, placeLoopExits: false, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);

        // Assert
        body.Statements[2].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(a.Entry);
        refusals[trueGoto].Should().Be(GotoKeepReason.SharedTarget);
    }

    [Fact]
    public void ExitPlacementMovesTheMostUsedExitRightAfterTheLoop() {
        // Arrange
        (NaturalLoopForest<CfgBlock> loops, CfgBlock head, CfgBlock mid, CfgBlock tail, CfgBlock x, CfgBlock y, CfgBlock z, CfgBlock end) = TwoExitLoop();
        LoweredBlock<CfgBlock>[] blocks = TwoExitLoopBlocks(head, mid, tail, x, y, z, end, tie: false);
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        BlockNode structured = MethodStructurizer.Structure(blocks, loops, "method", Entries(), refusals, true, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);
        BlockNode body = FallthroughGotoRemover.Remove(structured);

        // Assert
        InfiniteLoopNode loop = body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        body.Statements[2].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(y.Entry);
        body.Statements[3].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(z.Entry);
        CountNodes<GotoNode>(loop.Body, jump => Equals(jump.Target, Target(y))).Should().Be(0);
        CountNodes<BreakNode>(loop.Body, _ => true).Should().Be(2);
        CountNodes<GotoNode>(loop.Body, jump => Equals(jump.Target, Target(x))).Should().Be(1);
        CountNodes<GotoNode>(body, jump => Equals(jump.Target, Target(z))).Should().Be(0);
    }

    [Fact]
    public void WithoutExitPlacementTheFirstExitInTheLayoutStaysNext() {
        // Arrange
        (NaturalLoopForest<CfgBlock> loops, CfgBlock head, CfgBlock mid, CfgBlock tail, CfgBlock x, CfgBlock y, CfgBlock z, CfgBlock end) = TwoExitLoop();
        LoweredBlock<CfgBlock>[] blocks = TwoExitLoopBlocks(head, mid, tail, x, y, z, end, tie: false);
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        BlockNode structured = MethodStructurizer.Structure(blocks, loops, "method", Entries(), refusals, false, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);
        BlockNode body = FallthroughGotoRemover.Remove(structured);

        // Assert
        InfiniteLoopNode loop = body.Statements[1].Should().BeOfType<InfiniteLoopNode>().Which;
        body.Statements[2].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(x.Entry);
        CountNodes<BreakNode>(loop.Body, _ => true).Should().Be(1);
        CountNodes<GotoNode>(loop.Body, jump => Equals(jump.Target, Target(y))).Should().Be(2);
    }

    [Fact]
    public void ExitPlacementOnATieMovesNothing() {
        // Arrange
        (NaturalLoopForest<CfgBlock> loops, CfgBlock head, CfgBlock mid, CfgBlock tail, CfgBlock x, CfgBlock y, CfgBlock z, CfgBlock end) = TwoExitLoop();
        LoweredBlock<CfgBlock>[] blocks = TwoExitLoopBlocks(head, mid, tail, x, y, z, end, tie: true);
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance);

        // Act
        BlockNode structured = MethodStructurizer.Structure(blocks, loops, "method", Entries(), refusals, true, allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);
        BlockNode body = FallthroughGotoRemover.Remove(structured);

        // Assert
        body.Statements[1].Should().BeOfType<InfiniteLoopNode>();
        body.Statements[2].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(x.Entry);
        body.Statements[3].Should().BeOfType<InstructionBoundaryNode>().Which.Node.Should().BeSameAs(y.Entry);
    }

    private static CfgNodeGotoTarget Target(CfgBlock block) => new(block.Entry);

    private static BlockNode Structure(IReadOnlyList<LoweredBlock<CfgBlock>> blocks, NaturalLoopForest<CfgBlock> loops,
        IReadOnlySet<IGotoTarget> entryTargets) =>
        MethodStructurizer.Structure(blocks, loops, "method", entryTargets,
            new Dictionary<GotoNode, GotoKeepReason>(ReferenceEqualityComparer.Instance), placeLoopExits: false,
            allowLocalsDeclaredInBlock: false, localsDeclaredInDominatingBlocks: false);

    private static LoweredBlock<CfgBlock> Lowered(CfgBlock block, bool holdsEntry, bool hasEntryCheck, string name) =>
        new(block, Target(block), hasEntryCheck, holdsEntry, true, new BlockNode(new CommentNode(name)));

    private static LoweredBlock<CfgBlock> Branching(CfgBlock block, CfgBlock header, IVisitableAstNode exit) {
        IfElseNode branch = new(Zero, new BlockNode(new GotoNode(Target(header))), new BlockNode(exit));
        string name = ReferenceEquals(block, header) ? "inner" : "latch";
        return new(block, Target(block), ReferenceEquals(block, header), false, true,
            new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode(name), branch))));
    }

    private static LoweredBlock<CfgBlock> Deciding(CfgBlock block, CfgBlock whenTrue, CfgBlock whenFalse) {
        IfElseNode branch = new(Zero, new BlockNode(new GotoNode(Target(whenTrue))), new BlockNode(new GotoNode(Target(whenFalse))));
        return new(block, Target(block), false, false, true, new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode("decide"), branch))));
    }

    private static LoweredBlock<CfgBlock> Jumping(CfgBlock block, string name, CfgBlock target) =>
        new(block, Target(block), false, false, true, new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode(name), new GotoNode(Target(target))))));

    private static LoweredBlock<CfgBlock> Dispatching(CfgBlock block, params CfgBlock[] targets) {
        SwitchNode dispatch = new(new ExplicitCastNode(DataType.UINT16, new RegisterNode(DataType.UINT16, 3)),
            targets.Select(target => new SwitchSection(target.Entry.Address.Offset, new BlockNode(new GotoNode(Target(target))))).ToList(),
            new BlockNode(Untested()));
        return new(block, Target(block), false, false, true, new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode("dispatch"), dispatch))));
    }

    private static LoweredBlock<CfgBlock> Ending(CfgBlock block, string name) =>
        new(block, Target(block), false, false, true, new BlockNode(new InstructionBoundaryNode(block.Entry, "", new BlockNode(new CommentNode(name), Untested()))));

    private static LoweredBlock<CfgBlock> Returning(CfgBlock block, string name) =>
        new(block, Target(block), false, false, true, new BlockNode(new InstructionBoundaryNode(block.Entry, "",
            new BlockNode(new CommentNode(name), new ReturnNode(new ProcedureReturnHelperNode(CallDistance.Near, BitWidth.WORD_16))))));

    private static LoweredBlock<CfgBlock> BranchingTo(CfgBlock block, CfgBlock whenTrue, CfgBlock whenFalse) {
        IfElseNode branch = new(Zero, new BlockNode(new GotoNode(Target(whenTrue))), new BlockNode(new GotoNode(Target(whenFalse))));
        return new(block, Target(block), false, false, true, new BlockNode(new InstructionBoundaryNode(block.Entry, "",
            new BlockNode(new CommentNode("decide"), branch))));
    }

    private static FailAsUntestedNode Untested() => new([UntestedMessagePart.Literal("Untested near jump target at 1000:0000")]);

    private IfElseNode BackEdgeIf(ValueNode condition, bool toHeaderWhenTrue, IVisitableAstNode exit) {
        BlockNode backEdge = new(new GotoNode(Target(_spin)));
        BlockNode exitArm = new(exit);
        if (toHeaderWhenTrue) {
            return new IfElseNode(condition, backEdge, exitArm);
        }
        return new IfElseNode(condition, exitArm, backEdge);
    }

    private IVisitableAstNode Terminator(string name) => name switch {
        "conditionReadsLocal" => BackEdgeIf(new VariableDeclarationNode(DataType.BOOL, "equal", Zero).Reference,
            toHeaderWhenTrue: true, new GotoNode(Target(_exit))),
        "exitArmHasCode" => new IfElseNode(Zero, new BlockNode(new GotoNode(Target(_spin))),
            new BlockNode(new MethodCallNode(null, "Foo"), new GotoNode(Target(_exit)))),
        "bothArmsRestart" => new IfElseNode(Zero, new BlockNode(new GotoNode(Target(_spin))), new BlockNode(new GotoNode(Target(_spin)))),
        "noBackEdge" => new IfElseNode(Zero, new BlockNode(new GotoNode(Target(_exit))), new BlockNode(new GotoNode(Target(_entry)))),
        "untestedMessageReadsRegister" => BackEdgeIf(Zero, toHeaderWhenTrue: true, new FailAsUntestedNode([
            UntestedMessagePart.Literal("Untested near jump target 0x"),
            UntestedMessagePart.Hole(new RegisterNode(DataType.UINT16, 0), "X4")])),
        "tryCatch" => new CpuFaultTryCatchNode(
            new BlockNode(BackEdgeIf(Zero, toHeaderWhenTrue: true, new GotoNode(Target(_exit)))),
            new SegmentedAddress(0x1000, 0x0010), []),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown terminator.")
    };

    private LoweredBlock<CfgBlock> Spin(IVisitableAstNode terminator) =>
        new(_spin, Target(_spin), true, false, true,
            new BlockNode(new InstructionBoundaryNode(_spin.Entry, "", new BlockNode(new CommentNode("spin"), terminator))));

    private BlockNode StructureWith(LoweredBlock<CfgBlock> spin, IReadOnlySet<IGotoTarget> entryTargets) =>
        Structure([Lowered(_entry, true, true, "entry"), spin, Lowered(_exit, false, false, "exit")], _loops, entryTargets);

    private HashSet<IGotoTarget> Entries(params ICfgNode[] more) =>
        [Target(_entry), .. more.Select(node => (IGotoTarget)new CfgNodeGotoTarget(node))];

    private LoweredBlock<CfgBlock>[] TwoExitLoopBlocks(CfgBlock head, CfgBlock mid, CfgBlock tail, CfgBlock x, CfgBlock y,
        CfgBlock z, CfgBlock end, bool tie) {
        IVisitableAstNode midExit = tie ? new GotoNode(Target(x)) : new GotoNode(Target(y));
        BlockNode tailExit = tie
            ? new BlockNode(new CommentNode("still in the loop"))
            : new BlockNode(new GotoNode(Target(x)), new CommentNode("after the goto"));
        LoweredBlock<CfgBlock> headBlock = new(head, Target(head), true, false, true, new BlockNode(new InstructionBoundaryNode(head.Entry, "",
            new BlockNode(new CommentNode("head"), new IfElseNode(Zero, new BlockNode(new GotoNode(Target(mid))), new BlockNode(new GotoNode(Target(y))))))));
        LoweredBlock<CfgBlock> midBlock = new(mid, Target(mid), false, false, true, new BlockNode(new InstructionBoundaryNode(mid.Entry, "",
            new BlockNode(new CommentNode("mid"), new IfElseNode(Zero, new BlockNode(new GotoNode(Target(tail))), new BlockNode(midExit))))));
        LoweredBlock<CfgBlock> tailBlock = new(tail, Target(tail), false, false, true, new BlockNode(new InstructionBoundaryNode(tail.Entry, "",
            new BlockNode(new CommentNode("tail"), new IfElseNode(Zero, new BlockNode(new GotoNode(Target(head))), tailExit)))));
        return [
            Lowered(_entry, true, true, "entry"), headBlock, midBlock, tailBlock,
            Jumping(x, "x", end), Jumping(y, "y", z), Jumping(z, "z", end), Lowered(end, false, false, "end")
        ];
    }

    private (NaturalLoopForest<CfgBlock> Loops, CfgBlock Head, CfgBlock Mid, CfgBlock Tail, CfgBlock X, CfgBlock Y, CfgBlock Z, CfgBlock End) TwoExitLoop() {
        CfgBlock head = CfgTestHelpers.CreateBlock(11, 0x40);
        CfgBlock mid = CfgTestHelpers.CreateBlock(12, 0x50);
        CfgBlock tail = CfgTestHelpers.CreateBlock(13, 0x60);
        CfgBlock x = CfgTestHelpers.CreateBlock(14, 0x70);
        CfgBlock y = CfgTestHelpers.CreateBlock(15, 0x80);
        CfgBlock z = CfgTestHelpers.CreateBlock(17, 0xA0);
        CfgBlock end = CfgTestHelpers.CreateBlock(16, 0x90);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([_entry, head, mid, tail, x, y, z, end],
            new() { [_entry] = [head], [head] = [mid, y], [mid] = [tail, y], [tail] = [head, x], [x] = [end], [y] = [z], [z] = [end] });
        return (loops, head, mid, tail, x, y, z, end);
    }

    /// <summary>A minimal generic block type: identity only, and its own goto target.</summary>
    private sealed class TestBlock(int id) : IGotoTarget {
        public int Id => id;
    }

    private static NaturalLoopForest<TestBlock> TestLoops(IReadOnlyList<TestBlock> blocks,
        Dictionary<TestBlock, IReadOnlyList<TestBlock>> successors) {
        BlockGraph<TestBlock> graph = new(blocks, [blocks[0]], successors, new Dictionary<TestBlock, TestBlock?>(),
            Comparer<TestBlock>.Create((first, second) => first.Id.CompareTo(second.Id)), block => $"block {block.Id}");
        DepthFirstOrdering<TestBlock> traversal = new([.. graph.EntryBlocks, .. graph.Blocks], graph.GetSuccessors);
        return NaturalLoopForest<TestBlock>.Build(graph, traversal.RetreatingEdges);
    }

    private static InstructionBoundaryNode Boundary(int offset, params IVisitableAstNode[] statements) =>
        new(CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, (ushort)offset)), "", new BlockNode(statements));

    private static LoweredBlock<TestBlock> Lowered(TestBlock block, bool hasEntryCheck, bool needsCheck,
        params IVisitableAstNode[] statements) =>
        new(block, block, hasEntryCheck, false, needsCheck, new BlockNode(statements));

    private static LoweredBlock<TestBlock> Jumping(TestBlock block, int offset, TestBlock target) =>
        Lowered(block, false, false, Boundary(offset, new CommentNode("jump"), new GotoNode(target)));

    private static BlockNode StructureTestBlocks(IReadOnlyList<LoweredBlock<TestBlock>> blocks,
        NaturalLoopForest<TestBlock> loops, bool allowLocalsDeclaredInBlock) =>
        MethodStructurizer.Structure(blocks, loops, "method", new HashSet<IGotoTarget>(),
            new Dictionary<GotoNode, GotoKeepReason>(ReferenceEqualityComparer.Instance), placeLoopExits: false,
            allowLocalsDeclaredInBlock, localsDeclaredInDominatingBlocks: false);

    [Fact]
    public void HeaderThatOnlyTestsALoopConditionBecomesAWhileNode() {
        // Arrange
        TestBlock first = new(0);
        TestBlock head = new(1);
        TestBlock body = new(2);
        TestBlock exit = new(3);
        NaturalLoopForest<TestBlock> loops = TestLoops([first, head, body, exit],
            new() { [first] = [head], [head] = [body, exit], [body] = [head] });
        LoweredBlock<TestBlock> headBlock = Lowered(head, false, false,
            Boundary(0x40, new IfElseNode(Zero, new BlockNode(new GotoNode(body)), new BlockNode(new GotoNode(exit)))));
        LoweredBlock<TestBlock>[] blocks = [
            Lowered(first, false, false, Boundary(0x00, new CommentNode("first"))),
            headBlock,
            Jumping(body, 0x50, head),
            Lowered(exit, false, false, Boundary(0x60, new CommentNode("exit")))
        ];

        // Act
        BlockNode method = StructureTestBlocks(blocks, loops, false);

        // Assert
        PreTestLoopNode loop = method.Statements[1].Should().BeOfType<PreTestLoopNode>().Which;
        loop.Condition.Should().BeSameAs(Zero);
        loop.HeaderTarget.Should().BeSameAs(head);
        loop.Body.Statements.Should().ContainSingle().Which.Should().BeSameAs(blocks[2].Body.Statements[0]);
        method.Statements[2].Should().BeOfType<GotoNode>().Which.Target.Should().BeSameAs(exit);
        method.Statements[3].Should().BeSameAs(blocks[3].Body.Statements[0]);
    }

    [Fact]
    public void InsideInstructionLoopWithoutAnEntryCheckIsAccepted() {
        // Arrange
        TestBlock first = new(0);
        TestBlock head = new(1);
        NaturalLoopForest<TestBlock> loops = TestLoops([first, head], new() { [first] = [head], [head] = [head] });
        LoweredBlock<TestBlock>[] blocks = [
            Lowered(first, false, false, Boundary(0x00, new CommentNode("first"))),
            Lowered(head, false, false, Boundary(0x40, new CommentNode("head"), new GotoNode(head)))
        ];

        // Act
        BlockNode method = StructureTestBlocks(blocks, loops, false);

        // Assert
        method.Statements[1].Should().BeOfType<InfiniteLoopNode>();
    }

    [Fact]
    public void InsideInstructionLoopWithLoopHeaderNeedsCheckThrows() {
        // Arrange
        TestBlock first = new(0);
        TestBlock head = new(1);
        NaturalLoopForest<TestBlock> loops = TestLoops([first, head], new() { [first] = [head], [head] = [head] });
        LoweredBlock<TestBlock>[] blocks = [
            Lowered(first, false, false, Boundary(0x00, new CommentNode("first"))),
            Lowered(head, false, true, Boundary(0x40, new CommentNode("head"), new GotoNode(head)))
        ];

        // Act
        Action structure = () => StructureTestBlocks(blocks, loops, false);

        // Assert
        structure.Should().Throw<InvalidOperationException>().WithMessage("*has no CheckExternalEvents at its entry*");
    }

    [Fact]
    public void ConditionReadingALocalDeclaredInTheSameBlockNestsOnlyWhenAllowed() {
        // Arrange
        TestBlock decide = new(0);
        TestBlock left = new(1);
        TestBlock right = new(2);
        TestBlock join = new(3);
        NaturalLoopForest<TestBlock> loops = TestLoops([decide, left, right, join],
            new() { [decide] = [left, right], [left] = [join], [right] = [join] });
        VariableDeclarationNode local = new(DataType.BOOL, "local", Zero);
        IfElseNode branch = new(local.Reference, new BlockNode(new GotoNode(left)), new BlockNode(new GotoNode(right)));
        LoweredBlock<TestBlock> decideBlock = Lowered(decide, false, false, Boundary(0x00, local, branch));
        LoweredBlock<TestBlock>[] blocks = [
            decideBlock,
            Jumping(left, 0x40, join),
            Jumping(right, 0x50, join),
            Lowered(join, false, false, Boundary(0x60, new CommentNode("join")))
        ];

        // Act
        BlockNode refused = StructureTestBlocks(blocks, loops, false);
        BlockNode allowed = StructureTestBlocks(blocks, loops, true);

        // Assert
        refused.Statements[0].Should().BeOfType<InstructionBoundaryNode>()
            .Which.Body.Statements.Should().Contain(statement => statement is IfElseNode);
        refused.Statements[1].Should().BeOfType<InstructionBoundaryNode>();
        allowed.Statements[1].Should().BeOfType<IfElseNode>()
            .Which.Condition.Should().BeSameAs(local.Reference);
    }

    private static int CountNodes<T>(IVisitableAstNode node, Func<T, bool> matches) {
        int count = node is T match && matches(match) ? 1 : 0;
        switch (node) {
            case BlockNode block:
                return count + block.Statements.Sum(statement => CountNodes(statement, matches));
            case InstructionBoundaryNode boundary:
                return count + CountNodes(boundary.Body, matches);
            case IfElseNode ifElse:
                return count + CountNodes(ifElse.TrueCase, matches) + CountNodes(ifElse.FalseCase, matches);
            case WhileNode whileNode:
                return count + CountNodes(whileNode.Body, matches);
            case InfiniteLoopNode infiniteLoop:
                return count + CountNodes(infiniteLoop.Body, matches);
            case DoWhileNode doWhile:
                return count + CountNodes(doWhile.Body, matches);
            case SwitchNode switchNode:
                return count + switchNode.Sections.Sum(section => CountNodes(section.Body, matches))
                    + CountNodes(switchNode.Default, matches);
            case CpuFaultTryCatchNode tryCatch:
                return count + CountNodes(tryCatch.Body, matches);
            default:
                return count;
        }
    }
}