namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The named tables: a bounded register index on a constant base prints through a table accessor of
/// the element width (<c>DsTables.Word0200[SI]</c>), one instance per table, while an unknown or
/// overflowing range keeps the memory indexer.
/// </summary>
public class TableAccessClassificationTest {
    private static CfgCodePartition PartitionOf(CfgBlock block) => new() {
        Id = 1,
        Kind = CfgCodePartitionKind.Observed,
        Name = "test",
        Blocks = [block],
        Entries = SingleEntry(block)
    };

    private static GeneratedCSharpProgram Generate(params (ushort Offset, byte[] Bytes)[] instructions) {
        CfgCodePartition partition = PartitionOf(Linear(new TestInstructionHelper(), instructions));
        CfgPartitionedProgram program = new() {
            Partitions = [partition],
            Transfers = [],
            Inputs = GeneratorInputs.Empty with {
                ProgramEntryPoint = partition.Entries[0].Address
            }
        };
        return new CfgCSharpGenerator(collectCensuses: true, verify: true).Generate(program);
    }

    [Fact]
    public void BoundedByteIndexPrintsThroughTheTableClass() {
        // Arrange: and bx,7 then a byte read at bx+0x0120.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0x83, 0xE3, 0x07]),       // and bx, 7
            (0x0003, [0x8A, 0x87, 0x20, 0x01]), // mov al, [bx+0x0120]
            (0x0007, [0xC3]));                // ret

        // Assert
        program.SourceText.Should().Contain("AL = DsTables.Byte0120[BX];");
        string tables = program.Files.Single(file => file.Name == "DsTables.cs").Text;
        tables.Should().Contain("public sealed class DsTables : MemoryBasedDataStructureWithDsBaseAddress");
        tables.Should().Contain("Byte0120 = new UInt8SegmentedTable(_memory, () => Segment, 0x0120, 8);");
        tables.Should().Contain("/// DS:0x0120, 8 elements of 1 byte, whatever DS holds at the access.");
        program.SourceText.Should().Contain("protected readonly DsTables DsTables;");
        program.SourceText.Should().Contain("DsTables = new DsTables(Memory, State.SegmentRegisters);");
        AssertCompiles(program);
    }

    [Fact]
    public void ShiftedWordIndexStaysAnIndexer() {
        // Arrange: si bounded to [0,3] on the fall-through, shifted into a word index at +0x0200.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0x83, 0xFE, 0x03]),       // cmp si, 3
            (0x0003, [0x77, 0x06]),           // ja +6 (over the read)
            (0x0005, [0xD1, 0xE6]),           // shl si, 1
            (0x0007, [0x8B, 0x84, 0x00, 0x02]), // mov ax, [si+0x0200]
            (0x000B, [0xC3]));                // ret

        // Assert: the pipeline keeps the shift as a separate statement, so the offset is
        // `SI + 0x0200` on the post-shift version: a word access of the R + c shape, which only a
        // byte element qualifies for. It stays an indexer.
        program.SourceText.Should().Contain("AX = UInt16[ds, (ushort)(SI + 0x0200)];");
        program.SourceText.Should().NotContain("DsTables");
        AssertCompiles(program);
    }

    [Fact]
    public void TwoAccessesShareOneTableInstance() {
        // Arrange: two byte reads of the same table.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0x83, 0xE3, 0x07]),       // and bx, 7
            (0x0003, [0x8A, 0x87, 0x20, 0x01]), // mov al, [bx+0x0120]
            (0x0007, [0x8A, 0x8F, 0x20, 0x01]), // mov cl, [bx+0x0120]
            (0x000B, [0xC3]));                // ret

        // Assert: one property, one instance, two element accesses.
        string tables = program.Files.Single(file => file.Name == "DsTables.cs").Text;
        tables.Split("new UInt8SegmentedTable(").Length.Should().Be(2);
        program.SourceText.Should().Contain("AL = DsTables.Byte0120[BX];");
        program.SourceText.Should().Contain("CL = DsTables.Byte0120[BX];");
        AssertCompiles(program);
    }

    [Fact]
    public void UnknownIndexRangeStaysAnIndexer() {
        // Arrange: no bound on bx.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0x8A, 0x87, 0x20, 0x01]), // mov al, [bx+0x0120]
            (0x0004, [0xC3]));                // ret

        // Assert
        program.SourceText.Should().Contain("UInt8[ds, (ushort)(bx + 0x0120)]");
        program.SourceText.Should().NotContain("Tables");
        AssertCompiles(program);
    }

    [Fact]
    public void RangeWhoseLastElementCrossesTheSegmentEndStaysAnIndexer() {
        // Arrange: bx in [0,7] but 0xFFFC + 7 > 0xFFFF.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0x83, 0xE3, 0x07]),       // and bx, 7
            (0x0003, [0x8A, 0x87, 0xFC, 0xFF]), // mov al, [bx+0xFFFC]
            (0x0007, [0xC3]));                // ret

        // Assert: the 0xFFFC constant prints as the equivalent -4 displacement.
        program.SourceText.Should().Contain("UInt8[ds, (ushort)(BX - 4)]");
        program.SourceText.Should().NotContain("DsTables");
        AssertCompiles(program);
    }

    [Fact]
    public void StackSegmentIndexStaysAnIndexer() {
        // Arrange: a bounded bp index, an SS access.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0x83, 0xE5, 0x07]),       // and bp, 7
            (0x0003, [0x8A, 0x86, 0x20, 0x01]), // mov al, [bp+0x0120]
            (0x0007, [0xC3]));                // ret

        // Assert
        program.SourceText.Should().Contain("SsUInt8[(ushort)(BP + 0x0120)]");
        program.SourceText.Should().NotContain("Tables");
        AssertCompiles(program);
    }

    [Fact]
    public void TableContainingAGlobalDocumentsBothDirections() {
        // Arrange: a byte table of two elements at 0x0020 and a global byte at 0x0020.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0x83, 0xE3, 0x01]),       // and bx, 1
            (0x0003, [0x8A, 0x87, 0x20, 0x00]), // mov al, [bx+0x0020]
            (0x0007, [0x8A, 0x0E, 0x20, 0x00]), // mov cl, [0x0020]
            (0x000B, [0xC3]));                // ret

        // Assert
        program.Files.Single(file => file.Name == "DsTables.cs").Text.Should()
            .Contain("/// Contains DsGlobals.Byte0020 (element 0).");
        program.Files.Single(file => file.Name == "DsGlobals.cs").Text.Should()
            .Contain("/// element 0 of DsTables.Byte0020.");
        AssertCompiles(program);
    }
}
