namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.Fixtures;

using Xunit;

/// <summary>
/// Tests conservative CFG function partition recovery over synthetic block graphs.
/// </summary>
public sealed class CfgFunctionPartitionerTest : IDisposable {
    private const ushort Seg = 0x1000;

    private readonly LinkerHarness _harness = new();
    private readonly CfgBlockGraphExporter _graphExporter = new();
    private readonly CfgFunctionPartitioner _partitioner = new();
    private readonly FunctionCatalogue _functionCatalogue = new();
    private readonly ExecutionContextManagerFactory _contextManagerFactory;
    private readonly ExecutionContextManager _contextManager;

    public CfgFunctionPartitionerTest() {
        _contextManagerFactory = new ExecutionContextManagerFactory(_functionCatalogue);
        _contextManager = _contextManagerFactory.ContextManager;
    }

    public void Dispose() {
        _harness.Dispose();
        _contextManagerFactory.Dispose();
    }

    [Fact]
    public void Partition_LinearBlocks_ProducesOneNormalMethodPartition() {
        // Arrange
        CfgInstruction entry = CreateInstruction(0x0000, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction middle = CreateInstruction(0x0001, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction end = CreateInstruction(0x0002, 0x90, 1, InstructionKind.None);
        _harness.Linker.Link(InstructionSuccessorType.Normal, entry, middle);
        _harness.Linker.Link(InstructionSuccessorType.Normal, middle, end);
        RegisterEntry(entry);

        // Act
        CfgPartitionedProgram program = PartitionFrom(entry);

        // Assert
        program.Partitions.Should().ContainSingle();
        CfgCodePartition partition = program.Partitions[0];
        partition.Kind.Should().Be(CfgCodePartitionKind.Observed);
        partition.Blocks.Should().BeEquivalentTo([
            CfgTestHelpers.GetContainingBlock(entry),
            CfgTestHelpers.GetContainingBlock(middle),
            CfgTestHelpers.GetContainingBlock(end)
        ]);
        program.Transfers.Should().BeEmpty();
    }

    [Fact]
    public void Partition_NonEmptyGraphWithoutRoot_ThrowsInvalidOperationException() {
        // Arrange
        CfgInstruction entry = CreateInstruction(0x0000, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction end = CreateInstruction(0x0001, 0x90, 1, InstructionKind.None);
        _harness.Linker.Link(InstructionSuccessorType.Normal, entry, end);

        // Act
        Action act = () => PartitionFrom(entry);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*root*");
    }

    [Fact]
    public void Partition_OwnerlessBlock_ThrowsInvalidOperationException() {
        // Arrange
        CfgInstruction entry = CreateInstruction(0x0000, 0x90, 1, InstructionKind.None);
        CfgInstruction ownerless = CreateInstruction(0x0100, 0x90, 1, InstructionKind.None);
        CfgBlock entryBlock = CreateBlock(100, entry);
        CfgBlock ownerlessBlock = CreateBlock(101, ownerless);
        RegisterEntry(entry);
        CfgBlockGraph graph = new() {
            Blocks = [
                new CfgBlockGraphNode { Block = entryBlock, IsExecutingBlock = false },
                new CfgBlockGraphNode { Block = ownerlessBlock, IsExecutingBlock = false }
            ],
            Edges = [],
            Truncated = false
        };

        // Act
        Action act = () => _partitioner.Partition(graph, _contextManager, _functionCatalogue);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ownerless*");
    }

    [Fact]
    public void Partition_OwnershipPreservingCycleEnteredOnlyByReturn_AssignsEveryBlockToAPartition() {
        // Arrange
        // The bug is a graph-shape problem, not an SMC problem: an ownership-preserving cycle whose
        // only entry from outside is a suppressed return target. This test builds that shape directly.
        //   A --callContinuation--> B --jump--> D --callContinuation--> E --jump--> A
        // The cycle contains no execution-context entry, call target, or CPU fault target, so no
        // root sits inside it. Its only entry from outside is a return edge into B. Because B is
        // also an aligned call-continuation target of the in-cycle call A, RetTarget root promotion
        // for B is suppressed, leaving the whole cycle ownerless. The partitioner must still attribute
        // every block to a partition instead of throwing. (In the wild this shape showed up as a region
        // of stale self-modified blocks re-entered only through returns, but staleness is incidental.)
        CfgInstruction start = CreateInstruction(0x2000, 0xE8, 3, InstructionKind.Call);
        CfgInstruction externalCallee = CreateInstruction(0x0100, 0xC3, 1, InstructionKind.Return);
        CfgInstruction cycleCallA = CreateInstruction(0x0000, 0xE8, 3, InstructionKind.Call);
        CfgInstruction inCallee1 = CreateInstruction(0x0200, 0xC3, 1, InstructionKind.Return);
        CfgInstruction cycleEntryB = CreateInstruction(0x0003, 0xEB, 2, InstructionKind.Jump);
        CfgInstruction cycleCallD = CreateInstruction(0x0010, 0xE8, 3, InstructionKind.Call);
        CfgInstruction inCallee2 = CreateInstruction(0x0300, 0xC3, 1, InstructionKind.Return);
        CfgInstruction cycleBackE = CreateInstruction(0x0013, 0xEB, 2, InstructionKind.Jump);

        // start calls externalCallee, whose ret lands on B (misaligned vs start, so a plain RetTarget into B).
        _harness.Linker.Link(InstructionSuccessorType.Normal, start, externalCallee);
        externalCallee.CurrentCorrespondingCallInstruction = start;
        _harness.Linker.Link(InstructionSuccessorType.Normal, externalCallee, cycleEntryB);

        // A calls inCallee1, whose ret lands on B (aligned: A.next == B), making A -> B a call-continuation.
        _harness.Linker.Link(InstructionSuccessorType.Normal, cycleCallA, inCallee1);
        inCallee1.CurrentCorrespondingCallInstruction = cycleCallA;
        _harness.Linker.Link(InstructionSuccessorType.Normal, inCallee1, cycleEntryB);

        // B -> D, D calls inCallee2 (aligned continuation D -> E), E -> A closes the cycle.
        _harness.Linker.Link(InstructionSuccessorType.Normal, cycleEntryB, cycleCallD);
        _harness.Linker.Link(InstructionSuccessorType.Normal, cycleCallD, inCallee2);
        inCallee2.CurrentCorrespondingCallInstruction = cycleCallD;
        _harness.Linker.Link(InstructionSuccessorType.Normal, inCallee2, cycleBackE);
        _harness.Linker.Link(InstructionSuccessorType.Normal, cycleBackE, cycleCallA);
        RegisterEntry(start);

        // Act
        CfgPartitionedProgram program = PartitionFrom(start);

        // Assert
        CfgBlock blockA = CfgTestHelpers.GetContainingBlock(cycleCallA);
        CfgBlock blockB = CfgTestHelpers.GetContainingBlock(cycleEntryB);
        CfgBlock blockD = CfgTestHelpers.GetContainingBlock(cycleCallD);
        CfgBlock blockE = CfgTestHelpers.GetContainingBlock(cycleBackE);
        foreach (CfgBlock cycleBlock in new[] { blockA, blockB, blockD, blockE }) {
            program.Partitions.Should().ContainSingle(partition => partition.Blocks.Contains(cycleBlock),
                "every block in an ownerless ownership-preserving cycle must be attributed to exactly one partition");
        }
    }

    [Fact]
    public void Partition_NormalCallWithContinuation_SeparatesCallerAndCallee() {
        // Arrange
        CfgInstruction call = CreateInstruction(0x0000, 0xE8, 3, InstructionKind.Call);
        CfgInstruction calleeReturn = CreateInstruction(0x0100, 0xC3, 1, InstructionKind.Return);
        CfgInstruction continuation = CreateInstruction(0x0003, 0x90, 1, InstructionKind.None);
        _harness.Linker.Link(InstructionSuccessorType.Normal, call, calleeReturn);
        calleeReturn.CurrentCorrespondingCallInstruction = call;
        _harness.Linker.Link(InstructionSuccessorType.Normal, calleeReturn, continuation);
        RegisterEntry(call);

        // Act
        CfgPartitionedProgram program = PartitionFrom(call);

        // Assert
        CfgCodePartition callerOverlay = FindPartitionContaining(program, call);
        CfgCodePartition calleeOverlay = FindPartitionContaining(program, calleeReturn);
        CfgCodePartitionTransfer callTransfer = program.Transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == callerOverlay
            && transfer.ToPartition == calleeOverlay
            && transfer.Kind == CfgCodePartitionTransferKind.CallOut).Subject;
        callTransfer.FromNode.Should().BeSameAs(call);
        callTransfer.TargetNode.Should().BeSameAs(calleeReturn);
        callTransfer.CallContinuationNode.Should().BeSameAs(continuation);
        program.Partitions.Should().HaveCount(2);
        callerOverlay.Blocks.Should().Contain(CfgTestHelpers.GetContainingBlock(continuation));
        callerOverlay.Blocks.Should().NotContain(CfgTestHelpers.GetContainingBlock(calleeReturn));
        program.Transfers.Should().Contain(transfer => transfer.Kind == CfgCodePartitionTransferKind.AlignedReturn
            && transfer.FromPartition == calleeOverlay
            && transfer.ToPartition == callerOverlay);
    }

    [Fact]
    public void Partition_TwoRootsJumpToSameTail_ExtractsSyntheticSharedPartition() {
        // Arrange
        CfgInstruction firstEntry = CreateInstruction(0x0000, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction secondEntry = CreateInstruction(0x0010, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction sharedTail = CreateInstruction(0x0020, 0x90, 1, InstructionKind.None);
        _harness.Linker.Link(InstructionSuccessorType.Normal, firstEntry, sharedTail);
        _harness.Linker.Link(InstructionSuccessorType.Normal, secondEntry, sharedTail);
        RegisterEntry(firstEntry);
        RegisterEntry(secondEntry);

        // Act
        CfgPartitionedProgram program = PartitionFrom(firstEntry);

        // Assert
        CfgCodePartition syntheticOverlay = program.Partitions.Should().ContainSingle(partition => partition.Kind == CfgCodePartitionKind.Synthetic).Subject;
        syntheticOverlay.Entries.Should().ContainSingle();
        syntheticOverlay.Blocks.Should().Contain(CfgTestHelpers.GetContainingBlock(sharedTail));
        program.Partitions.Where(partition => partition.Kind == CfgCodePartitionKind.Observed).Should().HaveCount(2);
        program.Transfers.Should().Contain(transfer => transfer.ToPartition == syntheticOverlay && transfer.Kind == CfgCodePartitionTransferKind.CrossPartitionFlow);
    }

    [Fact]
    public void BuildBlockAssignment_SyntheticPartitionTakesPrecedenceOverObservedPartition() {
        // Arrange
        CfgInstruction sharedInstruction = CreateInstruction(0x0010, 0x90, 1, InstructionKind.None);
        CfgBlock sharedBlock = CreateBlock(100, sharedInstruction);
        CfgPartitionDraft observed = new(1, CfgCodePartitionKind.Observed, sharedBlock, "observed");
        CfgPartitionDraft synthetic = new(2, CfgCodePartitionKind.Synthetic, sharedBlock, "synthetic");

        // Act
        Dictionary<CfgBlock, CfgPartitionDraft> partitionByBlock = CfgPartitionAssignment.BuildBlockAssignment([observed, synthetic]);

        // Assert
        partitionByBlock[sharedBlock].Should().BeSameAs(synthetic);
    }

    [Fact]
    public void Partition_SharedComponentWithMultipleEntries_ProjectsMultipleEntries() {
        // Arrange
        CfgInstruction firstEntry = CreateInstruction(0x0000, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction secondEntry = CreateInstruction(0x0010, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction firstShared = CreateInstruction(0x0020, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction secondShared = CreateInstruction(0x0030, 0xEB, 1, InstructionKind.Jump);
        _harness.Linker.Link(InstructionSuccessorType.Normal, firstEntry, firstShared);
        _harness.Linker.Link(InstructionSuccessorType.Normal, secondEntry, secondShared);
        _harness.Linker.Link(InstructionSuccessorType.Normal, firstShared, secondShared);
        _harness.Linker.Link(InstructionSuccessorType.Normal, secondShared, firstShared);
        RegisterEntry(firstEntry);
        RegisterEntry(secondEntry);

        // Act
        CfgPartitionedProgram program = PartitionFrom(firstEntry);

        // Assert
        CfgCodePartition syntheticOverlay = program.Partitions.Should().ContainSingle(partition => partition.Kind == CfgCodePartitionKind.Synthetic).Subject;
        syntheticOverlay.Entries.Should().HaveCount(2);
        syntheticOverlay.Entries.Select(entry => entry.Node).Should().Contain([firstShared, secondShared]);
        syntheticOverlay.Entries.Should().OnlyContain(entry => entry.Kind == CfgCodePartitionEntryKind.SharedEntry);
    }

    [Fact]
    public void Partition_MultiEntrySharedComponentWithDominatedRegions_SplitsSingleEntrySyntheticPartitions() {
        // Arrange
        CfgInstruction firstEntry = CreateInstruction(0x0000, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction secondEntry = CreateInstruction(0x0010, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction firstSharedEntry = CreateInstruction(0x0020, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction firstDominated = CreateInstruction(0x0030, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction secondSharedEntry = CreateInstruction(0x0040, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction secondDominated = CreateInstruction(0x0050, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction sharedJoin = CreateInstruction(0x0060, 0x90, 1, InstructionKind.None);
        AllowAdditionalSuccessor(firstEntry, secondSharedEntry);
        AllowAdditionalSuccessor(secondEntry, firstSharedEntry);
        _harness.Linker.Link(InstructionSuccessorType.Normal, firstEntry, firstSharedEntry);
        _harness.Linker.Link(InstructionSuccessorType.Normal, firstEntry, secondSharedEntry);
        _harness.Linker.Link(InstructionSuccessorType.Normal, secondEntry, secondSharedEntry);
        _harness.Linker.Link(InstructionSuccessorType.Normal, secondEntry, firstSharedEntry);
        _harness.Linker.Link(InstructionSuccessorType.Normal, firstSharedEntry, firstDominated);
        _harness.Linker.Link(InstructionSuccessorType.Normal, firstDominated, sharedJoin);
        _harness.Linker.Link(InstructionSuccessorType.Normal, secondSharedEntry, secondDominated);
        _harness.Linker.Link(InstructionSuccessorType.Normal, secondDominated, sharedJoin);
        RegisterEntry(firstEntry);
        RegisterEntry(secondEntry);

        // Act
        CfgPartitionedProgram program = PartitionFrom(firstEntry);

        // Assert
        CfgBlock firstSharedEntryBlock = CfgTestHelpers.GetContainingBlock(firstSharedEntry);
        CfgBlock firstDominatedBlock = CfgTestHelpers.GetContainingBlock(firstDominated);
        CfgBlock secondSharedEntryBlock = CfgTestHelpers.GetContainingBlock(secondSharedEntry);
        CfgBlock secondDominatedBlock = CfgTestHelpers.GetContainingBlock(secondDominated);
        CfgBlock sharedJoinBlock = CfgTestHelpers.GetContainingBlock(sharedJoin);
        CfgCodePartition firstSynthetic = FindPartitionContaining(program, firstSharedEntry);
        CfgCodePartition secondSynthetic = FindPartitionContaining(program, secondSharedEntry);
        CfgCodePartition residualSynthetic = FindPartitionContaining(program, sharedJoin);
        firstSynthetic.Kind.Should().Be(CfgCodePartitionKind.Synthetic);
        firstSynthetic.Entries.Should().ContainSingle(entry => entry.Block == firstSharedEntryBlock);
        firstSynthetic.Blocks.Should().BeEquivalentTo([firstSharedEntryBlock, firstDominatedBlock]);
        secondSynthetic.Kind.Should().Be(CfgCodePartitionKind.Synthetic);
        secondSynthetic.Entries.Should().ContainSingle(entry => entry.Block == secondSharedEntryBlock);
        secondSynthetic.Blocks.Should().BeEquivalentTo([secondSharedEntryBlock, secondDominatedBlock]);
        residualSynthetic.Kind.Should().Be(CfgCodePartitionKind.Synthetic);
        residualSynthetic.Entries.Should().ContainSingle(entry => entry.Block == sharedJoinBlock);
        residualSynthetic.Blocks.Should().BeEquivalentTo([sharedJoinBlock]);
        program.Partitions.Where(partition => partition.Kind == CfgCodePartitionKind.Synthetic).Should().OnlyContain(partition => partition.Entries.Count == 1);
    }

    [Fact]
    public void PartitionEdgeRecord_HasIncomingFromOutside_IgnoresNonOwnershipEdges() {
        // Arrange
        CfgInstruction firstEntry = CreateInstruction(0x0000, 0xEB, 1, InstructionKind.Jump);
        CfgInstruction callOnlyTarget = CreateInstruction(0x0010, 0x90, 1, InstructionKind.None);
        CfgBlock firstEntryBlock = CreateBlock(100, firstEntry);
        CfgBlock callOnlyTargetBlock = CreateBlock(101, callOnlyTarget);
        HashSet<CfgBlock> blocks = [callOnlyTargetBlock];
        List<CfgPartitionEdgeRecord> edgeRecords = [
            new(firstEntryBlock, callOnlyTargetBlock, firstEntry, callOnlyTarget,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Call)
        ];
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);

        // Act
        bool hasIncomingFromOutside = edgeIndex.HasIncomingFromOutside(callOnlyTargetBlock, blocks);

        // Assert
        hasIncomingFromOutside.Should().BeFalse();
    }

    [Fact]
    public void Partition_CpuFaultEdge_DoesNotOwnHandlerFromFaultingPartition() {
        // Arrange
        CfgInstruction faultingInstruction = CreateInstruction(0x0000, 0xF7, 1, InstructionKind.None);
        CfgInstruction handler = CreateInstruction(0x0100, 0x90, 1, InstructionKind.None);
        _harness.Linker.Link(InstructionSuccessorType.CpuFault, faultingInstruction, handler);
        RegisterEntry(faultingInstruction);

        // Act
        CfgPartitionedProgram program = PartitionFrom(faultingInstruction);

        // Assert
        CfgCodePartition faultingOverlay = FindPartitionContaining(program, faultingInstruction);
        CfgCodePartition handlerOverlay = FindPartitionContaining(program, handler);
        program.Transfers.Should().ContainSingle(transfer =>
            transfer.Kind == CfgCodePartitionTransferKind.CpuFault
            && transfer.FromPartition == faultingOverlay
            && transfer.ToPartition == handlerOverlay
            && transfer.CallContinuationNode == null);
        program.Partitions.Should().HaveCount(2);
        faultingOverlay.Blocks.Should().NotContain(CfgTestHelpers.GetContainingBlock(handler));
    }

    [Fact]
    public void Partition_MisalignedReturn_ClassifiesDynamicReturn() {
        // Arrange
        CfgInstruction call = CreateInstruction(0x0000, 0xE8, 3, InstructionKind.Call);
        CfgInstruction calleeReturn = CreateInstruction(0x0100, 0xC3, 1, InstructionKind.Return);
        // dynamicTarget is at 0x0005, not at the aligned continuation 0x0003, so it is misaligned.
        // A misaligned continuation is not ownership-preserving: dynamicTarget gets its own partition.
        CfgInstruction dynamicTarget = CreateInstruction(0x0005, 0x90, 1, InstructionKind.None);
        _harness.Linker.Link(InstructionSuccessorType.Normal, call, calleeReturn);
        calleeReturn.CurrentCorrespondingCallInstruction = call;
        _harness.Linker.Link(InstructionSuccessorType.Normal, calleeReturn, dynamicTarget);
        RegisterEntry(call);

        // Act
        CfgPartitionedProgram program = PartitionFrom(call);

        // Assert
        CfgCodePartition calleeOverlay = FindPartitionContaining(program, calleeReturn);
        CfgCodePartition dynamicTargetOverlay = FindPartitionContaining(program, dynamicTarget);
        program.Transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == calleeOverlay
            && transfer.ToPartition == dynamicTargetOverlay
            && transfer.Kind == CfgCodePartitionTransferKind.DynamicReturn
            && transfer.CallContinuationNode == null);
        // The misaligned target is NOT in the caller's partition; it has its own partition.
        dynamicTargetOverlay.Should().NotBeSameAs(FindPartitionContaining(program, call));
    }

    [Fact]
    public void PartitionEdgeAnnotator_SplitContinuationReturn_ClassifiesAlignedReturn() {
        // Arrange
        CfgInstruction callerCall = CreateInstruction(0x0000, 0xE8, 3, InstructionKind.Call);
        CfgInstruction calleeEntry = CreateInstruction(0x0100, 0xE8, 3, InstructionKind.Call);
        CfgInstruction returningRet = CreateInstruction(0x0103, 0xC3, 1, InstructionKind.Return);
        CfgInstruction callerContinuation = CreateInstruction(0x0003, 0x90, 1, InstructionKind.None);
        CfgBlock callerBlock = CreateBlock(100, callerCall);
        CfgBlock calleeEntryBlock = CreateBlock(101, calleeEntry);
        CfgBlock returningBlock = CreateBlock(102, returningRet);
        CfgBlock callerContinuationBlock = CreateBlock(103, callerContinuation);
        CfgCodePartition callerPartition = CreatePartition(1, callerBlock, callerContinuationBlock);
        CfgCodePartition calleeEntryPartition = CreatePartition(2, calleeEntryBlock);
        CfgCodePartition returningPartition = CreatePartition(3, returningBlock);
        List<CfgPartitionEdgeRecord> edgeRecords = [
            new(callerBlock, calleeEntryBlock, callerCall, calleeEntry,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Call),
            new(callerBlock, callerContinuationBlock, callerCall, callerContinuation,
                InstructionSuccessorType.CallToReturn, ClassifiedEdgeKind.CallContinuation),
            new(calleeEntryBlock, returningBlock, calleeEntry, returningRet,
                InstructionSuccessorType.CallToReturn, ClassifiedEdgeKind.CallContinuation),
            new(returningBlock, callerContinuationBlock, returningRet, callerContinuation,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.RetTarget)
        ];
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock = new() {
            [callerBlock] = callerPartition,
            [calleeEntryBlock] = calleeEntryPartition,
            [returningBlock] = returningPartition,
            [callerContinuationBlock] = callerPartition
        };
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);
        CfgPartitionEdgeAnnotator annotator = new();

        // Act
        List<CfgCodePartitionTransfer> transfers = annotator.CollectTransfers(edgeIndex, partitionByBlock);

        // Assert
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.ToPartition == callerPartition
            && Equals(transfer.FromNode, returningRet)
            && Equals(transfer.TargetNode, callerContinuation)
            && transfer.Kind == CfgCodePartitionTransferKind.AlignedReturn);
        transfers.Should().NotContain(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.Kind == CfgCodePartitionTransferKind.DynamicReturn);
    }

    [Fact]
    public void PartitionEdgeAnnotator_TailJumpIntoSplitReturn_ClassifiesAlignedReturn() {
        // Arrange
        CfgInstruction callerCall = CreateInstruction(0x0000, 0xE8, 3, InstructionKind.Call);
        CfgInstruction jumpEntry = CreateInstruction(0x0100, 0xEB, 2, InstructionKind.Jump);
        CfgInstruction returningRet = CreateInstruction(0x0102, 0xC3, 1, InstructionKind.Return);
        CfgInstruction callerContinuation = CreateInstruction(0x0003, 0x90, 1, InstructionKind.None);
        CfgBlock callerBlock = CreateBlock(100, callerCall);
        CfgBlock jumpEntryBlock = CreateBlock(101, jumpEntry);
        CfgBlock returningBlock = CreateBlock(102, returningRet);
        CfgBlock callerContinuationBlock = CreateBlock(103, callerContinuation);
        CfgCodePartition callerPartition = CreatePartition(1, callerBlock, callerContinuationBlock);
        CfgCodePartition jumpEntryPartition = CreatePartition(2, jumpEntryBlock);
        CfgCodePartition returningPartition = CreatePartition(3, returningBlock);
        List<CfgPartitionEdgeRecord> edgeRecords = [
            new(callerBlock, jumpEntryBlock, callerCall, jumpEntry,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Call),
            new(callerBlock, callerContinuationBlock, callerCall, callerContinuation,
                InstructionSuccessorType.CallToReturn, ClassifiedEdgeKind.CallContinuation),
            new(jumpEntryBlock, returningBlock, jumpEntry, returningRet,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Jump),
            new(returningBlock, callerContinuationBlock, returningRet, callerContinuation,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.RetTarget)
        ];
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock = new() {
            [callerBlock] = callerPartition,
            [jumpEntryBlock] = jumpEntryPartition,
            [returningBlock] = returningPartition,
            [callerContinuationBlock] = callerPartition
        };
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);
        CfgPartitionEdgeAnnotator annotator = new();

        // Act
        List<CfgCodePartitionTransfer> transfers = annotator.CollectTransfers(edgeIndex, partitionByBlock);

        // Assert
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.ToPartition == callerPartition
            && Equals(transfer.FromNode, returningRet)
            && Equals(transfer.TargetNode, callerContinuation)
            && transfer.Kind == CfgCodePartitionTransferKind.AlignedReturn);
        transfers.Should().NotContain(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.Kind == CfgCodePartitionTransferKind.DynamicReturn);
    }

    [Fact]
    public void PartitionEdgeAnnotator_CallContinuationWithoutCallEdge_ClassifiesAlignedReturn() {
        // Arrange
        CfgInstruction indirectCall = CreateInstruction(0x0000, 0xFF, 2, InstructionKind.Call);
        CfgInstruction nestedCall = CreateInstruction(0x0100, 0xE8, 3, InstructionKind.Call);
        CfgInstruction nestedContinuationCall = CreateInstruction(0x0103, 0xE8, 3, InstructionKind.Call);
        CfgInstruction returningRet = CreateInstruction(0x0106, 0xC3, 1, InstructionKind.Return);
        CfgInstruction indirectContinuation = CreateInstruction(0x0002, 0x90, 1, InstructionKind.None);
        CfgBlock callerBlock = CreateBlock(100, indirectCall);
        CfgBlock nestedCallBlock = CreateBlock(101, nestedCall);
        CfgBlock nestedContinuationBlock = CreateBlock(102, nestedContinuationCall);
        CfgBlock returningBlock = CreateBlock(103, returningRet);
        CfgBlock indirectContinuationBlock = CreateBlock(104, indirectContinuation);
        CfgCodePartition callerPartition = CreatePartition(1, callerBlock, nestedCallBlock, indirectContinuationBlock);
        CfgCodePartition nestedContinuationPartition = CreatePartition(2, nestedContinuationBlock);
        CfgCodePartition returningPartition = CreatePartition(3, returningBlock);
        List<CfgPartitionEdgeRecord> edgeRecords = [
            new(callerBlock, indirectContinuationBlock, indirectCall, indirectContinuation,
                InstructionSuccessorType.CallToReturn, ClassifiedEdgeKind.CallContinuation),
            new(nestedCallBlock, nestedContinuationBlock, nestedCall, nestedContinuationCall,
                InstructionSuccessorType.CallToReturn, ClassifiedEdgeKind.CallContinuation),
            new(nestedContinuationBlock, returningBlock, nestedContinuationCall, returningRet,
                InstructionSuccessorType.CallToReturn, ClassifiedEdgeKind.CallContinuation),
            new(returningBlock, indirectContinuationBlock, returningRet, indirectContinuation,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.RetTarget)
        ];
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock = new() {
            [callerBlock] = callerPartition,
            [nestedCallBlock] = callerPartition,
            [nestedContinuationBlock] = nestedContinuationPartition,
            [returningBlock] = returningPartition,
            [indirectContinuationBlock] = callerPartition
        };
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);
        CfgPartitionEdgeAnnotator annotator = new();

        // Act
        List<CfgCodePartitionTransfer> transfers = annotator.CollectTransfers(edgeIndex, partitionByBlock);

        // Assert
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.ToPartition == callerPartition
            && Equals(transfer.FromNode, returningRet)
            && Equals(transfer.TargetNode, indirectContinuation)
            && transfer.Kind == CfgCodePartitionTransferKind.AlignedReturn);
        transfers.Should().NotContain(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.Kind == CfgCodePartitionTransferKind.DynamicReturn);
    }

    [Fact]
    public void PartitionEdgeAnnotator_MissingCallContinuationEdge_ClassifiesDynamicReturn() {
        // Arrange
        CfgInstruction indirectCall = CreateInstruction(0x0000, 0xFF, 2, InstructionKind.Call);
        CfgInstruction indirectCallTarget = CreateInstruction(0x0100, 0xE8, 3, InstructionKind.Call);
        CfgInstruction nestedCall = CreateInstruction(0x0103, 0xE8, 3, InstructionKind.Call);
        CfgInstruction returningRet = CreateInstruction(0x0106, 0xC3, 1, InstructionKind.Return);
        CfgInstruction indirectContinuation = CreateInstruction(0x0002, 0x90, 1, InstructionKind.None);
        CfgBlock callerBlock = CreateBlock(100, indirectCall);
        CfgBlock indirectCallTargetBlock = CreateBlock(101, indirectCallTarget);
        CfgBlock nestedCallBlock = CreateBlock(102, nestedCall);
        CfgBlock returningBlock = CreateBlock(103, returningRet);
        CfgBlock indirectContinuationBlock = CreateBlock(104, indirectContinuation);
        CfgCodePartition callerPartition = CreatePartition(1, callerBlock, indirectCallTargetBlock, indirectContinuationBlock);
        CfgCodePartition nestedCallPartition = CreatePartition(2, nestedCallBlock);
        CfgCodePartition returningPartition = CreatePartition(3, returningBlock);
        List<CfgPartitionEdgeRecord> edgeRecords = [
            new(callerBlock, indirectCallTargetBlock, indirectCall, indirectCallTarget,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Call),
            new(indirectCallTargetBlock, nestedCallBlock, indirectCallTarget, nestedCall,
                InstructionSuccessorType.CallToReturn, ClassifiedEdgeKind.CallContinuation),
            new(nestedCallBlock, returningBlock, nestedCall, returningRet,
                InstructionSuccessorType.CallToReturn, ClassifiedEdgeKind.CallContinuation),
            new(returningBlock, indirectContinuationBlock, returningRet, indirectContinuation,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.RetTarget)
        ];
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock = new() {
            [callerBlock] = callerPartition,
            [indirectCallTargetBlock] = callerPartition,
            [nestedCallBlock] = nestedCallPartition,
            [returningBlock] = returningPartition,
            [indirectContinuationBlock] = callerPartition
        };
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);
        CfgPartitionEdgeAnnotator annotator = new();

        // Act
        List<CfgCodePartitionTransfer> transfers = annotator.CollectTransfers(edgeIndex, partitionByBlock);

        // Assert
        // Without a CallContinuation edge from the indirect call's block to the continuation,
        // there is no graph evidence that this return is aligned — it classifies as DynamicReturn.
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.ToPartition == callerPartition
            && Equals(transfer.FromNode, returningRet)
            && Equals(transfer.TargetNode, indirectContinuation)
            && transfer.Kind == CfgCodePartitionTransferKind.DynamicReturn);
        transfers.Should().NotContain(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.Kind == CfgCodePartitionTransferKind.AlignedReturn);
    }

    [Fact]
    public void PartitionEdgeAnnotator_MixedActivationCycle_ClassifiesCycleJumpsAsCyclic() {
        // Arrange
        CfgInstruction firstJump = CreateInstruction(0x0000, 0xEB, 2, InstructionKind.Jump);
        CfgInstruction secondEntry = CreateInstruction(0x0100, 0xE8, 3, InstructionKind.Call);
        CfgInstruction thirdEntry = CreateInstruction(0x0200, 0xEB, 2, InstructionKind.Jump);
        CfgBlock firstBlock = CreateBlock(100, firstJump);
        CfgBlock secondBlock = CreateBlock(101, secondEntry);
        CfgBlock thirdBlock = CreateBlock(102, thirdEntry);
        CfgCodePartition firstPartition = CreatePartition(1, firstBlock);
        CfgCodePartition secondPartition = CreatePartition(2, secondBlock);
        CfgCodePartition thirdPartition = CreatePartition(3, thirdBlock);
        List<CfgPartitionEdgeRecord> edgeRecords = [
            new(firstBlock, secondBlock, firstJump, secondEntry,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Jump),
            new(secondBlock, thirdBlock, secondEntry, thirdEntry,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Call),
            new(thirdBlock, firstBlock, thirdEntry, firstJump,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Jump)
        ];
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock = new() {
            [firstBlock] = firstPartition,
            [secondBlock] = secondPartition,
            [thirdBlock] = thirdPartition
        };
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);
        CfgPartitionEdgeAnnotator annotator = new();
        CfgPartitionCycleClassifier cycleClassifier = new();

        // Act
        List<CfgCodePartitionTransfer> transfers = cycleClassifier.Refine(
            [firstPartition, secondPartition, thirdPartition],
            annotator.CollectTransfers(edgeIndex, partitionByBlock));

        // Assert
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == firstPartition
            && transfer.ToPartition == secondPartition
            && transfer.Kind == CfgCodePartitionTransferKind.CyclicCrossPartitionFlow);
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == thirdPartition
            && transfer.ToPartition == firstPartition
            && transfer.Kind == CfgCodePartitionTransferKind.CyclicCrossPartitionFlow);
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == secondPartition
            && transfer.ToPartition == thirdPartition
            && transfer.Kind == CfgCodePartitionTransferKind.CallOut);
    }

    [Fact]
    public void PartitionEdgeAnnotator_DynamicReturnActivationCycle_ClassifiesJumpAsCyclic() {
        // Arrange
        CfgInstruction jump = CreateInstruction(0x0000, 0xEB, 2, InstructionKind.Jump);
        CfgInstruction returningRet = CreateInstruction(0x0100, 0xC3, 1, InstructionKind.Return);
        CfgBlock jumpBlock = CreateBlock(100, jump);
        CfgBlock returningBlock = CreateBlock(101, returningRet);
        CfgCodePartition jumpPartition = CreatePartition(1, jumpBlock);
        CfgCodePartition returningPartition = CreatePartition(2, returningBlock);
        List<CfgPartitionEdgeRecord> edgeRecords = [
            new(jumpBlock, returningBlock, jump, returningRet,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Jump),
            new(returningBlock, jumpBlock, returningRet, jump,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.RetTarget)
        ];
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock = new() {
            [jumpBlock] = jumpPartition,
            [returningBlock] = returningPartition
        };
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);
        CfgPartitionEdgeAnnotator annotator = new();
        CfgPartitionCycleClassifier cycleClassifier = new();

        // Act
        List<CfgCodePartitionTransfer> transfers = cycleClassifier.Refine(
            [jumpPartition, returningPartition],
            annotator.CollectTransfers(edgeIndex, partitionByBlock));

        // Assert
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == jumpPartition
            && transfer.ToPartition == returningPartition
            && transfer.Kind == CfgCodePartitionTransferKind.CyclicCrossPartitionFlow);
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.ToPartition == jumpPartition
            && transfer.Kind == CfgCodePartitionTransferKind.DynamicReturn);
    }

    [Fact]
    public void PartitionEdgeAnnotator_CpuFaultActivationCycle_ClassifiesJumpAsCyclic() {
        // Arrange
        CfgInstruction jump = CreateInstruction(0x0000, 0xEB, 2, InstructionKind.Jump);
        CfgInstruction fault = CreateInstruction(0x0100, 0xF4, 1, InstructionKind.None);
        CfgBlock jumpBlock = CreateBlock(100, jump);
        CfgBlock faultBlock = CreateBlock(101, fault);
        CfgCodePartition jumpPartition = CreatePartition(1, jumpBlock);
        CfgCodePartition faultPartition = CreatePartition(2, faultBlock);
        List<CfgPartitionEdgeRecord> edgeRecords = [
            new(jumpBlock, faultBlock, jump, fault,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Jump),
            new(faultBlock, jumpBlock, fault, jump,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.CpuFault)
        ];
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock = new() {
            [jumpBlock] = jumpPartition,
            [faultBlock] = faultPartition
        };
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);
        CfgPartitionEdgeAnnotator annotator = new();
        CfgPartitionCycleClassifier cycleClassifier = new();

        // Act
        List<CfgCodePartitionTransfer> transfers = cycleClassifier.Refine(
            [jumpPartition, faultPartition],
            annotator.CollectTransfers(edgeIndex, partitionByBlock));

        // Assert
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == jumpPartition
            && transfer.ToPartition == faultPartition
            && transfer.Kind == CfgCodePartitionTransferKind.CyclicCrossPartitionFlow);
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == faultPartition
            && transfer.ToPartition == jumpPartition
            && transfer.Kind == CfgCodePartitionTransferKind.CpuFault);
    }

    [Fact]
    public void PartitionEdgeAnnotator_AlignedReturnDoesNotCloseActivationCycle_KeepsJumpNonCyclic() {
        // Arrange
        CfgInstruction jump = CreateInstruction(0x0000, 0xEB, 2, InstructionKind.Jump);
        CfgInstruction call = CreateInstruction(0x0002, 0xE8, 3, InstructionKind.Call);
        CfgInstruction continuation = CreateInstruction(0x0005, 0x90, 1, InstructionKind.None);
        CfgInstruction returningRet = CreateInstruction(0x0100, 0xC3, 1, InstructionKind.Return);
        CfgBlock jumpBlock = CreateBlock(100, jump);
        CfgBlock callBlock = CreateBlock(101, call);
        CfgBlock continuationBlock = CreateBlock(102, continuation);
        CfgBlock returningBlock = CreateBlock(103, returningRet);
        CfgCodePartition callerPartition = CreatePartition(1, jumpBlock, callBlock, continuationBlock);
        CfgCodePartition returningPartition = CreatePartition(2, returningBlock);
        List<CfgPartitionEdgeRecord> edgeRecords = [
            new(jumpBlock, returningBlock, jump, returningRet,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Jump),
            new(callBlock, returningBlock, call, returningRet,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.Call),
            new(callBlock, continuationBlock, call, continuation,
                InstructionSuccessorType.CallToReturn, ClassifiedEdgeKind.CallContinuation),
            new(returningBlock, continuationBlock, returningRet, continuation,
                InstructionSuccessorType.Normal, ClassifiedEdgeKind.RetTarget)
        ];
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock = new() {
            [jumpBlock] = callerPartition,
            [callBlock] = callerPartition,
            [continuationBlock] = callerPartition,
            [returningBlock] = returningPartition
        };
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);
        CfgPartitionEdgeAnnotator annotator = new();
        CfgPartitionCycleClassifier cycleClassifier = new();

        // Act
        List<CfgCodePartitionTransfer> transfers = cycleClassifier.Refine(
            [callerPartition, returningPartition],
            annotator.CollectTransfers(edgeIndex, partitionByBlock));

        // Assert
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == callerPartition
            && transfer.ToPartition == returningPartition
            && Equals(transfer.FromNode, jump)
            && transfer.Kind == CfgCodePartitionTransferKind.CrossPartitionFlow);
        transfers.Should().ContainSingle(transfer =>
            transfer.FromPartition == returningPartition
            && transfer.ToPartition == callerPartition
            && transfer.Kind == CfgCodePartitionTransferKind.AlignedReturn);
        transfers.Should().NotContain(transfer =>
            transfer.Kind == CfgCodePartitionTransferKind.CyclicCrossPartitionFlow);
    }

    private CfgPartitionedProgram PartitionFrom(CfgInstruction start) {
        CfgBlockGraph graph = _graphExporter.ExportFromNode(start, null, null);
        return _partitioner.Partition(graph, _contextManager, _functionCatalogue);
    }

    private void RegisterEntry(CfgInstruction instruction) {
        if (!_contextManager.ExecutionContextEntryPoints.TryGetValue(instruction.Address, out ISet<CfgInstruction>? instructions)) {
            instructions = new HashSet<CfgInstruction>();
            _contextManager.ExecutionContextEntryPoints.Add(instruction.Address, instructions);
        }
        instructions.Add(instruction);
    }

    private CfgInstruction CreateInstruction(ushort offset, byte opcode, int length, InstructionKind kind) =>
        _harness.CreateInstruction(new SegmentedAddress(Seg, offset), opcode, length, kind);

    private static void AllowAdditionalSuccessor(CfgInstruction instruction, CfgInstruction target) =>
        instruction.IncreaseMaxSuccessorsCount(target.Address);

    private static CfgCodePartition FindPartitionContaining(CfgPartitionedProgram program, CfgInstruction instruction) {
        CfgBlock block = CfgTestHelpers.GetContainingBlock(instruction);
        return program.Partitions.Single(partition => partition.Blocks.Contains(block));
    }

    private static CfgBlock CreateBlock(int id, CfgInstruction instruction) {
        CfgBlock block = new(id, instruction);
        instruction.ContainingBlock = block;
        return block;
    }

    private static CfgCodePartition CreatePartition(int id, params CfgBlock[] blocks) => new() {
        Id = id,
        Kind = CfgCodePartitionKind.Observed,
        Name = $"partition_{id}",
        Blocks = blocks,
        Entries = [new CfgCodePartitionEntry {
            Node = blocks[0].Entry,
            Kind = CfgCodePartitionEntryKind.FunctionEntry
        }]
    };

}
