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

    private static LoweredBlock Lowered(CfgBlock block) => new(block, false, Empty());

    private static InstructionBoundaryNode Boundary(CfgBlock block, params IVisitableAstNode[] statements) =>
        new(block.Entry, "", new BlockNode(statements));

    private static GotoCensus Classify(IVisitableAstNode entryDispatch, IVisitableAstNode body, NaturalLoopForest loops,
        IReadOnlySet<ICfgNode> entryNodes, IReadOnlyList<LoweredBlock> blocks) =>
        Classify(entryDispatch, body, loops, entryNodes, blocks, NoRefusals);

    private static GotoCensus Classify(IVisitableAstNode entryDispatch, IVisitableAstNode body, NaturalLoopForest loops,
        IReadOnlySet<ICfgNode> entryNodes, IReadOnlyList<LoweredBlock> blocks,
        IReadOnlyDictionary<GotoNode, GotoKeepReason> refusals) =>
        GotoCensusClassifier.Classify(Sequence(entryDispatch), Sequence(body), loops, entryNodes, blocks, refusals);

    [Fact]
    public void GotoOfTheEntryDispatchCountsAsEntryDispatch() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry], new());
        SwitchNode dispatch = new(new LoadOffsetParameterNode(),
            [new SwitchSection(0, new BlockNode(new GotoNode(entry.Entry)))], Empty());

        // Act
        GotoCensus census = Classify(dispatch, Boundary(entry, new CommentNode("entry")), loops,
            new HashSet<ICfgNode> { entry.Entry }, [Lowered(entry)]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.EntryDispatch).Should().Be(1);
    }

    [Fact]
    public void CyclicJumpCountsAsCyclicJump() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry], new());

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, new CyclicJumpNode("otherMethod", 0)), loops,
            new HashSet<ICfgNode> { entry.Entry }, [Lowered(entry)]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.CyclicJump).Should().Be(1);
    }

    [Fact]
    public void GotoToABlockHoldingAPartitionEntryCountsAsTargetIsEntry() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock target = CfgTestHelpers.CreateBlock(2, 0x10);
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry, target], new() { [entry] = [target] });

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, new GotoNode(target.Entry)), loops,
            new HashSet<ICfgNode> { entry.Entry, target.Entry }, [Lowered(entry), Lowered(target)]);

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
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry, head, spin, exit],
            new() { [entry] = [head], [head] = [spin], [spin] = [head, exit] });

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, new GotoNode(spin.Entry)), loops,
            new HashSet<ICfgNode> { entry.Entry, secondEntry }, [Lowered(entry), Lowered(head), Lowered(spin), Lowered(exit)]);

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
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry, spin, exit],
            new() { [entry] = [spin], [spin] = [spin, exit] });
        InfiniteLoopNode loop = new(new BlockNode(Boundary(spin, new GotoNode(spin.Entry))));

        // Act
        GotoCensus census = Classify(Empty(), Sequence(Boundary(entry, new CommentNode("entry")), loop), loops,
            new HashSet<ICfgNode> { entry.Entry }, [Lowered(entry), Lowered(spin), Lowered(exit)]);

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
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry, spin, exit],
            new() { [entry] = [spin], [spin] = [spin, exit] });
        InfiniteLoopNode loop = new(new BlockNode(Boundary(spin, new GotoNode(exit.Entry))));

        // Act
        GotoCensus census = Classify(Empty(), Sequence(Boundary(entry, new CommentNode("entry")), loop), loops,
            new HashSet<ICfgNode> { entry.Entry }, [Lowered(entry), Lowered(spin), Lowered(exit)]);

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
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry, source, target],
            new() { [entry] = [source], [source] = [target] });
        LoweredBlock[] blocks = [Lowered(entry), Lowered(target), Lowered(source)];

        // Act
        GotoCensus census = Classify(Empty(), Boundary(source, new GotoNode(target.Entry)), loops,
            new HashSet<ICfgNode> { entry.Entry }, blocks);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.Irreducible).Should().Be(1);
    }

    [Fact]
    public void GotoWithARecordedRefusalCountsAsThatReason() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock target = CfgTestHelpers.CreateBlock(2, 0x10);
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry, target], new() { [entry] = [target] });
        GotoNode jump = new(target.Entry);
        Dictionary<GotoNode, GotoKeepReason> refusals = new(ReferenceEqualityComparer.Instance) {
            [jump] = GotoKeepReason.NoSingleJoin
        };

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, jump), loops,
            new HashSet<ICfgNode> { entry.Entry }, [Lowered(entry), Lowered(target)], refusals);

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
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry, middle, target],
            new() { [entry] = [middle], [middle] = [target] });

        // Act
        GotoCensus census = Classify(Empty(),
            Sequence(Boundary(entry, new GotoNode(target.Entry)), Boundary(middle, new GotoNode(target.Entry))), loops,
            new HashSet<ICfgNode> { entry.Entry }, [Lowered(entry), Lowered(middle), Lowered(target)]);

        // Assert
        census.Total.Should().Be(2);
        census.Count(GotoKeepReason.SharedTarget).Should().Be(2);
    }

    [Fact]
    public void SingleUnexplainedGotoCountsAsNotNextInLayout() {
        // Arrange
        CfgBlock entry = CfgTestHelpers.CreateBlock(1, 0x00);
        CfgBlock target = CfgTestHelpers.CreateBlock(2, 0x10);
        NaturalLoopForest loops = CfgTestHelpers.BuildLoopForest([entry, target], new() { [entry] = [target] });

        // Act
        GotoCensus census = Classify(Empty(), Boundary(entry, new GotoNode(target.Entry)), loops,
            new HashSet<ICfgNode> { entry.Entry }, [Lowered(entry), Lowered(target)]);

        // Assert
        census.Total.Should().Be(1);
        census.Count(GotoKeepReason.NotNextInLayout).Should().Be(1);
    }
}
