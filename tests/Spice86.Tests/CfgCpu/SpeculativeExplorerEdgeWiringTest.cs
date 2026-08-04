namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Linq;

using Xunit;

/// <summary>
/// Tests for Speculative CFG Explorer edge wiring.
/// These validate that the explorer correctly creates successor/predecessor edges between speculative
/// nodes and their predecessors during exploration.
/// </summary>
public sealed class SpeculativeExplorerEdgeWiringTest : SpeculativeTestBase {
    private readonly SpeculativeExplorer _explorer;

    public SpeculativeExplorerEdgeWiringTest() {
        _explorer = new SpeculativeExplorer(Parser, NodeIndex, NodeLinker);
    }

    /// <summary>
    /// Explorer wires successor edge from seed to speculative node.
    /// Arrange: JNZ at address A with static successor at address B.
    /// Act: ExploreFrom(A).
    /// Assert: A speculative node exists at B, A.Successors contains the node at B,
    /// and nodeAtB.Predecessors contains A.
    /// </summary>
    [Fact]
    public void ExplorerWiresSuccessorEdgeFromSeedToSpeculativeNode() {
        // Arrange: JNZ at 0:0x100 with rel8=+4 -> target is 0:0x106 (after 2-byte JNZ instruction at 0x100, +4)
        // JNZ is 2 bytes, so next instruction is at 0x102, and target = 0x102 + 4 = 0x106
        SegmentedAddress addrA = new(0, 0x100);
        // Write a NOP at the fall-through (0x102) so exploration can decode it
        WriteNop(new SegmentedAddress(0, 0x102));
        // Write a NOP at the branch target (0x106) so exploration can decode it
        WriteNop(new SegmentedAddress(0, 0x106));
        // Write RETs after so exploration terminates
        WriteRet(new SegmentedAddress(0, 0x103));
        WriteRet(new SegmentedAddress(0, 0x107));

        CfgInstruction observedJnz = WriteConditionalJnzAndParse(addrA, 4);
        // Insert the observed node into the index (simulating first-parse)
        NodeIndex.Insert(observedJnz);

        // Act
        _explorer.ExploreFrom(observedJnz);

        // Assert: speculative nodes exist at the static successors
        SegmentedAddress addrFallthrough = new(0, 0x102); // JNZ is 2 bytes
        SegmentedAddress addrTarget = new(0, 0x106); // 0x102 + 4

        NodeIndex.HasAddress(addrFallthrough).Should().BeTrue("fall-through should have been explored");
        NodeIndex.HasAddress(addrTarget).Should().BeTrue("branch target should have been explored");

        // The speculative nodes should have successor edges from the observed JNZ
        CfgInstruction speculativeFallthrough = NodeIndex.GetAtAddress(addrFallthrough).First();
        CfgInstruction speculativeTarget = NodeIndex.GetAtAddress(addrTarget).First();

        speculativeFallthrough.IsSpeculative.Should().BeTrue();
        speculativeTarget.IsSpeculative.Should().BeTrue();

        // Key assertion: full bidirectional edges are wired via NodeLinker
        speculativeFallthrough.Predecessors.Should().Contain(observedJnz,
            "speculative fall-through should have predecessor edge from observed JNZ");
        speculativeTarget.Predecessors.Should().Contain(observedJnz,
            "speculative branch target should have predecessor edge from observed JNZ");
        observedJnz.Successors.Should().Contain(speculativeFallthrough,
            "observed JNZ should have successor edge to speculative fall-through");
        observedJnz.Successors.Should().Contain(speculativeTarget,
            "observed JNZ should have successor edge to speculative branch target");
    }

