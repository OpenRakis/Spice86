namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using Xunit;

/// <summary>
/// Equivalence proof for the globals accessors: the generated code's segmented indexer path
/// (<c>memory.UInt16[DS, offset]</c>, MMU access check plus per-byte translation) against the
/// <see cref="MemoryBasedDataStructureWithDsBaseAddress"/> indexer path (linear
/// <c>base + offset</c>, no access check). The two differ where the segmented path faults: a word
/// or dword access whose last byte would cross the 64KB segment limit throws a
/// <see cref="CpuGeneralProtectionFaultException"/> under the real-mode 386 MMU, while the
/// accessor's linear path silently reads or writes above it.
/// </summary>
public sealed class GlobalsAccessorEquivalenceTest {
    private const uint RamSize = 0x200000;

    private static (Memory Memory, MemoryBasedDataStructureWithDsBaseAddress Accessor) Fixture(
        ushort ds, bool a20Enabled) {
        A20Gate a20Gate = new() { IsEnabled = a20Enabled };
        Memory memory = new(new AddressReadWriteBreakpoints(), new Ram(RamSize), a20Gate,
            new RealModeMmu386(), false);
        SegmentRegisters segmentRegisters = new();
        segmentRegisters.UInt16[(uint)SegmentRegisterIndex.DsIndex] = ds;
        return (memory, new MemoryBasedDataStructureWithDsBaseAddress(memory, segmentRegisters));
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

    private static (string? Exception, uint Value) WriteIndexerReadAccessor(ushort ds, bool a20,
        uint offset, int width) {
        (Memory memory, MemoryBasedDataStructureWithDsBaseAddress accessor) = Fixture(ds, a20);
        uint value = 0;
        string? exception = Thrown(() => {
            WriteIndexer(memory, ds, offset, width);
            value = ReadAccessor(accessor, offset, width);
        });
        return (exception, value);
    }

    private static (string? Exception, uint Value) WriteAccessorReadIndexer(ushort ds, bool a20,
        uint offset, int width) {
        (Memory memory, MemoryBasedDataStructureWithDsBaseAddress accessor) = Fixture(ds, a20);
        uint value = 0;
        string? exception = Thrown(() => {
            WriteAccessor(accessor, offset, width);
            value = ReadIndexer(memory, ds, offset, width);
        });
        return (exception, value);
    }

    private static void WriteIndexer(Memory memory, ushort ds, uint offset, int width) {
        switch (width) {
            case 1: memory.UInt8[ds, offset] = 0xAB; break;
            case 2: memory.UInt16[ds, offset] = 0xABCD; break;
            default: memory.UInt32[ds, offset] = 0x89ABCDEF; break;
        }
    }

    private static uint ReadIndexer(Memory memory, ushort ds, uint offset, int width) => width switch {
        1 => memory.UInt8[ds, offset],
        2 => memory.UInt16[ds, offset],
        _ => memory.UInt32[ds, offset]
    };

    private static void WriteAccessor(MemoryBasedDataStructureWithDsBaseAddress accessor, uint offset,
        int width) {
        switch (width) {
            case 1: accessor.UInt8[offset] = 0xAB; break;
            case 2: accessor.UInt16[offset] = 0xABCD; break;
            default: accessor.UInt32[offset] = 0x89ABCDEF; break;
        }
    }

    private static uint ReadAccessor(MemoryBasedDataStructureWithDsBaseAddress accessor, uint offset,
        int width) => width switch {
        1 => accessor.UInt8[offset],
        2 => accessor.UInt16[offset],
        _ => accessor.UInt32[offset]
    };

    private static uint ExpectedValue(int width) => width switch {
        1 => 0xABu,
        2 => 0xABCDu,
        _ => 0x89ABCDEFu
    };

    [Theory]
    [InlineData(1, 0x0000)]
    [InlineData(1, 0x0004)]
    [InlineData(1, 0xFFFE)]
    [InlineData(1, 0xFFFF)]
    [InlineData(2, 0x0000)]
    [InlineData(2, 0x0004)]
    [InlineData(2, 0xFFFE)]
    [InlineData(4, 0x0000)]
    [InlineData(4, 0x0004)]
    public void SameValueAndNoException_ForInSegmentAccesses(int width, uint offset) {
        // Arrange / Act / Assert
        foreach (ushort ds in new ushort[] { 0x2000, 0xFFFF }) {
            foreach (bool a20 in new[] { true, false }) {
                WriteIndexerReadAccessor(ds, a20, offset, width).Should()
                    .Be((null, ExpectedValue(width)), $"write indexer / read accessor, DS={ds:X4}, A20={a20}");
                WriteAccessorReadIndexer(ds, a20, offset, width).Should()
                    .Be((null, ExpectedValue(width)), $"write accessor / read indexer, DS={ds:X4}, A20={a20}");
            }
        }
    }

    [Theory]
    // The segmented indexer checks the access against the 64KB segment limit and throws a #GP
    // when it would cross it; the accessor's linear base+offset path never faults and reaches the
    // byte past the segment end.
    [InlineData(2, 0xFFFF)]
    [InlineData(4, 0xFFFE)]
    [InlineData(4, 0xFFFF)]
    public void IndexerFaultsWhereAccessorDoesNot_ForLimitCrossingAccesses(int width, uint offset) {
        // Arrange / Act / Assert
        foreach (ushort ds in new ushort[] { 0x2000, 0xFFFF }) {
            foreach (bool a20 in new[] { true, false }) {
                WriteIndexerReadAccessor(ds, a20, offset, width).Should()
                    .Be((nameof(CpuGeneralProtectionFaultException), 0u),
                        $"write indexer, DS={ds:X4}, A20={a20}: the segmented path faults");
                WriteAccessorReadIndexer(ds, a20, offset, width).Should()
                    .Be((nameof(CpuGeneralProtectionFaultException), 0u),
                        $"read indexer, DS={ds:X4}, A20={a20}: the accessor wrote past the limit, the segmented read faults");
            }
        }
    }
}
