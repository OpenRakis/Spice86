namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

public class GotoCensusClassifierTest {
    private static readonly Dictionary<GotoNode, GotoKeepReason> NoRefusals = new(ReferenceEqualityComparer.Instance);

    private static CfgNodeGotoTarget Target(ICfgNode node) => new(node);

    private static LoweredBlock<CfgBlock> Lowered(CfgBlock block) =>
        new(block, Target(block.Entry), false, false, true, Empty());

    private static LoweredBlock<CfgBlock> LoweredEntry(CfgBlock block, BlockNode body) =>
        new(block, Target(block.Entry), false, true, true, body);

    private static InstructionBoundaryNode Boundary(CfgBlock block, params IVisitableAstNode[] statements) =>
        new(block.Entry, "", new BlockNode(statements));

    private static IReadOnlySet<IGotoTarget> EntryTargets(params ICfgNode[] nodes) =>
        nodes.Select(node => (IGotoTarget)Target(node)).ToHashSet();

    private static GotoCensus Classify(IVisitableAstNode entryDispatch, IVisitableAstNode body, NaturalLoopForest<CfgBlock> loops,
        IReadOnlySet<IGotoTarget> entryTargets, IReadOnlyList<LoweredBlock<CfgBlock>> blocks) =>
        Classify(entryDispatch, body, loops, entryTargets, blocks, NoRefusals);

    private static GotoCensus Classify(IVisitableAstNode entryDispatch, IVisitableAstNode body, NaturalLoopForest<CfgBlock> loops,
        IReadOnlySet<IGotoTarget> entryTargets, IReadOnlyList<LoweredBlock<CfgBlock>> blocks,
        IReadOnlyDictionary<GotoNode, GotoKeepReason> refusals) =>
        GotoCensusClassifier.Classify(Sequence(entryDispatch), Sequence(body), loops, entryTargets, blocks,
            block => block.Instructions, refusals);