    /// <summary>
    /// Explorer wires edges along a speculative chain.
    /// Arrange: observed node at A with StaticSuccessorAddresses = [B]. B is a NOP falling through to C. C is RET.
    /// Act: ExploreFrom(A).
    /// Assert: node at B has successor edge to node at C. Node at C has predecessor edge from B.
    /// </summary>
    [Fact]
    public void ExplorerWiresEdgesAlongSpeculativeChain() {
        // Arrange: JMP short at A targeting B. B is NOP falling through to C. C is RET.
        SegmentedAddress addrA = new(0, 0x200);
        SegmentedAddress addrB = new(0, 0x204); // target of JMP from A: 0x202 + 2 = 0x204
        SegmentedAddress addrC = new(0, 0x205); // B is NOP (1 byte), so C = B + 1

        // Write JMP short at A targeting B (rel8 = +2, since JMP is 2 bytes, next is 0x202, target is 0x204)
        WriteJmpShort(addrA, 2);
        WriteNop(addrB);
        WriteRet(addrC);

        CfgInstruction observedJmp = Parser.ParseInstructionAt(addrA);
        NodeIndex.Insert(observedJmp);

        // Act
        _explorer.ExploreFrom(observedJmp);

        // Assert
        CfgInstruction nodeB = NodeIndex.GetAtAddress(addrB).First();
        CfgInstruction nodeC = NodeIndex.GetAtAddress(addrC).First();
        nodeB.IsSpeculative.Should().BeTrue();
        nodeC.IsSpeculative.Should().BeTrue();

        // Edge from observed A to speculative B (full bidirectional)
        nodeB.Predecessors.Should().Contain(observedJmp, "B should have predecessor edge from A");
        observedJmp.Successors.Should().Contain(nodeB, "A should have successor edge to B");

        // Edge from speculative B to speculative C (full bidirectional)
        nodeC.Predecessors.Should().Contain(nodeB, "C should have predecessor edge from B");
        nodeB.Successors.Should().Contain(nodeC, "B should have successor edge to C");
    }

    /// <summary>
    /// Explorer creates convergence edge to existing index node.
    /// Arrange: observed node at A with static successor B. Index already contains an observed node at B.
    /// Act: ExploreFrom(A).
    /// Assert: A.Successors contains the existing node at B. No new node minted at B.
    /// </summary>
    [Fact]
    public void ExplorerCreatesConvergenceEdgeToExistingObservedNode() {
        // Arrange: JMP short at A targeting B. B already in index (observed).
        SegmentedAddress addrA = new(0, 0x300);
        SegmentedAddress addrB = new(0, 0x304); // JMP is 2 bytes, rel8=+2, target = 0x302 + 2 = 0x304

        WriteJmpShort(addrA, 2);
        WriteNop(addrB);

        CfgInstruction observedJmp = Parser.ParseInstructionAt(addrA);
        NodeIndex.Insert(observedJmp);

        // Pre-insert an observed node at B
        CfgInstruction existingObservedB = Parser.ParseInstructionAt(addrB);
        NodeIndex.Insert(existingObservedB);
        int nodeCountBefore = NodeIndex.GetAtAddress(addrB).Count();

        // Act
        _explorer.ExploreFrom(observedJmp);

        // Assert: A has predecessor wired to existing B (convergence), no duplicate
        int nodeCountAfter = NodeIndex.GetAtAddress(addrB).Count();
        nodeCountAfter.Should().Be(nodeCountBefore, "no new node should be minted at B");

        existingObservedB.Predecessors.Should().Contain(observedJmp,
            "existing B should have predecessor edge from A");
    }

