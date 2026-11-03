namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

using Xunit;

public class CpuMmuSegmentWriteTest {
    [Fact]
    public void DirectCsWriteInRealModeShouldTranslateThroughNewSegmentBase() {
        // Arrange
        State state = new(CpuModel.INTEL_80386);
        IMmu mmu = CpuMmuFactory.Create(CpuModel.INTEL_80386, state, new Ram(A20Gate.EndOfHighMemoryArea));

        // Act
        state.CS = 0x033A;
        uint linearAddress = mmu.TranslateAddress(0x033A, 0x2372, isWrite: false);

        // Assert
        Assert.Equal(0x33A0u + 0x2372u, linearAddress);
    }

    [Fact]
    public void CplShouldStayZeroAfterSettingPeUntilCsIsReloaded() {
        // Arrange
        State state = new(CpuModel.INTEL_80386);
        state.CS = 0x03B5;

        // Act
        state.ControlRegisters.Cr0 |= 1;

        // Assert
        Assert.Equal(CpuMode.Protected, state.CpuMode);
        Assert.Equal(0, state.Cpl);
    }
}
