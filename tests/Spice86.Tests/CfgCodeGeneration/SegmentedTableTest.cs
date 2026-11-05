namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

using Xunit;

/// <summary>
/// The segmented-table accessors compute exactly the access the printed indexer computes:
/// <c>table[i]</c> is <c>memory.UInt*[segment(), (ushort)(base + index * width)]</c> - same segment
/// resolution, same 16-bit wrap, same MMU fault at the segment end, whether the segment comes from
/// a register-read function or from a constant like a location-class table's.
/// </summary>
public sealed class SegmentedTableTest {
    private const uint RamSize = 0x200000;

    private static (Memory Memory, Func<ushort> Segment) Fixture(ushort ds, bool a20Enabled) {
        A20Gate a20Gate = new() { IsEnabled = a20Enabled };
        Memory memory = new(new AddressReadWriteBreakpoints(), new Ram(RamSize), a20Gate,
            new RealModeMmu386(), false);
        SegmentRegisters segmentRegisters = new();
        segmentRegisters.UInt16[(uint)SegmentRegisterIndex.DsIndex] = ds;
        return (memory, () => segmentRegisters.UInt16[(uint)SegmentRegisterIndex.DsIndex]);
    }

    private static (Memory Memory, Func<ushort> Segment) LocationFixture(ushort ds, bool a20Enabled) {
        (Memory memory, _) = Fixture(ds, a20Enabled);
        return (memory, () => ds);
    }

    /// <summary>Runs <paramref name="action"/>; returns the exception name or null.</summary>
    private static string? Thrown(Action action) {
        try {
            action();
            return null;
        } catch (CpuGeneralProtectionFaultException e) {
            return e.GetType().Name;
        }
    }

    private static object TableOf(IMemory memory, Func<ushort> segment, int width, ushort baseOffset,
        int length) => width switch {
        1 => new UInt8SegmentedTable(memory, segment, baseOffset, length),
        2 => new UInt16SegmentedTable(memory, segment, baseOffset, length),
        _ => new UInt32SegmentedTable(memory, segment, baseOffset, length)
    };

    private static ushort Offset(ushort baseOffset, int index, int width) =>
        (ushort)(baseOffset + index * width);

    private static uint ReadTable(object table, int index) => table switch {
        UInt8SegmentedTable byteTable => byteTable[index],
        UInt16SegmentedTable wordTable => wordTable[index],
        UInt32SegmentedTable dwordTable => dwordTable[index],
        _ => throw new NotSupportedException()
    };

    private static void WriteTable(object table, int index, uint value) {
        switch (table) {
            case UInt8SegmentedTable byteTable: byteTable[index] = (byte)value; break;
            case UInt16SegmentedTable wordTable: wordTable[index] = (ushort)value; break;
            case UInt32SegmentedTable dwordTable: dwordTable[index] = value; break;
        }
    }

    private static uint ReadIndexer(IMemory memory, Func<ushort> segment, ushort offset, int width) =>
        width switch {
            1 => memory.UInt8[segment(), offset],
            2 => memory.UInt16[segment(), offset],
            _ => memory.UInt32[segment(), offset]
        };

    private static void WriteIndexer(IMemory memory, Func<ushort> segment, ushort offset, int width,
        uint value) {
        switch (width) {
            case 1: memory.UInt8[segment(), offset] = (byte)value; break;
            case 2: memory.UInt16[segment(), offset] = (ushort)value; break;
            default: memory.UInt32[segment(), offset] = value; break;
        }
    }

    /// <summary>Writes through the table and reads through the indexer, writes through the indexer
    /// and reads through the table, in two fresh fixtures; both directions must agree on the
    /// exception and on the value.</summary>
    private static void Equivalent(ushort ds, bool a20, int width, ushort baseOffset, int index,
        bool location) {
        (Memory Memory, Func<ushort> Segment) fixture = location ? LocationFixture(ds, a20) : Fixture(ds, a20);
        object table = TableOf(fixture.Memory, fixture.Segment, width, baseOffset, index + 1);
        ushort offset = Offset(baseOffset, index, width);
        uint value = 0;
        string? writeException = Thrown(() => WriteTable(table, index, 0xA5A5A5A5u));
        string? readException = Thrown(() => value = ReadIndexer(fixture.Memory, fixture.Segment,
            offset, width));
        (string? Write, string? Read, uint Value) viaTable = (writeException, readException,
            writeException is null ? value & Mask(width) : value);

        (Memory Memory2, Func<ushort> Segment2) = location ? LocationFixture(ds, a20) : Fixture(ds, a20);
        object table2 = TableOf(Memory2, Segment2, width, baseOffset, index + 1);
        uint value2 = 0;
        string? writeException2 = Thrown(() => WriteIndexer(Memory2, Segment2, offset, width,
            0xA5A5A5A5u));
        string? readException2 = Thrown(() => value2 = ReadTable(table2, index));

        (writeException2, readException2, value2).Should()
            .Be((viaTable.Write, viaTable.Read, viaTable.Value));
    }