    /// <summary>
    /// Explorer creates convergence edge to existing speculative node.
    /// Arrange: two observed nodes X and Y both have static successor Z.
    /// ExploreFrom(X) creates speculative Z.
    /// Act: ExploreFrom(Y).
    /// Assert: Y.Successors contains the same speculative node at Z (not a second copy).
    /// Z has both X and Y in Predecessors.
    /// </summary>
    [Fact]
    public void ExplorerCreatesConvergenceEdgeToExistingSpeculativeNode() {
        // Arrange: JMP short at X targeting Z, JMP short at Y targeting Z.
        SegmentedAddress addrX = new(0, 0x400);
        SegmentedAddress addrY = new(0, 0x410);
        SegmentedAddress addrZ = new(0, 0x420);

        // JMP at X: 2 bytes, next=0x402, target=0x420 => rel8 = 0x420 - 0x402 = 0x1E (30)
        WriteJmpShort(addrX, 0x1E);
        // JMP at Y: 2 bytes, next=0x412, target=0x420 => rel8 = 0x420 - 0x412 = 0x0E (14)
        WriteJmpShort(addrY, 0x0E);
        // Write a RET at Z so exploration terminates
        WriteRet(addrZ);

        CfgInstruction observedX = Parser.ParseInstructionAt(addrX);
        NodeIndex.Insert(observedX);
        CfgInstruction observedY = Parser.ParseInstructionAt(addrY);
        NodeIndex.Insert(observedY);

        // First exploration from X creates speculative Z
        _explorer.ExploreFrom(observedX);
        CfgInstruction? speculativeZ = NodeIndex.GetAtAddress(addrZ).FirstOrDefault();
        speculativeZ.Should().NotBeNull();

        // Act: explore from Y
        _explorer.ExploreFrom(observedY);

        // Assert: Y has edge to the SAME speculative Z, no duplicate
        int nodeCountAtZ = NodeIndex.GetAtAddress(addrZ).Count();
        nodeCountAtZ.Should().Be(1, "only one node should exist at Z");

        CfgInstruction speculativeZNode = NodeIndex.GetAtAddress(addrZ).First();
        speculativeZNode.IsSpeculative.Should().BeTrue();

        observedY.Successors.Should().Contain(speculativeZNode,
            "explorer now wires full successor edges via NodeLinker");
        speculativeZNode.Predecessors.Should().Contain(observedX, "Z should have predecessor from X");
        speculativeZNode.Predecessors.Should().Contain(observedY, "Z should have predecessor from Y");
    }

    /// <summary>
    /// Explorer stops at poisoned address (no edge, no node).
    /// </summary>
    [Fact]
    public void ExplorerStopsAtPoisonedAddress() {
        // Arrange: JMP short at A targeting P. P is poisoned.
        SegmentedAddress addrA = new(0, 0x500);
        SegmentedAddress addrP = new(0, 0x504); // JMP is 2 bytes, rel8=+2

        WriteJmpShort(addrA, 2);
        WriteNop(addrP); // Write something valid at P (but it's poisoned)

        CfgInstruction observedA = Parser.ParseInstructionAt(addrA);
        NodeIndex.Insert(observedA);
        NodeIndex.PoisonSet.Add(addrP);

        // Act
        _explorer.ExploreFrom(observedA);

        // Assert: no node at P, no edge from A to P
        NodeIndex.HasAddress(addrP).Should().BeFalse("poisoned address should not get a node");
    }

    /// <summary>
    /// Explorer stops at invalid opcode and poisons the address.
    /// </summary>
    [Fact]
    public void ExplorerStopsAtInvalidOpcodeAndPoisonsAddress() {
        // Arrange: JMP short at A targeting I. I contains an invalid opcode (0xFF 0xFF).
        SegmentedAddress addrA = new(0, 0x600);
        SegmentedAddress addrI = new(0, 0x604); // JMP is 2 bytes, rel8=+2

        WriteJmpShort(addrA, 2);
        // Write invalid opcode at I: 0x0F 0xFF is invalid in real mode
        uint physI = MemoryUtils.ToPhysicalAddress(addrI.Segment, addrI.Offset);
        Memory.UInt8[physI] = 0x0F;
        Memory.UInt8[physI + 1] = 0xFF;

        CfgInstruction observedA = Parser.ParseInstructionAt(addrA);
        NodeIndex.Insert(observedA);

        // Act
        _explorer.ExploreFrom(observedA);

        // Assert
        NodeIndex.PoisonSet.Should().Contain(addrI, "invalid opcode address should be poisoned");
        observedA.Successors.Should().BeEmpty("A should have no successor edge to invalid I");
    }

