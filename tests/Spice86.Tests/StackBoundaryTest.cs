namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class StackBoundaryTest {
    [Fact]
    public void StrictPop16CrossingSegmentLimitShouldRaiseStackSegmentFault() {
        // Arrange
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 0xFFFF
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);

        // Act & Assert
        Assert.Throws<CpuStackSegmentFaultException>(() => stack.Pop16());
    }

    [Fact]
    public void StrictPushadPreValidationShouldFaultWithNoSideEffects() {
        // Arrange: SP=3 means a 32-bit push would wrap across segment limit
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 3
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);

        // Write sentinel values at the wrap-around area
        memory.UInt8[0xFFFF] = 0xAA;
        memory.UInt8[0x0000] = 0xBB;
        memory.UInt8[0x0001] = 0xCC;
        memory.UInt8[0x0002] = 0xDD;

        // Act & Assert: pre-validation should fault before any writes
        Assert.Throws<CpuStackSegmentFaultException>(() => stack.ValidateStackPushRange(4, 1));

        // SP unchanged, memory unchanged
        Assert.Equal(3, state.SP);
        Assert.Equal(0xAA, memory.UInt8[0xFFFF]);
        Assert.Equal(0xBB, memory.UInt8[0x0000]);
        Assert.Equal(0xCC, memory.UInt8[0x0001]);
        Assert.Equal(0xDD, memory.UInt8[0x0002]);
    }

    [Fact]
    public void StrictPopadPreValidationShouldFaultWithNoSideEffects() {
        // Arrange: 8 dword slots from 0xFFE9 = 32 bytes, last slot at 0x0001 crosses segment limit
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            ESP = 0x1111FFE9
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);

        // Act & Assert: pre-validation should fault before any registers are modified
        Assert.Throws<CpuStackSegmentFaultException>(() => stack.ValidateStackPopRange(4, 8));

        // SP and ESP unchanged (no registers modified)
        Assert.Equal((ushort)0xFFE9, state.SP);
        Assert.Equal(0x1111FFE9u, state.ESP);
    }

    [Fact]
    public void StrictPush16FaultShouldLeaveStackPointerUnchanged() {
        // Arrange
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 1
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);

        // Act & Assert
        Assert.Throws<CpuStackSegmentFaultException>(() => stack.Push16(0x1234));
        Assert.Equal(1, state.SP);
    }

    [Fact]
    public void StrictPush32FaultShouldLeaveStackPointerUnchanged() {
        // Arrange
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 3
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);

        // Act & Assert
        Assert.Throws<CpuStackSegmentFaultException>(() => stack.Push32(0x12345678));
        Assert.Equal(3, state.SP);
    }

    [Fact]
    public void StrictCompositeStackWriteFaultShouldNotPartiallyWrite() {
        // Arrange
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 0xFFFD
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);
        memory.UInt8[0xFFFD] = 0xAA;
        memory.UInt8[0xFFFE] = 0xBB;

        // Act & Assert
        Assert.Throws<CpuStackSegmentFaultException>(() => stack.PokeSegmentedAddress(0, new SegmentedAddress(0x1234, 0x5678)));
        Assert.Equal(0xAA, memory.UInt8[0xFFFD]);
        Assert.Equal(0xBB, memory.UInt8[0xFFFE]);
    }

    [Fact]
    public void StrictFarPointer32PopShouldReadOffsetAndSegment() {
        // Arrange: SP=0xFFF8, 6-byte read fits within segment limit
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 0xFFF8
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);
        WriteFarPointer32(memory, 0xFFF8, 0x1234, 0xABCD);

        // Act
        SegmentedAddress32 address = stack.PopSegmentedAddress32();

        // Assert
        Assert.Equal(0xABCD, address.Segment);
        Assert.Equal(0x1234u, address.Offset);
        Assert.Equal(0x0000, state.SP);
    }

    [Fact]
    public void StrictFarPointer32PopShouldWrapWhenPopsCrossSegmentBoundary() {
        // Arrange: SP=0xFFFC. First 4-byte pop (EIP) ends at FFFF, second 4-byte pop
        // (padded CS) wraps to offset 0x0000. Hardware wraps between individual pops.
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 0xFFFC
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);
        WriteFarPointer32(memory, 0xFFFC, 0x1234, 0xABCD);

        // Act
        SegmentedAddress32 address = stack.PopSegmentedAddress32();

        // Assert
        Assert.Equal(0xABCD, address.Segment);
        Assert.Equal(0x1234u, address.Offset);
        Assert.Equal(0x0004, state.SP);
    }

    [Fact]
    public void StrictFarPointer32PopShouldFaultWhenFirstPopCrossesSegmentLimit() {
        // Arrange: SP=0xFFFD, first 4-byte pop (EIP) at FFFD crosses segment limit.
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 0xFFFD
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);
        WriteFarPointer32(memory, 0xFFFD, 0x1234, 0xABCD);

        // Act & Assert
        Assert.Throws<CpuStackSegmentFaultException>(() => stack.PopSegmentedAddress32());
    }

    [Fact]
    public void StrictFarPointer32PushShouldRaiseStackSegmentFaultWhenFrameCrossesLimit() {
        // SP=5: newSp=(ushort)(5-8)=0xFFFD; 8-byte frame at 0xFFFD..0x10004 crosses the limit.
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 5
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);

        Assert.Throws<CpuStackSegmentFaultException>(() => stack.PushFarPointer32(new SegmentedAddress32(0xABCD, 0x1234)));
    }

    [Fact]
    public void StrictFarPointer32PopShouldRaiseStackSegmentFaultWhenSegmentSlotCrossesLimit() {
        // Arrange
        State state = new(CpuModel.INTEL_80286) {
            SS = 0,
            SP = 0xFFFB
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);
        WriteFarPointer32(memory, state.SP, 0x1234, 0xABCD);

        // Act & Assert
        Assert.Throws<CpuStackSegmentFaultException>(() => stack.PopSegmentedAddress32());
    }

    [Fact]
    public void WrappingInterruptPointer32ReadShouldWrapStackSlots() {
        // Arrange: 8086 MMU wraps; interrupt pointer at SP=0xFFFC crosses boundary
        State state = new(CpuModel.INTEL_80386) {
            SS = 0,
            SP = 0xFFFC
        };
        Memory memory = CreateMemory(new RealModeMmu8086());
        Stack stack = new(memory, state);
        WriteUInt32(memory, 0xFFFC, 0x00001234);
        WriteUInt32(memory, 0x0000, 0xFFFFABCD);
        WriteUInt32(memory, 0x0004, 0x00000202);

        // Act
        SegmentedAddress address = stack.PopInterruptPointer32();
        uint flags = stack.Pop32();

        // Assert
        Assert.Equal(0xABCD, address.Segment);
        Assert.Equal(0x1234, address.Offset);
        Assert.Equal(0x00000202u, flags);
        Assert.Equal(0x0008, state.SP);
    }

    [Fact]
    public void StrictInterruptPointer32ReadShouldWrapWhenPopsCrossSegmentBoundary() {
        // Arrange: SP=0xFFFC. First 4-byte pop (EIP) ends at FFFF, second 4-byte pop
        // (padded CS) wraps to offset 0x0000. Hardware wraps between individual pops.
        State state = new(CpuModel.INTEL_80386) {
            SS = 0,
            SP = 0xFFFC
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);
        WriteUInt32(memory, 0xFFFC, 0x00001234);
        WriteUInt32(memory, 0x0000, 0xFFFFABCD);

        // Act
        SegmentedAddress address = stack.PopInterruptPointer32();

        // Assert
        Assert.Equal(0xABCD, address.Segment);
        Assert.Equal(0x1234, address.Offset);
        Assert.Equal(0x0004, state.SP);
    }

    [Fact]
    public void StrictInterruptPointer32ReadShouldFaultWhenFirstPopCrossesSegmentLimit() {
        // Arrange: SP=0xFFFD, first 4-byte pop at FFFD crosses segment limit
        State state = new(CpuModel.INTEL_80386) {
            SS = 0,
            SP = 0xFFFD
        };
        Memory memory = CreateMemory(new RealModeMmu386());
        Stack stack = new(memory, state);

        // Act & Assert
        Assert.Throws<CpuStackSegmentFaultException>(() => stack.PopInterruptPointer32());
    }

    private static Memory CreateMemory(IMmu mmu) {
        return new Memory(new(), new Ram(0x20000), new A20Gate(), mmu, false);
    }

    private static void WriteUInt32(Memory memory, ushort offset, uint value) {
        memory.UInt8[offset] = (byte)value;
        memory.UInt8[(uint)(ushort)(offset + 1)] = (byte)(value >> 8);
        memory.UInt8[(uint)(ushort)(offset + 2)] = (byte)(value >> 16);
        memory.UInt8[(uint)(ushort)(offset + 3)] = (byte)(value >> 24);
    }

    private static void WriteFarPointer32(Memory memory, ushort stackPointer, uint offset, ushort segment) {
        memory.UInt8[stackPointer] = (byte)offset;
        memory.UInt8[(uint)(ushort)(stackPointer + 1)] = (byte)(offset >> 8);
        memory.UInt8[(uint)(ushort)(stackPointer + 2)] = (byte)(offset >> 16);
        memory.UInt8[(uint)(ushort)(stackPointer + 3)] = (byte)(offset >> 24);
        memory.UInt8[(uint)(ushort)(stackPointer + 4)] = (byte)segment;
        memory.UInt8[(uint)(ushort)(stackPointer + 5)] = (byte)(segment >> 8);
    }
}