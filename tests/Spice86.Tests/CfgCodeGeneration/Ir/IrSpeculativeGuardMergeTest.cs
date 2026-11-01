namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The speculative-guard merge pass prints one <c>VerifySpeculativeRunOrFail</c> for a straight-line
/// run of speculative instructions when nothing between the first instruction and a later one can
/// change the run's bytes or leave the run, and keeps the per-instruction
/// <c>VerifySpeculativeEntryOrFail</c> wherever a memory write (SG-1), an interrupt delivery (SG-2), a
/// fault edge (SG-3), a port access (SG-4) or a contiguity break (SG-5) blocks the merge.
/// </summary>
public sealed class IrSpeculativeGuardMergeTest {
    private static List<string> GuardLines(GeneratedCSharpProgram program, string callName) =>
        program.SourceText.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains(callName))
            .ToList();

    private static void Speculative(params CfgInstruction[] instructions) {
        foreach (CfgInstruction instruction in instructions) {
            instruction.SetSpeculative(true);
        }
    }

    [Fact]
    public void RegisterOnlyRunSharesOneGuard() {
        // Arrange: observed nop, then speculative mov ax,1 / inc ax / mov bx,ax / hlt, all contiguous.
        TestInstructionHelper helper = new();
        CfgInstruction nop = Parse(helper, 0x0000, 0x90);
        CfgInstruction mov = Parse(helper, 0x0001, 0xB8, 0x01, 0x00);
        CfgInstruction inc = Parse(helper, 0x0004, 0x40);
        CfgInstruction movBx = Parse(helper, 0x0005, 0x8B, 0xD8);
        CfgInstruction hlt = Parse(helper, 0x0007, 0xF4);
        Speculative(mov, inc, movBx, hlt);
        CfgBlock block = Linear(nop, mov, inc, movBx, hlt);

        // Act
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        AssertCompiles(program);
        GuardLines(program, "VerifySpeculativeRunOrFail(").Should().ContainSingle(
            line => line.StartsWith("VerifySpeculativeRunOrFail(biosSegment, 0x0001,"),
            "the four contiguous speculative instructions share one run guard at the run entry");
        GuardLines(program, "VerifySpeculativeEntryOrFail(").Should().BeEmpty(
            "no per-instruction guard survives inside the merged run");
    }

    [Fact]
    public void StoreIntoTheRunKeepsTheLaterGuard() {
        // Arrange: observed mov ax,0xF000 / mov es,ax, then speculative
        // mov byte [es:0x000B],0x90 (rewrites the nop at F000:000B) / nop / hlt.
        TestInstructionHelper helper = new();
        CfgInstruction movAx = Parse(helper, 0x0000, 0xB8, 0x00, 0xF0);
        CfgInstruction movEs = Parse(helper, 0x0003, 0x8E, 0xC0);
        CfgInstruction store = Parse(helper, 0x0005, 0x26, 0xC6, 0x06, 0x0B, 0x00, 0x90);
        CfgInstruction nop = Parse(helper, 0x000B, 0x90);
        CfgInstruction hlt = Parse(helper, 0x000C, 0xF4);
        Speculative(store, nop, hlt);
        CfgBlock block = Linear(movAx, movEs, store, nop, hlt);

        // Act
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert: the store cannot merge with the nop guard, so the nop guard still runs after the store.
        AssertCompiles(program);
        GuardLines(program, "VerifySpeculativeEntryOrFail(").Should().ContainSingle(
            line => line.StartsWith("VerifySpeculativeEntryOrFail(biosSegment, 0x0005,"),
            "the store's guard cannot cover the nop it rewrites");
        GuardLines(program, "VerifySpeculativeRunOrFail(")
            .Should().OnlyContain(line => line.StartsWith("VerifySpeculativeRunOrFail(biosSegment, 0x000B,"),
                "no merged guard may cover the store and the instruction it rewrites");
    }

    [Fact]
    public void StoreDisjointFromTheRunSharesOneGuard() {
        // Arrange: observed xor ax,ax / mov ds,ax, then speculative
        // mov byte [0x0401],0xDD (a data write far from F000 code) / nop / hlt.
        TestInstructionHelper helper = new();
        CfgInstruction xorAx = Parse(helper, 0x0000, 0x33, 0xC0);
        CfgInstruction movDs = Parse(helper, 0x0002, 0x8E, 0xD8);
        CfgInstruction store = Parse(helper, 0x0004, 0xC6, 0x06, 0x01, 0x04, 0xDD);
        CfgInstruction nop = Parse(helper, 0x0009, 0x90);
        CfgInstruction hlt = Parse(helper, 0x000A, 0xF4);
        Speculative(store, nop, hlt);
        CfgBlock block = Linear(xorAx, movDs, store, nop, hlt);

        // Act
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert: the store is proved disjoint from the run's bytes, so the run merges.
        AssertCompiles(program);
        GuardLines(program, "VerifySpeculativeRunOrFail(").Should().ContainSingle(
            line => line.StartsWith("VerifySpeculativeRunOrFail(biosSegment, 0x0004,"),
            "a store proved disjoint from the code bytes does not block the merge");
        GuardLines(program, "VerifySpeculativeEntryOrFail(").Should().BeEmpty();
    }

    [Fact]
    public void StiKeepsTheNextGuard() {
        // Arrange: observed nop, then speculative sti / nop / hlt; the interrupt delivery check
        // emitted after sti can change memory, so the nop keeps a guard that runs after it.
        TestInstructionHelper helper = new();
        CfgInstruction entry = Parse(helper, 0x0000, 0x90);
        CfgInstruction sti = Parse(helper, 0x0001, 0xFB);
        CfgInstruction nop = Parse(helper, 0x0002, 0x90);
        CfgInstruction hlt = Parse(helper, 0x0003, 0xF4);
        Speculative(sti, nop, hlt);
        // sti ends its block: the interrupt delivery check it emits is the block's last statement.
        CfgInstruction[] first = Chain(entry, sti);
        CfgInstruction[] second = Chain(nop, hlt);
        CfgBlock firstBlock = Block(1, first);
        CfgBlock secondBlock = Block(2, second);
        sti.Successors.Add(nop);
        sti.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { nop };

        // Act
        GeneratedCSharpProgram program = Generate(SingleEntry(firstBlock), firstBlock, secondBlock);

        // Assert
        AssertCompiles(program);
        program.SourceText.Should().Contain("CheckExternalEvents",
            "sti emits an interrupt delivery check mid-block");
        GuardLines(program, "VerifySpeculativeEntryOrFail(").Should().ContainSingle(
            line => line.StartsWith("VerifySpeculativeEntryOrFail(biosSegment, 0x0001,"),
            "the sti guard cannot cover instructions past the interrupt delivery check");
    }

    [Fact]
    public void FaultEdgeKeepsTheNextGuard() {
        // Arrange: observed nop, then speculative div bl (CPU-fault successor) / nop / hlt.
        TestInstructionHelper helper = new();
        CfgInstruction entry = Parse(helper, 0x0000, 0x90);
        CfgInstruction div = Parse(helper, 0x0001, 0xF6, 0xF3);
        CfgInstruction nop = Parse(helper, 0x0003, 0x90);
        CfgInstruction hlt = Parse(helper, 0x0004, 0xF4);
        CfgInstruction faultHandler = Parse(helper, 0x0100, 0x90);
        Speculative(div, nop, hlt);
        div.SuccessorsPerType[InstructionSuccessorType.CpuFault] =
            new HashSet<ICfgNode> { faultHandler };
        CfgBlock block = Linear(entry, div, nop, hlt);
        CfgCodePartition main = Partition(1, block);
        CfgCodePartition handlerPartition = Partition(2, [Block(9, faultHandler)]);
        CfgCodePartitionTransfer faultTransfer = new() {
            FromPartition = main,
            ToPartition = handlerPartition,
            FromNode = div,
            TargetNode = faultHandler,
            Kind = CfgCodePartitionTransferKind.CpuFault
        };

        // Act
        GeneratedCSharpProgram program = GenerateProgram([main, handlerPartition], [faultTransfer]);

        // Assert
        AssertCompiles(program);
        GuardLines(program, "VerifySpeculativeEntryOrFail(").Should().ContainSingle(
            line => line.StartsWith("VerifySpeculativeEntryOrFail(biosSegment, 0x0001,"),
            "the div guard cannot cover instructions past a possible CPU fault");
    }

    [Fact]
    public void PortWriteKeepsTheNextGuard() {
        // Arrange: observed nop, then speculative out dx,al / nop / hlt.
        TestInstructionHelper helper = new();
        CfgInstruction entry = Parse(helper, 0x0000, 0x90);
        CfgInstruction outDx = Parse(helper, 0x0001, 0xEE);
        CfgInstruction nop = Parse(helper, 0x0002, 0x90);
        CfgInstruction hlt = Parse(helper, 0x0003, 0xF4);
        Speculative(outDx, nop, hlt);
        CfgBlock block = Linear(entry, outDx, nop, hlt);

        // Act
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        AssertCompiles(program);
        GuardLines(program, "VerifySpeculativeEntryOrFail(").Should().ContainSingle(
            line => line.StartsWith("VerifySpeculativeEntryOrFail(biosSegment, 0x0001,"),
            "the port access guard cannot cover instructions past a possible DMA write");
    }

    [Fact]
    public void ObservedInstructionInsideTheRunSplitsIt() {
        // Arrange: observed nop, speculative nop, observed nop, speculative nop / hlt.
        TestInstructionHelper helper = new();
        CfgInstruction entry = Parse(helper, 0x0000, 0x90);
        CfgInstruction specOne = Parse(helper, 0x0001, 0x90);
        CfgInstruction observed = Parse(helper, 0x0002, 0x90);
        CfgInstruction specTwo = Parse(helper, 0x0003, 0x90);
        CfgInstruction hlt = Parse(helper, 0x0004, 0xF4);
        Speculative(specOne, specTwo, hlt);
        CfgBlock block = Linear(entry, specOne, observed, specTwo, hlt);

        // Act
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert: no merge across the observed instruction; the trailing speculative pair merges.
        AssertCompiles(program);
        GuardLines(program, "VerifySpeculativeEntryOrFail(").Should().ContainSingle(
            line => line.StartsWith("VerifySpeculativeEntryOrFail(biosSegment, 0x0001,"),
            "the first speculative instruction keeps its own guard across the observed instruction");
    }
}
