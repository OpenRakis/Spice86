namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;

using Xunit;

/// <summary>
/// Tests basic <see cref="CfgBlock"/> state derived from contained instructions.
/// </summary>
public class BlockBasicsTest {
    /// <summary>Address used for the synthetic single-instruction block under test.</summary>
    private static readonly SegmentedAddress TestAddress = new(0x1000, 0x0000);
    private static readonly SequentialIdAllocator _allocator = new();

    /// <summary>
    /// <see cref="CfgBlock.IsLive"/> reflects the live state of its single contained
    /// instruction and correctly tracks round-trip transitions via <see cref="CfgInstruction.SetLive"/>.
    /// </summary>
    [Fact]
    public void IsLive_PropagatesFromSingleInstruction_OnSetLiveTransitions() {
        CfgInstruction entry = CreateMinimalInstruction();
        CfgBlock block = new(_allocator.AllocateId(), entry);
        entry.ContainingBlock = block;

        block.IsLive.Should().BeTrue(
            "a block is live iff every contained instruction is live");

        entry.SetLive(false);
        block.IsLive.Should().BeFalse(
            "marking the only instruction as non-live must flip the block's IsLive to false");

        entry.SetLive(true);
        block.IsLive.Should().BeTrue(
            "re-marking every contained instruction as live must flip the block's IsLive back to true");
    }

    /// <summary>
    /// Verifies that <see cref="CfgBlock.IsLive"/> correctly tracks the non-live counter
    /// across multiple instructions: the block is live only when all contained instructions
    /// are live.
    /// </summary>
    [Fact]
    public void IsLive_TracksNonLiveCounter_AcrossMultipleInstructions() {
        CfgInstruction instr0 = CreateMinimalInstruction(new SegmentedAddress(0x1000, 0));
        CfgInstruction instr1 = CreateMinimalInstruction(new SegmentedAddress(0x1000, 1));
        CfgInstruction instr2 = CreateMinimalInstruction(new SegmentedAddress(0x1000, 2));

        CfgBlock block = new(_allocator.AllocateId(), instr0);
        block.Append(instr1);
        block.Append(instr2);
        instr0.ContainingBlock = block;
        instr1.ContainingBlock = block;
        instr2.ContainingBlock = block;

        block.IsLive.Should().BeTrue("all instructions are live");

        instr1.SetLive(false);
        block.IsLive.Should().BeFalse("one instruction is non-live");

        instr2.SetLive(false);
        block.IsLive.Should().BeFalse("two instructions are non-live");

        instr2.SetLive(true);
        block.IsLive.Should().BeFalse("instr1 is still non-live");

        instr1.SetLive(true);
        block.IsLive.Should().BeTrue("all instructions are live again");
    }

    /// <summary>
    /// <see cref="CfgBlock.DisplayAst"/> returns a <see cref="BlockNode"/> wrapping all
    /// contained instructions' display ASTs.
    /// </summary>
    [Fact]
    public void DisplayAst_ReturnsBlockNodeWrappingInstructions() {
        TestInstructionHelper helper = new();
        CfgInstruction terminator = helper.WriteAndParse(TestAddress, w => w.WriteUInt8(0x90));
        CfgBlock block = new(_allocator.AllocateId(), terminator);

        // Act
        IVisitableAstNode blockAst = block.DisplayAst;

        blockAst.Should().BeOfType<BlockNode>();
        BlockNode blockNode = (BlockNode)blockAst;
        blockNode.Statements.Should().HaveCount(1);
        blockNode.Statements[0].Should().BeSameAs(terminator.DisplayAst);
    }

    /// <summary>
    /// Builds a minimal <see cref="CfgInstruction"/> at <see cref="TestAddress"/>.
    /// Sufficient for tests that do not access DisplayAst or ExecutionAst.
    /// </summary>
    private static CfgInstruction CreateMinimalInstruction() => CreateMinimalInstruction(TestAddress);

    /// <summary>
    /// Builds a minimal <see cref="CfgInstruction"/> at the given address.
    /// </summary>
    private static CfgInstruction CreateMinimalInstruction(SegmentedAddress address) {
        InstructionField<ushort> opcodeField = new(
            indexInInstruction: 0,
            length: 1,
            physicalAddress: address.Linear,
            value: 0x90,
            signatureValue: ImmutableList.Create<byte?>(0x90),
            final: true);
        return new CfgInstruction(_allocator.AllocateId(), address, opcodeField, new List<InstructionPrefix>(), maxSuccessorsCount: 1);
    }
}
