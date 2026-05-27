namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.Fixtures;

using Xunit;

/// <summary>
/// Tests for <see cref="CfgBlockGraphExporter"/> validating BFS traversal, edge discovery,
/// deduplication, truncation, and execution-context metadata.
/// </summary>
public class CfgBlockGraphExporterTest : IDisposable {
    private const ushort Seg = 0x1000;
    private readonly LinkerHarness _harness;
    private readonly CfgBlockGraphExporter _exporter;

    public CfgBlockGraphExporterTest() {
        _harness = new LinkerHarness();
        _exporter = new CfgBlockGraphExporter();
    }

    public void Dispose() {
        _harness.Dispose();
    }

    /// <summary>
    /// Builds a three-block linear chain: blkA (jmp) → blkB (jmp) → blkC.
    /// Each block is a single JMP instruction (which is a block terminator by Kind).
    /// </summary>
    private (CfgInstruction instrA, CfgInstruction instrB, CfgInstruction instrC) BuildThreeBlockChain() {
        CfgInstruction instrA = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0000), 0xEB, 1, InstructionKind.Jump);
        CfgInstruction instrB = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0001), 0xEB, 1, InstructionKind.Jump);
        CfgInstruction instrC = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0002), 0x90, 1, InstructionKind.None);

        _harness.Linker.Link(InstructionSuccessorType.Normal, instrA, instrB);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrB, instrC);

        return (instrA, instrB, instrC);
    }

    /// <summary>
    /// Exports a linear chain of three blocks from the first block. All three blocks and
    /// two edges should be present in the exported graph.
    /// </summary>
    [Fact]
    public void ExportFromNode_LinearChain_ExportsAllBlocksAndEdges() {
        // Arrange
        (CfgInstruction instrA, CfgInstruction instrB, CfgInstruction instrC) = BuildThreeBlockChain();

        CfgBlock blockA = CfgTestHelpers.GetContainingBlock(instrA);
        CfgBlock blockB = CfgTestHelpers.GetContainingBlock(instrB);
        CfgBlock blockC = CfgTestHelpers.GetContainingBlock(instrC);

        // Act
        CfgBlockGraph graph = _exporter.ExportFromNode(instrA, null, null);

        // Assert
        graph.Blocks.Should().HaveCount(3);
        graph.Blocks.Select(b => b.Block.Id).Should().Contain(blockA.Id);
        graph.Blocks.Select(b => b.Block.Id).Should().Contain(blockB.Id);
        graph.Blocks.Select(b => b.Block.Id).Should().Contain(blockC.Id);

        graph.Edges.Should().HaveCount(2);
        graph.Edges.Should().Contain(e => e.From.Id == blockA.Id && e.To.Id == blockB.Id);
        graph.Edges.Should().Contain(e => e.From.Id == blockB.Id && e.To.Id == blockC.Id);
        graph.Truncated.Should().BeFalse();
    }

    /// <summary>
    /// Predecessor-only reachable blocks are included in the exported graph.
    /// </summary>
    [Fact]
    public void ExportFromNode_IncludesPredecessorOnlyBlocks() {
        // Arrange: A → B ← C (start from B; A and C are reachable only via predecessors)
        CfgInstruction instrA = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0000), 0xEB, 1, InstructionKind.Jump);
        CfgInstruction instrB = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0001), 0x90, 1, InstructionKind.None);
        CfgInstruction instrC = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0010), 0xEB, 1, InstructionKind.Jump);

        _harness.Linker.Link(InstructionSuccessorType.Normal, instrA, instrB);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrC, instrB);

        CfgBlock blockA = CfgTestHelpers.GetContainingBlock(instrA);
        CfgBlock blockB = CfgTestHelpers.GetContainingBlock(instrB);
        CfgBlock blockC = CfgTestHelpers.GetContainingBlock(instrC);

        // Act — start from B; A and C are reachable only through predecessors
        CfgBlockGraph graph = _exporter.ExportFromNode(instrB, null, null);

        // Assert
        graph.Blocks.Select(b => b.Block.Id).Should().Contain(blockA.Id);
        graph.Blocks.Select(b => b.Block.Id).Should().Contain(blockB.Id);
        graph.Blocks.Select(b => b.Block.Id).Should().Contain(blockC.Id);
    }

    /// <summary>
    /// Edges are deduplicated by (from.Id, to.Id).
    /// </summary>
    [Fact]
    public void ExportFromNode_DeduplicatesEdges() {
        // Arrange: A → B
        CfgInstruction instrA = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0000), 0xEB, 1, InstructionKind.Jump);
        CfgInstruction instrB = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0001), 0x90, 1, InstructionKind.None);

        _harness.Linker.Link(InstructionSuccessorType.Normal, instrA, instrB);

        CfgBlock blockA = CfgTestHelpers.GetContainingBlock(instrA);
        CfgBlock blockB = CfgTestHelpers.GetContainingBlock(instrB);

        // Act
        CfgBlockGraph graph = _exporter.ExportFromNode(instrA, null, null);

        // Assert — only one A→B edge even though both successor and predecessor traversal see it
        int edgeCount = graph.Edges.Count(e => e.From.Id == blockA.Id && e.To.Id == blockB.Id);
        edgeCount.Should().Be(1);
    }

    /// <summary>
    /// Truncation is reported when nodeLimit is reached.
    /// </summary>
    [Fact]
    public void ExportFromNode_TruncatesWhenNodeLimitReached() {
        // Arrange: A → B → C
        (CfgInstruction instrA, _, _) = BuildThreeBlockChain();

        // Act — limit to 2 blocks
        CfgBlockGraph graph = _exporter.ExportFromNode(instrA, null, 2);

        // Assert
        graph.Blocks.Should().HaveCount(2);
        graph.Truncated.Should().BeTrue();
    }

    /// <summary>
    /// When the graph is truncated, edges must only reference blocks that are present
    /// in the exported block list. No edge endpoint may point to an omitted block.
    /// </summary>
    [Fact]
    public void ExportFromNode_TruncatedGraph_EdgesAreClosedToIncludedBlocks() {
        // Arrange: A → B → C; limit to 1 block so only A is included
        (CfgInstruction instrA, _, _) = BuildThreeBlockChain();

        // Act
        CfgBlockGraph graph = _exporter.ExportFromNode(instrA, null, 1);

        // Assert
        graph.Blocks.Should().HaveCount(1);
        graph.Truncated.Should().BeTrue();

        HashSet<int> includedIds = graph.Blocks.Select(n => n.Block.Id).ToHashSet();
        foreach (CfgBlockGraphEdge edge in graph.Edges) {
            includedIds.Should().Contain(edge.From.Id,
                "edge source must be an included block");
            includedIds.Should().Contain(edge.To.Id,
                "edge target must be an included block");
        }
    }

    /// <summary>
    /// Raw block IDs are preserved in nodes and edges.
    /// </summary>
    [Fact]
    public void ExportFromNode_PreservesRawBlockIds() {
        // Arrange
        CfgInstruction instrA = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0000), 0xEB, 1, InstructionKind.Jump);
        CfgInstruction instrB = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0001), 0x90, 1, InstructionKind.None);

        _harness.Linker.Link(InstructionSuccessorType.Normal, instrA, instrB);

        CfgBlock blockA = CfgTestHelpers.GetContainingBlock(instrA);
        CfgBlock blockB = CfgTestHelpers.GetContainingBlock(instrB);

        // Act
        CfgBlockGraph graph = _exporter.ExportFromNode(instrA, null, null);

        // Assert — IDs match the actual CfgBlock.Id values
        graph.Blocks.Should().Contain(b => b.Block.Id == blockA.Id);
        graph.Blocks.Should().Contain(b => b.Block.Id == blockB.Id);
        graph.Edges.Should().Contain(e => e.From.Id == blockA.Id && e.To.Id == blockB.Id);
    }

    /// <summary>
    /// A seed node with no containing block returns an empty graph.
    /// </summary>
    [Fact]
    public void ExportFromNode_NoContainingBlock_ReturnsEmptyGraph() {
        // Arrange — a raw instruction with no links has no containing block
        CfgInstruction instrA = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0000));

        // Act
        CfgBlockGraph graph = _exporter.ExportFromNode(instrA, null, null);

        // Assert
        graph.Blocks.Should().BeEmpty();
        graph.Edges.Should().BeEmpty();
        graph.Truncated.Should().BeFalse();
    }

    /// <summary>
    /// The executing block is marked in the exported graph.
    /// </summary>
    [Fact]
    public void ExportFromNode_MarksExecutingBlock() {
        // Arrange
        CfgInstruction instrA = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0000), 0xEB, 1, InstructionKind.Jump);
        CfgInstruction instrB = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0001), 0x90, 1, InstructionKind.None);

        _harness.Linker.Link(InstructionSuccessorType.Normal, instrA, instrB);

        CfgBlock blockA = CfgTestHelpers.GetContainingBlock(instrA);

        // Act — instrA is the executing node, so its block should be marked
        CfgBlockGraph graph = _exporter.ExportFromNode(instrA, instrA, null);

        // Assert
        CfgBlockGraphNode executingGraphNode = graph.Blocks.First(b => b.Block.Id == blockA.Id);
        executingGraphNode.IsExecutingBlock.Should().BeTrue();

        CfgBlockGraphNode otherGraphNode = graph.Blocks.First(b => b.Block.Id != blockA.Id);
        otherGraphNode.IsExecutingBlock.Should().BeFalse();
    }

    /// <summary>
    /// ExportFromExecutionContext includes the last executed block and context metadata.
    /// </summary>
    [Fact]
    public void ExportFromExecutionContext_IncludesContextMetadata() {
        // Arrange — create a minimal ExecutionContextManager
        using ExecutionContextManagerFactory factory = new(new FunctionCatalogue());
        ExecutionContextManager contextManager = factory.ContextManager;

        // Build a simple chain
        CfgInstruction instrA = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0000), 0xEB, 1, InstructionKind.Jump);
        CfgInstruction instrB = _harness.CreateInstruction(new SegmentedAddress(Seg, 0x0001), 0x90, 1, InstructionKind.None);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrA, instrB);

        CfgBlock blockA = CfgTestHelpers.GetContainingBlock(instrA);
        contextManager.CurrentExecutionContext.LastExecuted = instrA;
        contextManager.ExecutingNode = instrA;

        // Act
        CfgExecutionContextGraph result = _exporter.ExportFromExecutionContext(contextManager, null);

        // Assert
        result.CurrentContextDepth.Should().Be(0);
        result.LastExecuted.Should().Be(instrA);
        result.LastExecutedBlock.Should().Be(blockA);
        result.Graph.Should().NotBeNull();
        result.Graph.Blocks.Should().Contain(b => b.Block.Id == blockA.Id);
    }
}