    /// <summary>
    /// Explorer does not speculate call continuations.
    /// Arrange: observed CALL near at A targeting callee C. Instruction after the call is A+N.
    /// Act: ExploreFrom(A).
    /// Assert: speculative node at C (callee IS explored). No speculative node at A+N.
    /// </summary>
    [Fact]
    public void ExplorerDoesNotSpeculateCallContinuations() {
        // Arrange: CALL near at address A (opcode E8 + rel16).
        // CALL near rel16 is 3 bytes: E8 lo hi.
        SegmentedAddress addrA = new(0, 0x700);
        SegmentedAddress addrCallee = new(0, 0x720); // target
        SegmentedAddress addrContinuation = new(0, 0x703); // A + 3 (size of CALL near rel16)

        // Write CALL near at A targeting 0x720: rel16 = 0x720 - 0x703 = 0x1D
        uint physA = MemoryUtils.ToPhysicalAddress(addrA.Segment, addrA.Offset);
        Memory.UInt8[physA] = 0xE8; // CALL rel16
        Memory.UInt16[physA + 1] = 0x001D; // rel16 = 0x1D -> target = 0x703 + 0x1D = 0x720

        // Write a RET at the callee so exploration terminates there
        WriteRet(addrCallee);
        // Write a NOP at the continuation
        WriteNop(addrContinuation);

        CfgInstruction observedCall = Parser.ParseInstructionAt(addrA);
        NodeIndex.Insert(observedCall);

        // Act
        _explorer.ExploreFrom(observedCall);

        // Assert: callee IS explored
        NodeIndex.HasAddress(addrCallee).Should().BeTrue("callee entry should be explored");
        CfgInstruction calleeNode = NodeIndex.GetAtAddress(addrCallee).First();
        calleeNode.IsSpeculative.Should().BeTrue();

        // Call continuation is NOT explored
        bool hasContinuation = NodeIndex.GetAtAddress(addrContinuation)
            .Any(n => n.IsSpeculative);
        hasContinuation.Should().BeFalse("call continuation should NOT be speculated");
    }

    /// <summary>
    /// Explorer must not converge across a different opcode at the same address.
    /// Two predecessors X and Y both target Z. Between explorations the byte at Z is rewritten with
    /// a different opcode (self-modifying code). The explorer must NOT wire Y onto X's stale variant;
    /// it must mint a distinct variant matching the current memory and wire Y to it. This is the
    /// regression guard for the cross-variant convergence leak that forced a spurious SelectorNode.
    /// </summary>
    [Fact]
    public void ExplorerDoesNotConvergeAcrossDifferentOpcodeAtSameAddress() {
        SegmentedAddress addrX = new(0, 0x100);
        SegmentedAddress addrY = new(0, 0x110);
        SegmentedAddress addrZ = new(0, 0x120);

        // JMP at X: next=0x102, target=0x120 => rel8=0x1E
        WriteJmpShort(addrX, 0x1E);
        // JMP at Y: next=0x112, target=0x120 => rel8=0x0E
        WriteJmpShort(addrY, 0x0E);

        // First era: Z is RET.
        WriteRet(addrZ);
        CfgInstruction observedX = Parser.ParseInstructionAt(addrX);
        NodeIndex.Insert(observedX);
        _explorer.ExploreFrom(observedX);
        CfgInstruction variantRet = NodeIndex.GetAtAddress(addrZ).Single();
        variantRet.IsSpeculative.Should().BeTrue();

        // Second era: Z now decodes to HLT (different opcode => different SignatureFinal).
        uint physZ = MemoryUtils.ToPhysicalAddress(addrZ.Segment, addrZ.Offset);
        Memory.UInt8[physZ] = 0xF4; // HLT
        CfgInstruction observedY = Parser.ParseInstructionAt(addrY);
        NodeIndex.Insert(observedY);
        _explorer.ExploreFrom(observedY);

        // Two distinct variants now live at Z, each wired to its own predecessor.
        NodeIndex.GetAtAddress(addrZ).Should().HaveCount(2,
            "a different opcode at the same address must mint a distinct variant, not converge");
        CfgInstruction variantHlt = NodeIndex.GetAtAddress(addrZ).Single(n => !ReferenceEquals(n, variantRet));

        observedX.Successors.Should().Contain(variantRet);
        observedY.Successors.Should().Contain(variantHlt);
        observedY.Successors.Should().NotContain(variantRet,
            "Y must not be wired to X's stale-opcode variant (the cross-variant convergence leak)");
    }

