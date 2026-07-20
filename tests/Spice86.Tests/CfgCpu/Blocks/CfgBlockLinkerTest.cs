namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;
using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using static CfgTestHelpers;

using Xunit;

/// <summary>
/// Tests block construction and maintenance behavior in <see cref="NodeLinker"/>.
/// </summary>
public class CfgBlockLinkerTest : IDisposable {
    private const ushort BaseSegment = 0x1000;
    private const ushort TargetSegment = 0x4000;

    private static readonly SequentialIdAllocator _blockAllocator = new();
    private readonly CfgNodeExecutionCompiler _compiler;
    private readonly CfgNodeExecutionCompilerMonitor _monitor;
    private readonly NodeLinker _linker;

    public CfgBlockLinkerTest() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        _monitor = new CfgNodeExecutionCompilerMonitor(loggerService);
        _compiler = new CfgNodeExecutionCompiler(_monitor, loggerService, JitMode.InterpretedOnly);
        _linker = new NodeLinker(new InstructionReplacerRegistry(), _compiler, new SequentialIdAllocator());
    }

    public void Dispose() {
        _compiler.Dispose();
        _monitor.Dispose();
    }

    [Fact]
    public void IsLive_IgnoresRepeatedSetLiveCalls() {
        CfgInstruction[] instructions = BuildBlock(4);
        CfgBlock block = GetContainingBlock(instructions[0]);

        instructions[1].SetLive(true);
        block.IsLive.Should().BeTrue();

        instructions[2].SetLive(false);
        block.IsLive.Should().BeFalse();
        instructions[2].SetLive(false);
        block.IsLive.Should().BeFalse();

        instructions[2].SetLive(true);
        block.IsLive.Should().BeTrue();
    }

    [Fact]
    public void IsLive_BecomesTrueOnlyAfterEveryInstructionIsLiveAgain() {
        CfgInstruction[] instructions = BuildBlock(5);
        CfgBlock block = GetContainingBlock(instructions[0]);

        for (int i = 0; i < instructions.Length; i++) {
            instructions[i].SetLive(false);
            block.IsLive.Should().BeFalse();
        }

        for (int i = 0; i < instructions.Length - 1; i++) {
            instructions[i].SetLive(true);
            block.IsLive.Should().BeFalse($"instruction {i + 1} to {instructions.Length - 1} still non-live");
        }
        instructions[^1].SetLive(true);
        block.IsLive.Should().BeTrue();
    }

    [Fact]
    public void IsDiscoveryComplete_NeverTransitionsBackToFalse() {
        CfgInstruction a = CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction b = CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        CfgInstruction c = CreateInstruction(new SegmentedAddress(TargetSegment, 0));

        _linker.Link(InstructionSuccessorType.Normal, a, b);
        CfgBlock ab = GetContainingBlock(a);
        ab.IsDiscoveryComplete.Should().BeFalse("block is still being built");

        _linker.Link(InstructionSuccessorType.Normal, b, c);
        ab.IsDiscoveryComplete.Should().BeTrue("boundary path finalizes the block");

        CfgInstruction d = CreateInstruction(new SegmentedAddress(TargetSegment, 1));
        _linker.Link(InstructionSuccessorType.Normal, c, d);
        ab.IsDiscoveryComplete.Should().BeTrue("must never flip back to false");
    }

    [Theory]
    [InlineData(InstructionSuccessorType.Normal)]
    [InlineData(InstructionSuccessorType.CallToReturn)]
    [InlineData(InstructionSuccessorType.CallToMisalignedReturn)]
    [InlineData(InstructionSuccessorType.CpuFault)]
    public void ColdPath_Continuation_AppendsBothToSameBlock(InstructionSuccessorType linkType) {
        using LinkerHarness harness = new();
        CfgInstruction current = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction next = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 1));

        harness.Linker.Link(linkType, current, next);

        current.ContainingBlock.Should().NotBeNull();
        next.ContainingBlock.Should().BeSameAs(current.ContainingBlock,
            "continuation: both must be in the same block");
        GetContainingBlock(current).Instructions.Should().HaveCount(2);
    }

    [Fact]
    public void ColdPath_ContinuationToTerminator_KeepsTerminatorInPredecessorBlock() {
        using LinkerHarness harness = new();
        CfgInstruction current = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction next = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        next.MarkAsBlockTerminator();

        ICfgNode returned = harness.Linker.Link(InstructionSuccessorType.Normal, current, next);

        returned.Should().BeSameAs(next);
        CfgBlock currentBlock = GetContainingBlock(current);
        next.ContainingBlock.Should().BeSameAs(currentBlock,
            "a returned terminator is not a graph boundary by itself");
        currentBlock.Instructions.Should().Equal([current, next]);
        currentBlock.Entry.Should().BeSameAs(current);
        currentBlock.Terminator.Should().BeSameAs(next);
        currentBlock.IsDiscoveryComplete.Should().BeTrue();
    }

    [Theory]
    [InlineData(InstructionSuccessorType.Normal)]
    [InlineData(InstructionSuccessorType.CallToReturn)]
    [InlineData(InstructionSuccessorType.CallToMisalignedReturn)]
    [InlineData(InstructionSuccessorType.CpuFault)]
    public void ColdPath_MemoryGap_PlacesInSeparateBlocks(InstructionSuccessorType linkType) {
        using LinkerHarness harness = new();
        CfgInstruction current = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction next = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 10));

        harness.Linker.Link(linkType, current, next);

        current.ContainingBlock.Should().NotBeNull();
        next.ContainingBlock.Should().NotBeNull();
        next.ContainingBlock.Should().NotBeSameAs(current.ContainingBlock,
            "memory gap: must be in separate blocks");
    }

    [Theory]
    [InlineData(InstructionSuccessorType.Normal)]
    [InlineData(InstructionSuccessorType.CallToReturn)]
    [InlineData(InstructionSuccessorType.CallToMisalignedReturn)]
    [InlineData(InstructionSuccessorType.CpuFault)]
    public void ColdPath_NextIsStarter_PlacesInSeparateBlocks(InstructionSuccessorType linkType) {
        using LinkerHarness harness = new();
        CfgInstruction current = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction next = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        next.MarkAsBlockStarter();

        harness.Linker.Link(linkType, current, next);

        next.ContainingBlock.Should().NotBeSameAs(current.ContainingBlock,
            "next is a starter: must be in a separate block");
    }

    [Theory]
    [InlineData(InstructionSuccessorType.Normal)]
    [InlineData(InstructionSuccessorType.CallToReturn)]
    [InlineData(InstructionSuccessorType.CallToMisalignedReturn)]
    [InlineData(InstructionSuccessorType.CpuFault)]
    public void ColdPath_CurrentIsTerminator_PlacesInSeparateBlocks(InstructionSuccessorType linkType) {
        using LinkerHarness harness = new();
        CfgInstruction current = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        current.MarkAsBlockTerminator();
        CfgInstruction next = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 1));

        harness.Linker.Link(linkType, current, next);

        next.ContainingBlock.Should().NotBeSameAs(current.ContainingBlock,
            "current is a terminator: must be in a separate block");
        GetContainingBlock(current).IsDiscoveryComplete.Should().BeTrue();
    }

    [Fact]
    public void Split_InteriorEdge_ProducesCorrectPrefixAndSuffix() {
        CfgInstruction[] instructions = BuildLinkedBlock(5);
        CfgBlock alpha = GetContainingBlock(instructions[0]);
        alpha.Instructions.Should().HaveCount(5);

        CfgInstruction target = CreateInstruction(new SegmentedAddress(TargetSegment, 0));
        _linker.Link(InstructionSuccessorType.Normal, instructions[2], target);

        CfgBlock prefix = GetContainingBlock(instructions[0]);
        prefix.Instructions.Should().HaveCount(3);
        prefix.Entry.Should().BeSameAs(instructions[0]);
        prefix.Terminator.Should().BeSameAs(instructions[2]);
        prefix.IsDiscoveryComplete.Should().BeTrue();

        CfgBlock suffix = GetContainingBlock(instructions[3]);
        suffix.Should().NotBeSameAs(prefix);
        suffix.Instructions.Should().HaveCount(2);
        suffix.Entry.Should().BeSameAs(instructions[3]);
        suffix.Terminator.Should().BeSameAs(instructions[4]);
        suffix.IsDiscoveryComplete.Should().BeTrue();

        for (int i = 0; i <= 2; i++) {
            instructions[i].ContainingBlock.Should().BeSameAs(prefix);
        }
        for (int i = 3; i < 5; i++) {
            instructions[i].ContainingBlock.Should().BeSameAs(suffix);
        }

        instructions[2].Successors.Should().Contain(target);
        instructions[2].Successors.Should().Contain(instructions[3]);
    }

    [Fact]
    public void Split_PreservesNonLiveCounterInBothHalves() {
        CfgInstruction[] instructions = BuildLinkedBlock(6);
        CfgBlock alpha = GetContainingBlock(instructions[0]);

        instructions[1].SetLive(false);
        instructions[4].SetLive(false);
        alpha.IsLive.Should().BeFalse();

        CfgInstruction target = CreateInstruction(new SegmentedAddress(TargetSegment, 0));
        _linker.Link(InstructionSuccessorType.Normal, instructions[2], target);

        CfgBlock prefix = GetContainingBlock(instructions[0]);
        CfgBlock suffix = GetContainingBlock(instructions[3]);

        prefix.IsLive.Should().BeFalse("prefix contains non-live instruction[1]");
        suffix.IsLive.Should().BeFalse("suffix contains non-live instruction[4]");

        instructions[1].SetLive(true);
        prefix.IsLive.Should().BeTrue();

        instructions[4].SetLive(true);
        suffix.IsLive.Should().BeTrue();
    }

    [Fact]
    public void ReplaceInstruction_InPlace_TransfersBackPointerAndAdjustsCounter() {
        CfgInstruction[] instructions = BuildBlock(4);
        CfgBlock block = GetContainingBlock(instructions[0]);

        CfgInstruction oldInstr = instructions[2];
        CfgInstruction newInstr = CreateInstruction(new SegmentedAddress(0x2000, 2));

        _linker.ReplaceInstruction(oldInstr, newInstr);

        block.Instructions.Should().HaveCount(4);
        block.Instructions[2].Should().BeSameAs(newInstr);
        block.Instructions[0].Should().BeSameAs(instructions[0]);
        block.Instructions[1].Should().BeSameAs(instructions[1]);
        block.Instructions[3].Should().BeSameAs(instructions[3]);
        newInstr.ContainingBlock.Should().BeSameAs(block);
        oldInstr.ContainingBlock.Should().BeNull();
    }

    [Fact]
    public void ReplaceInstruction_NonLiveOldWithLiveNew_FixesCounter() {
        CfgInstruction[] instructions = BuildBlock(3);
        CfgBlock block = GetContainingBlock(instructions[0]);

        instructions[1].SetLive(false);
        block.IsLive.Should().BeFalse();

        CfgInstruction newInstr = CreateInstruction(new SegmentedAddress(0x2000, 1));
        _linker.ReplaceInstruction(instructions[1], newInstr);

        block.IsLive.Should().BeTrue();
    }

    [Fact]
    public void ReplaceInstruction_LiveOldWithNonLiveNew_FixesCounter() {
        CfgInstruction[] instructions = BuildBlock(3);
        CfgBlock block = GetContainingBlock(instructions[0]);
        block.IsLive.Should().BeTrue();

        CfgInstruction newInstr = CreateInstruction(new SegmentedAddress(0x2000, 1));
        newInstr.SetLive(false);
        _linker.ReplaceInstruction(instructions[1], newInstr);

        block.IsLive.Should().BeFalse();
    }

    [Fact]
    public void IntraBlockBackEdge_SplitsBlockAtTarget() {
        // Arrange: three consecutive instructions A, B, C where C is a block terminator.
        CfgInstruction a = CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction b = CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        CfgInstruction c = CreateInstruction(new SegmentedAddress(BaseSegment, 2));
        c.MarkAsBlockTerminator();

        // Act: build block [A, B, C] via sequential linking, then create back-edge C->B.
        _linker.Link(InstructionSuccessorType.Normal, a, b);
        _linker.Link(InstructionSuccessorType.Normal, b, c);

        CfgBlock blockBeforeBackEdge = GetContainingBlock(a);
        blockBeforeBackEdge.Instructions.Should().HaveCount(3, "block should be [A, B, C] before back-edge");
        blockBeforeBackEdge.IsDiscoveryComplete.Should().BeTrue("C is a terminator appended by continuation");

        _linker.Link(InstructionSuccessorType.Normal, c, b);

        // Assert: the back-edge C->B should split the block so that B becomes an entry point.
        CfgBlock aBlock = GetContainingBlock(a);
        CfgBlock bBlock = GetContainingBlock(b);
        CfgBlock cBlock = GetContainingBlock(c);
        bBlock.Should().NotBeSameAs(aBlock,
            "B is a back-edge target and must be split into its own block");
        bBlock.Entry.Should().BeSameAs(b, "B must be the entry of the new block");
        bBlock.Instructions.Should().Equal([b, c], "target suffix block should be [B, C]");
        bBlock.Terminator.Should().BeSameAs(c);
        bBlock.IsDiscoveryComplete.Should().BeTrue("suffix block inherits completed discovery");
        cBlock.Should().BeSameAs(bBlock);
        aBlock.Instructions.Should().HaveCount(1, "prefix block should be [A]");
        aBlock.Entry.Should().BeSameAs(a);
        aBlock.IsDiscoveryComplete.Should().BeTrue("prefix block is completed on split");
    }

    [Fact]
    public void IntraBlockBackEdge_ToEntry_DoesNotSplit() {
        // Arrange: back-edge to the block entry (A) from the terminator (B) should NOT
        // split A's block: A is already the entry.
        CfgInstruction a = CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction b = CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        b.MarkAsBlockTerminator();

        // Act
        _linker.Link(InstructionSuccessorType.Normal, a, b);
        CfgBlock blockBefore = GetContainingBlock(a);
        blockBefore.Instructions.Should().Equal([a, b]);

        _linker.Link(InstructionSuccessorType.Normal, b, a);

        // Assert: block stays [A, B] because A is already the entry.
        CfgBlock blockAfter = GetContainingBlock(a);
        blockAfter.Should().BeSameAs(blockBefore);
        blockAfter.Instructions.Should().Equal([a, b]);
        blockAfter.Entry.Should().BeSameAs(a);
    }

    [Fact]
    public void IntraBlockNeighborEdge_ToCompletedInteriorNode_RemainsIdempotent() {
        // Arrange: completed block [A, B, C] where C is a terminator. Re-linking A->B
        // returns B, but B is still only an internal neighbor in the graph.
        CfgInstruction a = CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction b = CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        CfgInstruction c = CreateInstruction(new SegmentedAddress(BaseSegment, 2));
        c.MarkAsBlockTerminator();

        _linker.Link(InstructionSuccessorType.Normal, a, b);
        _linker.Link(InstructionSuccessorType.Normal, b, c);
        CfgBlock blockBefore = GetContainingBlock(a);
        blockBefore.Instructions.Should().HaveCount(3);

        // Act: re-link the existing neighbor edge A->B via a different link type.
        _linker.Link(InstructionSuccessorType.CpuFault, a, b);

        // Assert: the block shape is unchanged; returned-node normalization is not a graph rule.
        CfgBlock blockAfter = GetContainingBlock(a);
        CfgBlock bBlock = GetContainingBlock(b);
        blockAfter.Should().BeSameAs(blockBefore);
        blockAfter.Instructions.Should().Equal([a, b, c]);
        blockAfter.Entry.Should().BeSameAs(a);
        bBlock.Should().BeSameAs(blockAfter);
    }

    [Fact]
    public void IntraBlockEdge_InteriorToEntry_SplitsAfterCurrent() {
        // Arrange: block [A, B, C, D] where D is a terminator — a CPU fault edge from B to A.
        // B is interior and not A's neighbor in the block for this edge direction.
        // A is already the entry so no split there, but B must become a terminator
        // by splitting after it.
        CfgInstruction a = CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction b = CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        CfgInstruction c = CreateInstruction(new SegmentedAddress(BaseSegment, 2));
        CfgInstruction d = CreateInstruction(new SegmentedAddress(BaseSegment, 3));
        d.MarkAsBlockTerminator();

        _linker.Link(InstructionSuccessorType.Normal, a, b);
        _linker.Link(InstructionSuccessorType.Normal, b, c);
        _linker.Link(InstructionSuccessorType.Normal, c, d);
        GetContainingBlock(a).Instructions.Should().HaveCount(4, "block should be [A, B, C, D]");

        // Act: CPU fault edge from B (interior, index 1) to A (entry, index 0).
        _linker.Link(InstructionSuccessorType.CpuFault, b, a);

        // Assert: B is now a terminator; block split into [A, B] and [C, D].
        CfgBlock abBlock = GetContainingBlock(a);
        CfgBlock cdBlock = GetContainingBlock(c);
        abBlock.Should().NotBeSameAs(cdBlock);
        abBlock.Instructions.Should().HaveCount(2, "prefix block should be [A, B]");
        abBlock.Entry.Should().BeSameAs(a);
        abBlock.Terminator.Should().BeSameAs(b);
        abBlock.IsDiscoveryComplete.Should().BeTrue("prefix block is completed on split");
        cdBlock.Instructions.Should().Equal([c, d], "suffix block should be [C, D]");
        cdBlock.Entry.Should().BeSameAs(c);
        cdBlock.Terminator.Should().BeSameAs(d);
        cdBlock.IsDiscoveryComplete.Should().BeTrue("suffix block is completed on split");
        GetContainingBlock(d).Should().BeSameAs(cdBlock);
    }

    [Fact]
    public void IntraBlockForwardSkip_SplitsAtTarget() {
        // Arrange: block [A, B, C, D, E] where E is a terminator — a forward edge from A to D skipping B, C.
        CfgInstruction a = CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction b = CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        CfgInstruction c = CreateInstruction(new SegmentedAddress(BaseSegment, 2));
        CfgInstruction d = CreateInstruction(new SegmentedAddress(BaseSegment, 3));
        CfgInstruction e = CreateInstruction(new SegmentedAddress(BaseSegment, 4));
        e.MarkAsBlockTerminator();

        _linker.Link(InstructionSuccessorType.Normal, a, b);
        _linker.Link(InstructionSuccessorType.Normal, b, c);
        _linker.Link(InstructionSuccessorType.Normal, c, d);
        _linker.Link(InstructionSuccessorType.Normal, d, e);
        GetContainingBlock(a).Instructions.Should().HaveCount(5, "block should be [A, B, C, D, E]");

        // Act: forward skip edge from A (index 0) to D (index 3).
        _linker.Link(InstructionSuccessorType.CpuFault, a, d);

        // Assert: split at D (new entry) and after A (new terminator).
        // Result: [A], [B, C], [D, E].
        CfgBlock aBlock = GetContainingBlock(a);
        CfgBlock bcBlock = GetContainingBlock(b);
        CfgBlock deBlock = GetContainingBlock(d);
        aBlock.Instructions.Should().HaveCount(1, "A is now a single-instruction block");
        aBlock.Entry.Should().BeSameAs(a);
        bcBlock.Should().NotBeSameAs(aBlock);
        bcBlock.Instructions.Should().HaveCount(2, "middle block should be [B, C]");
        bcBlock.Entry.Should().BeSameAs(b);
        deBlock.Should().NotBeSameAs(bcBlock);
        deBlock.Instructions.Should().Equal([d, e], "target block should be [D, E]");
        deBlock.Entry.Should().BeSameAs(d);
        aBlock.IsDiscoveryComplete.Should().BeTrue();
        bcBlock.IsDiscoveryComplete.Should().BeTrue();
        deBlock.IsDiscoveryComplete.Should().BeTrue();
        GetContainingBlock(e).Should().BeSameAs(deBlock);
    }

    [Fact]
    public void IntraBlockForwardFault_SplitsSourceMiddleAndTarget() {
        // Arrange: block [A, B, C, D, E] where E is a terminator.
        CfgInstruction a = CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction b = CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        CfgInstruction c = CreateInstruction(new SegmentedAddress(BaseSegment, 2));
        CfgInstruction d = CreateInstruction(new SegmentedAddress(BaseSegment, 3));
        CfgInstruction e = CreateInstruction(new SegmentedAddress(BaseSegment, 4));
        e.MarkAsBlockTerminator();

        _linker.Link(InstructionSuccessorType.Normal, a, b);
        _linker.Link(InstructionSuccessorType.Normal, b, c);
        _linker.Link(InstructionSuccessorType.Normal, c, d);
        _linker.Link(InstructionSuccessorType.Normal, d, e);

        // Act: CPU fault edge from B to D skips C and makes B a block terminator.
        _linker.Link(InstructionSuccessorType.CpuFault, b, d);

        // Assert: both endpoints are normalized, producing [A, B], [C], and [D, E].
        CfgBlock abBlock = GetContainingBlock(a);
        CfgBlock cBlock = GetContainingBlock(c);
        CfgBlock deBlock = GetContainingBlock(d);
        abBlock.Instructions.Should().Equal([a, b]);
        abBlock.Terminator.Should().BeSameAs(b);
        cBlock.Should().NotBeSameAs(abBlock);
        cBlock.Instructions.Should().Equal([c]);
        cBlock.Entry.Should().BeSameAs(c);
        deBlock.Should().NotBeSameAs(cBlock);
        deBlock.Instructions.Should().Equal([d, e]);
        deBlock.Entry.Should().BeSameAs(d);
        deBlock.Terminator.Should().BeSameAs(e);
        abBlock.IsDiscoveryComplete.Should().BeTrue();
        cBlock.IsDiscoveryComplete.Should().BeTrue();
        deBlock.IsDiscoveryComplete.Should().BeTrue();
    }

    [Fact]
    public void Link_SameTriple_IsIdempotent_Continuation() {
        using LinkerHarness harness = new();
        CfgInstruction current = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction next = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 1));

        ICfgNode first = harness.Linker.Link(InstructionSuccessorType.Normal, current, next);
        int successorsAfterFirst = current.Successors.Count;
        int predecessorsAfterFirst = next.Predecessors.Count;
        CfgBlock? blockAfterFirst = current.ContainingBlock;

        ICfgNode second = harness.Linker.Link(InstructionSuccessorType.Normal, current, next);

        second.Should().BeSameAs(first, "Link must return the same node");
        current.Successors.Should().HaveCount(successorsAfterFirst, "no duplicate edges");
        next.Predecessors.Should().HaveCount(predecessorsAfterFirst, "no duplicate edges");
        current.ContainingBlock.Should().BeSameAs(blockAfterFirst, "no new block created");
    }

    [Fact]
    public void Link_SameTriple_IsIdempotent_Boundary() {
        using LinkerHarness harness = new();
        CfgInstruction current = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        current.MarkAsBlockTerminator();
        CfgInstruction next = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 1));

        ICfgNode first = harness.Linker.Link(InstructionSuccessorType.Normal, current, next);
        int currentBlockSize = GetContainingBlock(current).Instructions.Count;
        int nextBlockSize = GetContainingBlock(next).Instructions.Count;

        ICfgNode second = harness.Linker.Link(InstructionSuccessorType.Normal, current, next);

        second.Should().BeSameAs(first);
        GetContainingBlock(current).Instructions.Should().HaveCount(currentBlockSize);
        GetContainingBlock(next).Instructions.Should().HaveCount(nextBlockSize);
    }

    /// <summary>
    /// Builds a block of N instructions with ContainingBlock back-pointers set (no linker).
    /// </summary>
    private static CfgInstruction[] BuildBlock(int size) {
        CfgInstruction[] instructions = new CfgInstruction[size];
        for (int i = 0; i < size; i++) {
            instructions[i] = CreateInstruction(new SegmentedAddress(BaseSegment, (ushort)i));
        }
        CfgBlock block = new(_blockAllocator.AllocateId(), instructions[0]);
        for (int i = 1; i < size; i++) {
            block.Append(instructions[i]);
        }
        foreach (CfgInstruction instr in instructions) {
            instr.ContainingBlock = block;
        }
        return instructions;
    }

    [Fact]
    public void ColdPath_PopfBothStarterAndTerminator_FormsSingleInstructionBlock() {
        using LinkerHarness harness = new();
        CfgInstruction before = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 0));
        CfgInstruction popf = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 1));
        popf.MarkAsBlockStarter();
        popf.MarkAsBlockTerminator();
        CfgInstruction after = harness.CreateInstruction(new SegmentedAddress(BaseSegment, 2));

        harness.Linker.Link(InstructionSuccessorType.Normal, before, popf);
        harness.Linker.Link(InstructionSuccessorType.Normal, popf, after);

        CfgBlock popfBlock = GetContainingBlock(popf);
        popfBlock.Instructions.Should().HaveCount(1, "POPF is both starter and terminator so it must be alone in its block");
        popfBlock.Entry.Should().BeSameAs(popf);
        popfBlock.Terminator.Should().BeSameAs(popf);
        popfBlock.IsDiscoveryComplete.Should().BeTrue();
        popfBlock.Should().NotBeSameAs(GetContainingBlock(before), "POPF starter flag must split it from its predecessor");
        popfBlock.Should().NotBeSameAs(GetContainingBlock(after), "POPF terminator flag must split it from its successor");
    }

    /// <summary>
    /// Builds a block of N instructions by linking them through the NodeLinker (cold path).
    /// </summary>
    private CfgInstruction[] BuildLinkedBlock(int size) {
        CfgInstruction[] instructions = new CfgInstruction[size];
        for (int i = 0; i < size; i++) {
            instructions[i] = CreateInstruction(new SegmentedAddress(BaseSegment, (ushort)i));
        }
        for (int i = 0; i < size - 1; i++) {
            _linker.Link(InstructionSuccessorType.Normal, instructions[i], instructions[i + 1]);
        }
        return instructions;
    }

}

