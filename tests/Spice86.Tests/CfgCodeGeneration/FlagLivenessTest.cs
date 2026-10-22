namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Liveness;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

public class FlagLivenessTest {
    private const StatusFlags ShiftFlags = StatusFlags.Carry | StatusFlags.Parity | StatusFlags.Zero | StatusFlags.Sign | StatusFlags.Overflow;
    private readonly Dictionary<ICfgNode, InstructionFlagFacts> _facts = new();
    private ushort _nextOffset;

    [Fact]
    public void FlagsOverwrittenOnEveryPathAreDeadAfterTheJump() {
        // Arrange
        CfgInstruction compare = Node(Writes(StatusFlags.All));
        CfgInstruction jumpIfZero = Node(Reads(StatusFlags.Zero));
        CfgBlock test = Block(1, compare, jumpIfZero);
        CfgBlock taken = Block(2, Node(Writes(StatusFlags.All)), Node(Exit()));
        CfgBlock notTaken = Block(3, Node(Writes(StatusFlags.All)), Node(Exit()));

        // Act
        FlagLiveness liveness = Compute([test, taken, notTaken], new() { [test] = [taken, notTaken] });

        // Assert
        liveness.LiveAfter(jumpIfZero).Should().Be(StatusFlags.None);
        liveness.LiveBefore(jumpIfZero).Should().Be(StatusFlags.Zero);
        liveness.LiveAfter(compare).Should().Be(StatusFlags.Zero);
        liveness.LiveIn(test).Should().Be(StatusFlags.None);
        liveness.LiveOut(taken).Should().Be(StatusFlags.All);
    }

    [Fact]
    public void FlagsReadOnOnePathAreLiveAfterTheJump() {
        // Arrange
        CfgInstruction compare = Node(Writes(StatusFlags.All));
        CfgInstruction jumpIfZero = Node(Reads(StatusFlags.Zero));
        CfgBlock test = Block(1, compare, jumpIfZero);
        CfgBlock taken = Block(2, Node(new InstructionFlagFacts(FlagEffect.None, false, false)), Node(Exit()));
        CfgBlock notTaken = Block(3, Node(Writes(StatusFlags.All)), Node(Exit()));

        // Act
        FlagLiveness liveness = Compute([test, taken, notTaken], new() { [test] = [taken, notTaken] });

        // Assert
        liveness.LiveAfter(jumpIfZero).Should().Be(StatusFlags.All);
        liveness.LiveAfter(compare).Should().Be(StatusFlags.All);
        liveness.LiveBefore(compare).Should().Be(StatusFlags.None);
    }

    [Fact]
    public void DecrementKeepsCarryLiveAroundALoop() {
        // Arrange
        CfgInstruction decrement = Node(Writes(StatusFlags.All & ~StatusFlags.Carry));
        CfgInstruction jumpIfNotZero = Node(Reads(StatusFlags.Zero));
        CfgBlock loop = Block(1, decrement, jumpIfNotZero);
        CfgBlock exit = Block(2,
            Node(new InstructionFlagFacts(new FlagEffect(StatusFlags.Carry, StatusFlags.All, StatusFlags.All), false, false)),
            Node(Exit()));

        // Act
        FlagLiveness liveness = Compute([loop, exit], new() { [loop] = [loop, exit] });

        // Assert
        liveness.LiveIn(exit).Should().Be(StatusFlags.Carry);
        liveness.LiveAfter(jumpIfNotZero).Should().Be(StatusFlags.Carry);
        liveness.LiveAfter(decrement).Should().Be(StatusFlags.Zero | StatusFlags.Carry);
        liveness.LiveIn(loop).Should().Be(StatusFlags.Carry);
    }

    [Fact]
    public void CpuFaultMakesEveryFlagLiveBefore() {
        // Arrange
        CfgInstruction divide = Node(new InstructionFlagFacts(FlagEffect.None, true, false));
        CfgBlock block = Block(1, divide, Node(Writes(StatusFlags.All)), Node(Exit()));

        // Act
        FlagLiveness liveness = Compute([block], new());

        // Assert
        liveness.LiveBefore(divide).Should().Be(StatusFlags.All);
        liveness.LiveAfter(divide).Should().Be(StatusFlags.None);
    }

    [Fact]
    public void LeavingTheMethodMakesEveryFlagLiveAfter() {
        // Arrange
        CfgInstruction compare = Node(Writes(StatusFlags.All));
        CfgBlock block = Block(1, compare, Node(Exit()));

        // Act
        FlagLiveness liveness = Compute([block], new());

        // Assert
        liveness.LiveAfter(compare).Should().Be(StatusFlags.All);
        liveness.LiveOut(block).Should().Be(StatusFlags.All);
    }

    [Fact]
    public void MayWriteDoesNotEndLiveness() {
        // Arrange
        CfgInstruction compare = Node(Writes(StatusFlags.All));
        CfgInstruction shiftByCl = Node(new InstructionFlagFacts(new FlagEffect(StatusFlags.None, StatusFlags.None, ShiftFlags), false, false));
        CfgInstruction jumpIfCarry = Node(Reads(StatusFlags.Carry));
        CfgBlock test = Block(1, compare, shiftByCl, jumpIfCarry);
        CfgBlock taken = Block(2, Node(Writes(StatusFlags.All)), Node(Exit()));
        CfgBlock notTaken = Block(3, Node(Writes(StatusFlags.All)), Node(Exit()));

        // Act
        FlagLiveness liveness = Compute([test, taken, notTaken], new() { [test] = [taken, notTaken] });

        // Assert
        liveness.LiveBefore(shiftByCl).Should().Be(StatusFlags.Carry);
        liveness.LiveAfter(compare).Should().Be(StatusFlags.Carry);
    }

    private CfgInstruction Node(InstructionFlagFacts facts) {
        CfgInstruction node = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, _nextOffset));
        _nextOffset += 0x10;
        _facts[node] = facts;
        return node;
    }

    private static InstructionFlagFacts Writes(StatusFlags flags) => new(FlagEffect.Writing(flags), false, false);

    private static InstructionFlagFacts Reads(StatusFlags flags) => new(FlagEffect.Reading(flags), false, false);

    private static InstructionFlagFacts Exit() => new(FlagEffect.None, false, true);

    private static CfgBlock Block(int id, params ICfgNode[] nodes) {
        CfgBlock block = new(id, nodes[0]);
        foreach (ICfgNode node in nodes.Skip(1)) {
            block.Append(node);
        }
        return block;
    }

    private FlagLiveness Compute(IReadOnlyList<CfgBlock> blocks, Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors) {
        PartitionBlockGraph graph = new(blocks, [blocks[0]], successors, new Dictionary<CfgBlock, CfgBlock?>());
        DepthFirstOrdering<CfgBlock> traversal = new([graph.PrimaryEntryBlock], graph.GetSuccessors);
        return FlagLiveness.Compute(graph, traversal, node => _facts[node]);
    }
}