    /// <summary>
    /// Explorer terminates on cyclic backward branch.
    /// Arrange: observed JNZ at A with static successors B and C.
    /// B is a JMP short targeting A (backward branch forming a cycle).
    /// Act: ExploreFrom(A).
    /// Assert: Exploration terminates (no infinite loop). Node at B with edge back to A.
    /// </summary>
    [Fact]
    public void ExplorerTerminatesOnCyclicBackwardBranch() {
        // Arrange: JNZ at A (0x800) with fall-through at B (0x802) and target at C (0x806).
        // B is JMP short back to A: rel8 = A - (B+2) = 0x800 - 0x804 = -4
        SegmentedAddress addrA = new(0, 0x800);
        SegmentedAddress addrB = new(0, 0x802); // fall-through (JNZ is 2 bytes)
        SegmentedAddress addrC = new(0, 0x806); // target: 0x802 + 4 = 0x806

        // JNZ at A with rel8=+4 (target = 0x802 + 4 = 0x806)
        uint physA = MemoryUtils.ToPhysicalAddress(addrA.Segment, addrA.Offset);
        Memory.UInt8[physA] = 0x75;
        Memory.Int8[physA + 1] = 4;

        // JMP short at B back to A: JMP is 2 bytes, next=0x804, target=0x800, rel8 = 0x800 - 0x804 = -4
        WriteJmpShort(addrB, -4);
        // RET at C
        WriteRet(addrC);

        CfgInstruction observedA = Parser.ParseInstructionAt(addrA);
        NodeIndex.Insert(observedA);

        // Act: should terminate (no infinite loop)
        _explorer.ExploreFrom(observedA);

        // Assert
        NodeIndex.HasAddress(addrB).Should().BeTrue("B should be explored");
        NodeIndex.HasAddress(addrC).Should().BeTrue("C should be explored");

        CfgInstruction? nodeB = NodeIndex.GetAtAddress(addrB).FirstOrDefault();
        nodeB.Should().NotBeNull();

        // B should have a predecessor edge wired to A (convergence edge via backward branch)
        observedA.Predecessors.Should().Contain(nodeB,
            "A should have predecessor edge from B (backward branch)");
    }

    // -----------------------------------------------------------------------
    // Known-Safe Handler Seeding Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// SeedKnownSafe with a simple callback + iret body produces the expected speculative
    /// chain. This is the common handler shape (most interrupt handlers).
    /// </summary>
    [Fact]
    public void SeedKnownSafeCallbackPlusIretProducesFullChain() {
        // Arrange: at address 0:0x900, write a callback (FE 38 xx xx) + IRET (CF)
        SegmentedAddress entry = new(0, 0x900);
        uint physAddr = MemoryUtils.ToPhysicalAddress(entry.Segment, entry.Offset);
        // Callback: FE 38 00 00 (4 bytes)
        Memory.UInt8[physAddr] = 0xFE;
        Memory.UInt8[physAddr + 1] = 0x38;
        Memory.UInt8[physAddr + 2] = 0x00;
        Memory.UInt8[physAddr + 3] = 0x00;
        // IRET: CF (1 byte)
        Memory.UInt8[physAddr + 4] = 0xCF;

        // Act
        _explorer.SeedKnownSafe(entry);

        // Assert: both instructions exist in the index as speculative
        NodeIndex.HasAddress(entry).Should().BeTrue("entry callback should be indexed");
        SegmentedAddress iretAddr = new(0, 0x904);
        NodeIndex.HasAddress(iretAddr).Should().BeTrue("iret should be indexed");

        CfgInstruction callback = NodeIndex.GetAtAddress(entry).First();
        CfgInstruction iret = NodeIndex.GetAtAddress(iretAddr).First();
        callback.IsSpeculative.Should().BeTrue();
        iret.IsSpeculative.Should().BeTrue();

        // Chain: callback -> iret (successor edge)
        callback.Successors.Should().Contain(iret);
        iret.Predecessors.Should().Contain(callback);
    }

