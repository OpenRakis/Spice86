namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Liveness;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

public class InstructionFlagFactsBuilderTest {
    private static readonly SegmentedAddress At = new(0x1000, 0x0010);
    private static readonly SequentialIdAllocator ParserIds = new(1_000_000);
    private static readonly CfgCodePartition Partition = new() {
        Id = 1, Kind = CfgCodePartitionKind.Observed, Name = "partition_1", Blocks = [], Entries = []
    };

    [Fact]
    public void CompareHasItsAluEffectAndNoBoundary() {
        // Arrange
        CfgInstruction compare = Parse(0x39, 0xD8);

        // Act
        InstructionFlagFacts facts = Build(compare, [compare], new Dictionary<SegmentedAddress, ICfgNode>());

        // Assert
        facts.Should().Be(new InstructionFlagFacts(FlagEffect.Writing(StatusFlags.All), false, false));
    }

    [Theory]
    [InlineData(new byte[] { 0xE8, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xC3 })]
    [InlineData(new byte[] { 0xCB })]
    [InlineData(new byte[] { 0xCF })]
    [InlineData(new byte[] { 0xCD, 0x21 })]
    [InlineData(new byte[] { 0xF4 })]
    public void CallsReturnsInterruptsAndHaltLeaveEveryFlagLive(byte[] bytes) {
        // Arrange
        CfgInstruction instruction = Parse(bytes);

        // Act
        InstructionFlagFacts facts = Build(instruction, [instruction], new Dictionary<SegmentedAddress, ICfgNode>());

        // Assert
        facts.AllLiveAfter.Should().BeTrue();
    }

    [Fact]
    public void SuccessorOutsideTheMethodLeavesEveryFlagLive() {
        // Arrange
        CfgInstruction jump = Parse(0xEB, 0x00);
        CfgInstruction elsewhere = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x2000, 0x0000));
        jump.Successors.Add(elsewhere);

        // Act
        InstructionFlagFacts outside = Build(jump, [jump], new Dictionary<SegmentedAddress, ICfgNode>());
        InstructionFlagFacts inside = Build(jump, [jump, elsewhere], new Dictionary<SegmentedAddress, ICfgNode>());

        // Assert
        outside.AllLiveAfter.Should().BeTrue();
        inside.AllLiveAfter.Should().BeFalse();
    }

    [Fact]
    public void SynthesizedConditionalEdgeLeavesEveryFlagLive() {
        // Arrange
        CfgInstruction jumpIfZero = Parse(0x74, 0x05);
        CfgInstruction target = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0017));

        // Act
        InstructionFlagFacts facts = Build(jumpIfZero, [jumpIfZero, target],
            new Dictionary<SegmentedAddress, ICfgNode> { [target.Address] = target });

        // Assert
        facts.Effect.Should().Be(FlagEffect.Reading(StatusFlags.Zero));
        facts.AllLiveAfter.Should().BeTrue();
    }

    [Fact]
    public void UntestedConditionalArmLeavesNothingLive() {
        // Arrange
        CfgInstruction jumpIfZero = Parse(0x74, 0x05);

        // Act
        InstructionFlagFacts facts = Build(jumpIfZero, [jumpIfZero], new Dictionary<SegmentedAddress, ICfgNode>());

        // Assert
        facts.AllLiveAfter.Should().BeFalse();
    }

    [Fact]
    public void ObservedConditionalArmsAreNotSynthesized() {
        // Arrange
        CfgInstruction jumpIfZero = Parse(0x74, 0x05);
        CfgInstruction taken = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0017));
        CfgInstruction notTaken = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x0012));
        jumpIfZero.Successors.Add(taken);
        jumpIfZero.Successors.Add(notTaken);
        jumpIfZero.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { taken, notTaken };

        // Act
        InstructionFlagFacts facts = Build(jumpIfZero, [jumpIfZero, taken, notTaken],
            new Dictionary<SegmentedAddress, ICfgNode> { [taken.Address] = taken, [notTaken.Address] = notTaken });

        // Assert
        facts.AllLiveAfter.Should().BeFalse();
    }

    [Fact]
    public void ObservedCpuFaultMakesEveryFlagLiveBefore() {
        // Arrange
        CfgInstruction faulting = Parse(0xF6, 0xF3);
        CfgInstruction handler = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0xF000, 0x0000));
        faulting.SuccessorsPerType[InstructionSuccessorType.CpuFault] = new HashSet<ICfgNode> { handler };
        CfgInstruction neverFaulted = Parse(0xF6, 0xF3);

        // Act
        InstructionFlagFacts faultingFacts = Build(faulting, [faulting], new Dictionary<SegmentedAddress, ICfgNode>());
        InstructionFlagFacts neverFaultedFacts = Build(neverFaulted, [neverFaulted], new Dictionary<SegmentedAddress, ICfgNode>());

        // Assert
        faultingFacts.AllLiveBefore.Should().BeTrue();
        neverFaultedFacts.AllLiveBefore.Should().BeFalse();
    }

    private static InstructionFlagFacts Build(CfgInstruction instruction, ICfgNode[] methodNodes,
        Dictionary<SegmentedAddress, ICfgNode> blockEntryByAddress) =>
        new InstructionFlagFactsBuilder(CreateContext(methodNodes, blockEntryByAddress)).Build(instruction, methodNodes.ToHashSet());

    private static CfgGeneratorContext CreateContext(ICfgNode[] methodNodes,
        Dictionary<SegmentedAddress, ICfgNode> blockEntryByAddress) {
        Dictionary<ICfgNode, CfgCodePartition> partitionByNode = methodNodes.ToDictionary(node => node, _ => Partition);
        return new CfgGeneratorContext(
            new CfgPartitionedProgram { Partitions = [], Transfers = [] },
            partitionByNode,
            methodNames: new(),
            partitionBaseNames: new(),
            segmentVariables: new(),
            transfersByEdge: new(),
            entriesByPartition: new(),
            blockEntryByAddress: blockEntryByAddress,
            signatureFields: []);
    }

    [Fact]
    public void GivenAstReplacesTheInstructionsOwnEffect() {
        // Arrange
        CfgInstruction compare = Parse(0x39, 0xD8);
        BlockNode compareWithoutAluCall = new(new MoveIpNextNode(new ConstantNode(DataType.UINT16, 0x0012)));
        CfgGeneratorContext context = CreateContext([compare], new Dictionary<SegmentedAddress, ICfgNode>());

        // Act
        InstructionFlagFacts facts = new InstructionFlagFactsBuilder(context)
            .Build(compare, compareWithoutAluCall, new HashSet<ICfgNode> { compare });

        // Assert
        facts.Should().Be(new InstructionFlagFacts(FlagEffect.None, false, false));
    }

    private static CfgInstruction Parse(params byte[] bytes) {
        Memory memory = new(new AddressReadWriteBreakpoints(), new Ram(0x100000), new A20Gate(), new RealModeMmu8086(), false);
        State state = new(CpuModel.INTEL_80386);
        InstructionParser parser = new(memory, state, ParserIds);
        for (int index = 0; index < bytes.Length; index++) {
            memory.UInt8[At.Linear + (uint)index] = bytes[index];
        }
        return parser.ParseInstructionAt(At);
    }
}
