namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Pins the overload-binding table generated memory-offset expressions rely on:
/// integer constants keep binding the ushort and uint indexers while int-typed
/// compound expressions bind a wrapping int indexer that truncates to 16 bits
/// before the MMU access check.
/// </summary>
public class MemoryIndexerOverloadResolutionTest {
    private const ushort Segment = 0x1234;
    private readonly Memory _memory = new(new(), new Ram(0x200000), new A20Gate(), new RealModeMmu386(), false);
    private readonly State _state = new(CpuModel.INTEL_80286);

    public MemoryIndexerOverloadResolutionTest() {
        _state.SS = 0x2000;
    }

    private sealed class RecordingUInt8Indexer : MemoryIndexer<byte> {
        public RecordingUInt8Indexer(IMmu mmu) : base(mmu, 1) {
        }

        public string BoundOverload { get; private set; } = "none";

        public override byte this[uint address] {
            get => 0;
            set { }
        }

        public override byte this[ushort segment, ushort offset] {
            get {
                BoundOverload = "ushort";
                return base[segment, offset];
            }
            set {
                BoundOverload = "ushort";
                base[segment, offset] = value;
            }
        }

        public override byte this[ushort segment, uint offset, SegmentAccessKind accessKind] {
            get {
                if (BoundOverload == "none") {
                    BoundOverload = "uint";
                }
                return base[segment, offset, accessKind];
            }
            set {
                if (BoundOverload == "none") {
                    BoundOverload = "uint";
                }
                base[segment, offset, accessKind] = value;
            }
        }

        internal override byte ReadSegmented(ushort segment, uint offset) => 0;

        internal override void WriteSegmented(ushort segment, uint offset, byte value) {
        }

        public override int Count => 0;
    }

    [Fact]
    public void UInt8_ConstantFive_BindsTheUshortOverload() {
        RecordingUInt8Indexer indexer = new(new RealModeMmu386());

        _ = indexer[Segment, 5];

        Assert.Equal("ushort", indexer.BoundOverload);
    }

    [Fact]
    public void UInt8_ConstantAbove64KiB_BindsTheUintOverloadAndFaults() {
        RecordingUInt8Indexer indexer = new(new RealModeMmu386());

        Assert.Throws<CpuGeneralProtectionFaultException>(() => _ = indexer[Segment, 0x10000]);
        Assert.Equal("uint", indexer.BoundOverload);
    }

    [Fact]
    public void UInt8_IntExpression_BindsTheWrappingIntOverload() {
        RecordingUInt8Indexer indexer = new(new RealModeMmu386());
        ushort bx = 0xFFF0;

        _ = indexer[Segment, bx + 0x60];

        Assert.Equal("ushort", indexer.BoundOverload);
    }

    [Fact]
    public void UInt8_NegativeIntConstant_BindsTheWrappingIntOverload() {
        RecordingUInt8Indexer indexer = new(new RealModeMmu386());

        _ = indexer[Segment, -4];

        Assert.Equal("ushort", indexer.BoundOverload);
    }

    [Fact]
    public void UInt8_UintExpression_BindsTheUintOverloadAndDoesNotWrap() {
        RecordingUInt8Indexer indexer = new(new RealModeMmu386());
        uint ebx = 0xFFFC;

        Assert.Throws<CpuGeneralProtectionFaultException>(() => _ = indexer[Segment, ebx + 4]);
        Assert.Equal("uint", indexer.BoundOverload);
    }

    [Fact]
    public void UInt8_IntExpression_WrapsToSixteenBitsBeforeTheAccessCheck() {
        ushort bx = 0xFFF0;
        _memory.UInt8[Segment, (ushort)0x50] = 0xAB;

        byte value = _memory.UInt8[Segment, bx + 0x60];

        Assert.Equal(0xAB, value);
    }

    [Fact]
    public void UInt8_NegativeIntConstant_WrapsToSixteenBits() {
        _memory.UInt8[Segment, (ushort)0xFFFC] = 0xCD;

        byte value = _memory.UInt8[Segment, -4];

        Assert.Equal(0xCD, value);
    }

    [Fact]
    public void UInt8_ConstantAbove64KiB_FaultsWithAGeneralProtectionFault() {
        Assert.Throws<CpuGeneralProtectionFaultException>(() => _ = _memory.UInt8[Segment, 0x10000]);
    }

    [Fact]
    public void SsUInt8_IntExpression_WrapsToSixteenBits() {
        SsIndexer<byte> ssIndexer = new(_memory.UInt8, _state.SegmentRegisters);
        _memory.UInt8[_state.SS, (ushort)0x50] = 0xEF;
        ushort bx = 0xFFF0;

        byte value = ssIndexer[bx + 0x60];

        Assert.Equal(0xEF, value);
    }

    [Fact]
    public void SsUInt8_ConstantAbove64KiB_FaultsWithAStackSegmentFault() {
        SsIndexer<byte> ssIndexer = new(_memory.UInt8, _state.SegmentRegisters);

        Assert.Throws<CpuStackSegmentFaultException>(() => _ = ssIndexer[0x10000]);
    }

    [Fact]
    public void SsUInt8_NegativeIntConstant_WrapsToSixteenBits() {
        SsIndexer<byte> ssIndexer = new(_memory.UInt8, _state.SegmentRegisters);
        _memory.UInt8[_state.SS, (ushort)0xFFFC] = 0x77;

        byte value = ssIndexer[-4];

        Assert.Equal(0x77, value);
    }

    [Fact]
    public void SegmentedAddress32_IntExpression_WrapsToSixteenBits() {
        SegmentedAddress32Indexer indexer = _memory.SegmentedAddress32;
        indexer[Segment, (ushort)0x50] = new SegmentedAddress32(0xBEEF, 0xCAFE);
        ushort bx = 0xFFF0;

        SegmentedAddress32 value = indexer[Segment, bx + 0x60];

        Assert.Equal(0xCAFEu, value.Offset);
        Assert.Equal(0xBEEF, value.Segment);
    }

    [Fact]
    public void SegmentedAddress32_UintExpression_DoesNotWrap() {
        SegmentedAddress32Indexer indexer = _memory.SegmentedAddress32;
        uint ebx = 0xFFFC;

        Assert.Throws<CpuGeneralProtectionFaultException>(() => _ = indexer[Segment, ebx + 4]);
    }
}