    /// <summary>
    /// SeedKnownSafe is a no-op when the explorer is disabled (no node created).
    /// We test this by verifying that when no explorer exists in the feeder, the seed does nothing.
    /// Since we test the explorer directly here, we verify it's a no-op when address is already indexed.
    /// </summary>
    [Fact]
    public void SeedKnownSafeAddressAlreadyIndexedIsNoOp() {
        // Arrange: insert an observed node at the entry
        SegmentedAddress entry = new(0, 0xA00);
        uint physAddr = MemoryUtils.ToPhysicalAddress(entry.Segment, entry.Offset);
        Memory.UInt8[physAddr] = 0x90; // NOP

        CfgInstruction observed = Parser.ParseInstructionAt(entry);
        NodeIndex.Insert(observed);
        int countBefore = NodeIndex.GetAtAddress(entry).Count();

        // Act
        _explorer.SeedKnownSafe(entry);

        // Assert: no duplicate
        int countAfter = NodeIndex.GetAtAddress(entry).Count();
        countAfter.Should().Be(countBefore);
    }

    /// <summary>
    /// SeedKnownSafe with INT N + continuation (timer shape) wires CallToReturn edge
    /// and follows the continuation.
    /// Handler shape: callback + INT 0x1C + callback + IRET
    /// </summary>
    [Fact]
    public void SeedKnownSafeIntPlusContinuationWiresCallToReturnEdge() {
        // Arrange: at 0:0xB00 write: callback(4) + INT 0x1C(2) + callback(4) + IRET(1)
        SegmentedAddress entry = new(0, 0xB00);
        uint phys = MemoryUtils.ToPhysicalAddress(entry.Segment, entry.Offset);
        // Callback: FE 38 08 00
        Memory.UInt8[phys] = 0xFE;
        Memory.UInt8[phys + 1] = 0x38;
        Memory.UInt8[phys + 2] = 0x08;
        Memory.UInt8[phys + 3] = 0x00;
        // INT 0x1C: CD 1C
        Memory.UInt8[phys + 4] = 0xCD;
        Memory.UInt8[phys + 5] = 0x1C;
        // Callback: FE 38 09 00
        Memory.UInt8[phys + 6] = 0xFE;
        Memory.UInt8[phys + 7] = 0x38;
        Memory.UInt8[phys + 8] = 0x09;
        Memory.UInt8[phys + 9] = 0x00;
        // IRET: CF
        Memory.UInt8[phys + 10] = 0xCF;

        // Act
        _explorer.SeedKnownSafe(entry);

        // Assert: all four instructions indexed
        SegmentedAddress callbackAddr = entry;
        SegmentedAddress intAddr = new(0, 0xB04);
        SegmentedAddress afterIntAddr = new(0, 0xB06);
        SegmentedAddress iretAddr = new(0, 0xB0A);

        NodeIndex.HasAddress(callbackAddr).Should().BeTrue();
        NodeIndex.HasAddress(intAddr).Should().BeTrue();
        NodeIndex.HasAddress(afterIntAddr).Should().BeTrue("continuation after INT should be explored under trust");
        NodeIndex.HasAddress(iretAddr).Should().BeTrue();

        CfgInstruction intNode = NodeIndex.GetAtAddress(intAddr).First();
        CfgInstruction afterInt = NodeIndex.GetAtAddress(afterIntAddr).First();

        // The INT -> continuation edge should be CallToReturn
        intNode.SuccessorsPerType.Should().ContainKey(InstructionSuccessorType.CallToReturn);
        intNode.SuccessorsPerType[InstructionSuccessorType.CallToReturn].Should().Contain(afterInt);
        afterInt.Predecessors.Should().Contain(intNode);
    }