    private static uint Mask(int width) => width switch {
        1 => 0xFFu,
        2 => 0xFFFFu,
        _ => 0xFFFFFFFFu
    };

    [Fact]
    public void ByteTable_InSegment_MatchesTheIndexer() {
        // Arrange / Act / Assert
        Equivalent(0x2000, true, 1, 0x0120, 0, false);
        Equivalent(0x2000, true, 1, 0x0120, 7, false);
        Equivalent(0x2000, false, 1, 0x0120, 7, false);
        Equivalent(0x0000, true, 1, 0x0000, 3, false);
    }

    [Fact]
    public void WordTable_InSegment_MatchesTheIndexer() {
        // Arrange / Act / Assert
        Equivalent(0x2000, true, 2, 0x0000, 4, false);
        Equivalent(0x2000, true, 2, 0x0120, 0x100, false);
        Equivalent(0xFFF0, true, 2, 0x0000, 2, false);
    }

    [Fact]
    public void DwordTable_InSegment_MatchesTheIndexer() {
        // Arrange / Act / Assert
        Equivalent(0x2000, true, 4, 0x0000, 1, false);
        Equivalent(0x2000, true, 4, 0x0120, 0x40, false);
        Equivalent(0x2000, false, 4, 0x0120, 0x40, false);
    }

    [Fact]
    public void IndexWrappingPastTheSegmentEnd_WrapsLikeTheIndexer() {
        // Arrange / Act / Assert
        // base 0xFFF0: index 0x20 gives byte offset 0x10010 -> (ushort)0x0010
        Equivalent(0x2000, true, 1, 0xFFF0, 0x20, false);
        // word index 8 gives 0xFFF0 + 16 = 0x10000 -> 0x0000
        Equivalent(0x2000, true, 2, 0xFFF0, 8, false);
        // dword index 4 gives 0xFFF0 + 16 -> 0x0000
        Equivalent(0x2000, true, 4, 0xFFF0, 4, false);
        // a word index 7 lands exactly on the last word of the segment: no fault
        Equivalent(0x2000, true, 2, 0xFFF0, 7, false);
        // a dword index 3 lands on 0xFFFC: the last dword of the segment, no fault
        Equivalent(0x2000, true, 4, 0xFFF0, 3, false);
    }

    [Fact]
    public void SegmentEnd_FaultsLikeTheIndexer() {
        // Arrange / Act / Assert
        // a word at 0xFFFF faults (base 0xFFF1, word index 7)
        Equivalent(0x2000, true, 2, 0xFFF1, 7, false);
        // a dword at 0xFFFE (base 0xFFF2, dword index 3) and at 0xFFFF (base 0xFFF3, index 3)
        Equivalent(0x2000, true, 4, 0xFFF2, 3, false);
        Equivalent(0x2000, true, 4, 0xFFF3, 3, false);
    }

    [Fact]
    public void WordAtSegmentEnd_ThrowsGeneralProtectionFault() {
        // Arrange / Act
        (Memory memory, Func<ushort> segment) = Fixture(0x2000, true);
        UInt16SegmentedTable table = new(memory, segment, 0xFFF1, 8);

        // Assert
        Action write = () => table[7] = 0xABCD;
        write.Should().Throw<CpuGeneralProtectionFaultException>();
        Action read = () => _ = table[7];
        read.Should().Throw<CpuGeneralProtectionFaultException>();
    }

    [Fact]
    public void HighSegment_A20OnAndOff_MatchesTheIndexer() {
        // Arrange / Act / Assert
        Equivalent(0xFFFF, true, 1, 0x0120, 5, false);
        Equivalent(0xFFFF, false, 1, 0x0120, 5, false);
        Equivalent(0xFFFF, true, 2, 0x0120, 5, false);
        Equivalent(0xFFFF, false, 2, 0x0120, 5, false);
        Equivalent(0xFFFF, true, 4, 0x0120, 5, false);
        Equivalent(0xFFFF, true, 1, 0xFFF0, 0x20, false);
    }

    [Fact]
    public void ConstantSegment_LocationClassTable_MatchesTheIndexer() {
        // Arrange / Act / Assert
        // A location-class table passes a constant segment function: same bytes, same faults.
        Equivalent(0x2000, true, 1, 0x0120, 3, true);
        Equivalent(0x2000, true, 2, 0xFFF0, 7, true);
        Equivalent(0x2000, true, 4, 0x0120, 0x40, true);
        Equivalent(0xFFFF, true, 2, 0x0120, 2, true);
        Equivalent(0x2000, true, 2, 0xFFF1, 7, true);
    }

    [Fact]
    public void Length_IsDocumentedOnly() {
        // Arrange / Act
        (Memory memory, Func<ushort> segment) = Fixture(0x2000, true);
        UInt8SegmentedTable table = new(memory, segment, 0x0120, 2);

        // Assert
        // Writing past the documented length does not throw and lands on the byte after it.
        table[4] = 0xA5;
        memory.UInt8[0x2000, 0x0124].Should().Be(0xA5);
        table.Length.Should().Be(2);
    }
}
