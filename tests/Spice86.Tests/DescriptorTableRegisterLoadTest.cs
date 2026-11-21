namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.DescriptorTables;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

using Xunit;

public class DescriptorTableRegisterLoadTest {
    private const ushort Segment = 0x017D;
    private const uint Offset = 0x08D0;

    [Theory]
    [InlineData(false, 0x00001890u)]
    [InlineData(true, 0x93001890u)]
    public void LoadIdtrShouldKeepOnly24BaseBitsWithA16BitOperand(bool operandSize32, uint expectedBase) {
        // Arrange
        (State state, IMemory memory) = CreatePseudoDescriptor();

        // Act
        SegmentAndControlRegisterOperations.LoadIdtr(state, memory, Segment, Offset, operandSize32);

        // Assert
        Assert.Equal(expectedBase, state.Idtr.Base);
        Assert.Equal((ushort)0x07FF, state.Idtr.Limit);
    }

    [Theory]
    [InlineData(false, 0x00001890u)]
    [InlineData(true, 0x93001890u)]
    public void LoadGdtrShouldKeepOnly24BaseBitsWithA16BitOperand(bool operandSize32, uint expectedBase) {
        // Arrange
        (State state, IMemory memory) = CreatePseudoDescriptor();

        // Act
        SegmentAndControlRegisterOperations.LoadGdtr(state, memory, Segment, Offset, operandSize32);

        // Assert
        Assert.Equal(expectedBase, state.Gdtr.Base);
    }

    private static (State, IMemory) CreatePseudoDescriptor() {
        State state = new(CpuModel.INTEL_80386);
        IMemory memory = new Memory(new(), new Ram(0x100000), new A20Gate(), new RealModeMmu386(), false);
        memory.UInt16[Segment, Offset] = 0x07FF;
        memory.UInt32[Segment, Offset + 2] = 0x93001890;
        return (state, memory);
    }
}