    /// <summary>
    /// SeedKnownSafe with far call (0x9A) to RETF stub + callback + IRET (mouse shape).
    /// The far call's callee is NOT explored (far call imm does not register a static successor for
    /// the callee - its operand is switchable via InMemoryAddressSwitcher). Only the continuation
    /// after the far call is explored under trust with a CallToReturn edge.
    /// </summary>
    [Fact]
    public void SeedKnownSafeFarCallToStubWiresCallToReturnForContinuation() {
        // Arrange: at 0:0xC00 write RETF (CB), then at 0:0xC01 write far call 0:0xC00 (9A 00 0C 00 00)
        // then callback(4) + IRET(1)
        SegmentedAddress retfStubAddr = new(0, 0xC00);
        uint physRetf = MemoryUtils.ToPhysicalAddress(retfStubAddr.Segment, retfStubAddr.Offset);
        Memory.UInt8[physRetf] = 0xCB; // RETF

        SegmentedAddress handlerEntry = new(0, 0xC01);
        uint physEntry = MemoryUtils.ToPhysicalAddress(handlerEntry.Segment, handlerEntry.Offset);
        // Far CALL: 9A offset_lo offset_hi seg_lo seg_hi -> CALL FAR 0000:0C00
        Memory.UInt8[physEntry] = 0x9A;
        Memory.UInt8[physEntry + 1] = 0x00; // offset low
        Memory.UInt8[physEntry + 2] = 0x0C; // offset high
        Memory.UInt8[physEntry + 3] = 0x00; // segment low
        Memory.UInt8[physEntry + 4] = 0x00; // segment high
        // Continuation at entry+5 = 0xC06
        // Callback: FE 38 74 00
        Memory.UInt8[physEntry + 5] = 0xFE;
        Memory.UInt8[physEntry + 6] = 0x38;
        Memory.UInt8[physEntry + 7] = 0x74;
        Memory.UInt8[physEntry + 8] = 0x00;
        // IRET: CF
        Memory.UInt8[physEntry + 9] = 0xCF;

        // Act
        _explorer.SeedKnownSafe(handlerEntry);

        // Assert: handler entry, callee, and continuation are all explored.
        // Far call imm now registers its target as a static successor (same as JMP FAR imm).
        // Self-modifying code cases use selectors which block speculative discovery separately.
        SegmentedAddress continuationAddr = new(0, 0xC06);
        SegmentedAddress iretAddr = new(0, 0xC0A);

        NodeIndex.HasAddress(handlerEntry).Should().BeTrue("far call instruction should be indexed");
        NodeIndex.HasAddress(continuationAddr).Should().BeTrue("continuation after far call should be explored under trust");
        NodeIndex.HasAddress(iretAddr).Should().BeTrue("IRET terminator should be explored");

        // The callee (RETF stub) at 0:0xC00 IS explored: far call imm registers target as static successor
        NodeIndex.HasAddress(retfStubAddr).Should().BeTrue(
            "far call imm callee is explored via its static successor (self-modifying code uses selectors to block this)");

        CfgInstruction farCallNode = NodeIndex.GetAtAddress(handlerEntry).First();
        CfgInstruction continuationNode = NodeIndex.GetAtAddress(continuationAddr).First();

        // Far call -> continuation should be CallToReturn edge
        farCallNode.SuccessorsPerType.Should().ContainKey(InstructionSuccessorType.CallToReturn);
        farCallNode.SuccessorsPerType[InstructionSuccessorType.CallToReturn].Should().Contain(continuationNode);
    }