    [Fact]
    public void GotoOfTheEntryDispatchCountsAsEntryDispatch() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry], new());
        SwitchNode dispatch = new(new LoadOffsetParameterNode(),
            [new SwitchSection(0, new BlockNode(new GotoNode(Target(entry.Entry))))], Empty());

        // Act
        GotoCensus census = Classify(dispatch, Boundary(entry, new CommentNode("entry")), loops,
            EntryTargets(entry.Entry), [LoweredEntry(entry, Empty())]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.EntryDispatch).Should().Be(1);
    }

    [Fact]
    public void CyclicJumpCountsAsCyclicJump() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry], new());

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, new CyclicJumpNode("otherMethod", 0)), loops,
            EntryTargets(entry.Entry), [LoweredEntry(entry, Empty())]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.CyclicJump).Should().Be(1);
    }

    [Fact]
    public void GotoToABlockHoldingAPartitionEntryCountsAsTargetIsEntry() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock target = CfgTestHelpers.CreateBlock(2, 0x10);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry, target], new() { [entry] = [target] });

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, new GotoNode(Target(target.Entry))), loops,
            EntryTargets(entry.Entry, target.Entry), [LoweredEntry(entry, Empty()), LoweredEntry(target, Empty())]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.TargetIsEntry).Should().Be(1);
    }

    [Fact]
    public void GotoToABlockOfALoopWithASecondEntryCountsAsLoopWithSecondEntry() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock head = CfgTestHelpers.CreateBlock(2, 0x10);
        ICfgNode secondEntry = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0012));
        head.Append(secondEntry);
        CfgBlock spin = CfgTestHelpers.CreateBlock(3, 0x20);
        CfgBlock exit = CfgTestHelpers.CreateBlock(4, 0x30);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry, head, spin, exit],
            new() { [entry] = [head], [head] = [spin], [spin] = [head, exit] });
        LoweredBlock<CfgBlock> headBlock = LoweredEntry(head,
            new BlockNode(new InstructionBoundaryNode(secondEntry, "", Empty())));

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, new GotoNode(Target(spin.Entry))), loops,
            EntryTargets(entry.Entry, secondEntry), [LoweredEntry(entry, Empty()), headBlock, Lowered(spin), Lowered(exit)]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.LoopWithSecondEntry).Should().Be(1);
    }

    [Fact]
    public void GotoToTheEnclosingLoopHeaderCountsAsLoopRestart() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock spin = CfgTestHelpers.CreateBlock(2, 0x10);
        CfgBlock exit = CfgTestHelpers.CreateBlock(3, 0x20);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry, spin, exit],
            new() { [entry] = [spin], [spin] = [spin, exit] });
        InfiniteLoopNode loop = new(new BlockNode(Boundary(spin, new GotoNode(Target(spin.Entry)))));

        // Act
        GotoCensus census = Classify(Empty(), Sequence(Boundary(entry, new CommentNode("entry")), loop), loops,
            EntryTargets(entry.Entry), [LoweredEntry(entry, Empty()), Lowered(spin), Lowered(exit)]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.LoopRestart).Should().Be(1);
    }

    [Fact]
    public void GotoOutsideTheEnclosingLoopCountsAsLoopExit() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock spin = CfgTestHelpers.CreateBlock(2, 0x10);
        CfgBlock exit = CfgTestHelpers.CreateBlock(3, 0x20);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry, spin, exit],
            new() { [entry] = [spin], [spin] = [spin, exit] });
        InfiniteLoopNode loop = new(new BlockNode(Boundary(spin, new GotoNode(Target(exit.Entry)))));

        // Act
        GotoCensus census = Classify(Empty(), Sequence(Boundary(entry, new CommentNode("entry")), loop), loops,
            EntryTargets(entry.Entry), [LoweredEntry(entry, Empty()), Lowered(spin), Lowered(exit)]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.LoopExit).Should().Be(1);
    }

    [Fact]
    public void GotoBackwardToANonDominatorCountsAsIrreducible() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock source = CfgTestHelpers.CreateBlock(2, 0x10);
        CfgBlock target = CfgTestHelpers.CreateBlock(3, 0x20);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry, source, target],
            new() { [entry] = [source], [source] = [target] });
        LoweredBlock<CfgBlock>[] blocks = [LoweredEntry(entry, Empty()), Lowered(target), Lowered(source)];

        // Act
        GotoCensus census = Classify(Empty(), Boundary(source, new GotoNode(Target(target.Entry))), loops,
            EntryTargets(entry.Entry), blocks);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.Irreducible).Should().Be(1);
    }

    [Fact]
    public void GotoWithARecordedRefusalCountsAsThatReason() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock target = CfgTestHelpers.CreateBlock(2, 0x10);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry, target], new() { [entry] = [target] });
        GotoNode jump = new(Target(target.Entry));
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance) {
            [jump] = GotoKeepReason.NoSingleJoin
        };

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, jump), loops,
            EntryTargets(entry.Entry), [LoweredEntry(entry, Empty()), Lowered(target)], refusals);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.NoSingleJoin).Should().Be(1);
    }

    [Fact]
    public void GotosToASharedTargetEachCountAsSharedTarget() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock middle = CfgTestHelpers.CreateBlock(2, 0x10);
        CfgBlock target = CfgTestHelpers.CreateBlock(3, 0x20);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry, middle, target],
            new() { [entry] = [middle], [middle] = [target] });

        // Act
        GotoCensus census = Classify(Empty(),
            Sequence(Boundary(entry, new GotoNode(Target(target.Entry))),
                Boundary(middle, new GotoNode(Target(target.Entry)))), loops,
            EntryTargets(entry.Entry), [LoweredEntry(entry, Empty()), Lowered(middle), Lowered(target)]);

        // Assert
        census.Total.Should().Be(2);
        census.Count(GotoKeepReason.SharedTarget).Should().Be(2);
    }

    [Fact]
    public void SingleUnexplainedGotoCountsAsNotNextInLayout() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock target = CfgTestHelpers.CreateBlock(2, 0x10);
        NaturalLoopForest<CfgBlock> loops = CfgTestHelpers.BuildLoopForest([entry, target], new() { [entry] = [target] });

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, new GotoNode(Target(target.Entry))), loops,
            EntryTargets(entry.Entry), [LoweredEntry(entry, Empty()), Lowered(target)]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.NotNextInLayout).Should().Be(1);
    }
}