    /// <summary>
    /// Trust crosses the call boundary into the callee.
    /// A callee that itself contains a CALL should also have its own call-continuation explored,
    /// with a CallToReturn edge, because known-safe trust propagates into callees.
    /// </summary>
    [Fact]
    public void SeedKnownSafeTrustCrossesCallBoundary() {
        // Arrange: Handler at 0:0xD00 = CALL NEAR 0xD10(3) + callback(4) + IRET(1)
        // Callee at 0:0xD10 = CALL NEAR 0xD20(3) + RET(1)
        // Nested callee at 0:0xD20 = RET(1)
        SegmentedAddress handlerEntry = new(0, 0xD00);
        uint physHandler = MemoryUtils.ToPhysicalAddress(handlerEntry.Segment, handlerEntry.Offset);

        // CALL NEAR 0xD10: E8 rel16 where rel16 = 0xD10 - 0xD03 = 0x0D
        Memory.UInt8[physHandler] = 0xE8;
        Memory.UInt16[physHandler + 1] = 0x000D; // target = 0xD03 + 0x0D = 0xD10

        // Callback at 0xD03: FE 38 00 00
        Memory.UInt8[physHandler + 3] = 0xFE;
        Memory.UInt8[physHandler + 4] = 0x38;
        Memory.UInt8[physHandler + 5] = 0x00;
        Memory.UInt8[physHandler + 6] = 0x00;
        // IRET at 0xD07: CF
        Memory.UInt8[physHandler + 7] = 0xCF;

        // Callee at 0xD10: CALL NEAR 0xD20, E8 rel16 where rel16 = 0xD20 - 0xD13 = 0x0D
        uint physCallee = MemoryUtils.ToPhysicalAddress(0, 0xD10);
        Memory.UInt8[physCallee] = 0xE8;
        Memory.UInt16[physCallee + 1] = 0x000D; // target = 0xD13 + 0x0D = 0xD20
        // RET at 0xD13
        Memory.UInt8[physCallee + 3] = 0xC3;

        // Nested callee at 0xD20: RET
        uint physNested = MemoryUtils.ToPhysicalAddress(0, 0xD20);
        Memory.UInt8[physNested] = 0xC3;

        // Act
        _explorer.SeedKnownSafe(handlerEntry);

        // Assert: handler's call continuation at 0xD03 IS explored (trust follows continuation of handler's call)
        SegmentedAddress handlerContinuation = new(0, 0xD03);
        NodeIndex.HasAddress(handlerContinuation).Should().BeTrue(
            "handler's own call continuation should be explored under trust");

        // The callee at 0xD10 IS explored
        SegmentedAddress calleeEntry = new(0, 0xD10);
        NodeIndex.HasAddress(calleeEntry).Should().BeTrue("callee entry should be explored");

        // The nested callee at 0xD20 IS explored (it's the callee's callee entry)
        SegmentedAddress nestedCalleeEntry = new(0, 0xD20);
        NodeIndex.HasAddress(nestedCalleeEntry).Should().BeTrue("nested callee entry is explored");

        // The callee's OWN continuation at 0xD13 IS also explored, because trust now propagates
        // into callees rather than being dropped at the call boundary.
        SegmentedAddress calleeContinuation = new(0, 0xD13);
        NodeIndex.HasAddress(calleeContinuation).Should().BeTrue(
            "callee's call continuation should be explored because trust crosses the call boundary");

        // The callee's CALL should also have wired a CallToReturn edge to its own continuation.
        CfgInstruction calleeCallNode = NodeIndex.GetAtAddress(calleeEntry).First();
        CfgInstruction calleeContinuationNode = NodeIndex.GetAtAddress(calleeContinuation).First();
        calleeCallNode.SuccessorsPerType.Should().ContainKey(InstructionSuccessorType.CallToReturn);
        calleeCallNode.SuccessorsPerType[InstructionSuccessorType.CallToReturn].Should().Contain(calleeContinuationNode);
    }

    /// <summary>
    /// Non-seeded exploration of the same code does NOT produce continuation edges.
    /// </summary>
    [Fact]
    public void ExploreFromDoesNotProduceContinuationEdges() {
        // Arrange: at 0:0xE00, observed CALL NEAR targeting 0xE10, with a NOP at the continuation 0xE03
        SegmentedAddress addrA = new(0, 0xE00);
        uint physA = MemoryUtils.ToPhysicalAddress(addrA.Segment, addrA.Offset);
        // CALL NEAR 0xE10: E8 rel16 where rel16 = 0xE10 - 0xE03 = 0x0D
        Memory.UInt8[physA] = 0xE8;
        Memory.UInt16[physA + 1] = 0x000D;
        // RET at callee 0xE10
        WriteRet(new SegmentedAddress(0, 0xE10));
        // NOP at continuation 0xE03
        WriteNop(new SegmentedAddress(0, 0xE03));

        CfgInstruction observedCall = Parser.ParseInstructionAt(addrA);
        NodeIndex.Insert(observedCall);

        // Act: normal (non-seeded) exploration
        _explorer.ExploreFrom(observedCall);

        // Assert: continuation NOT explored
        bool hasContinuation = NodeIndex.GetAtAddress(new SegmentedAddress(0, 0xE03))
            .Any(n => n.IsSpeculative);
        hasContinuation.Should().BeFalse(
            "non-seeded exploration must NOT speculate call continuations");

        // But the callee IS explored
        NodeIndex.HasAddress(new SegmentedAddress(0, 0xE10)).Should().BeTrue("callee should be explored");

        // And no CallToReturn edge exists
        observedCall.SuccessorsPerType.Should().NotContainKey(InstructionSuccessorType.CallToReturn);
    }
}
