namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Memory.Mmu;

using Xunit;

public class RealModeMmuTest {
    [Fact]
    public void StrictDataByteAtSegmentLimitShouldSucceed() {
        // Arrange
        IMmu mmu = new RealModeMmu386();

        // Act & Assert
        mmu.CheckAccess(0, 0xFFFF, 1, SegmentAccessKind.Data);
    }

    [Fact]
    public void StrictDataWordAtSegmentLimitShouldRaiseGeneralProtectionFault() {
        // Arrange
        IMmu mmu = new RealModeMmu386();

        // Act & Assert
        Assert.Throws<CpuGeneralProtectionFaultException>(() => mmu.CheckAccess(0, 0xFFFF, 2, SegmentAccessKind.Data));
    }

    [Fact]
    public void StrictStackWordAtSegmentLimitShouldRaiseStackSegmentFault() {
        // Arrange
        IMmu mmu = new RealModeMmu386();

        // Act & Assert
        Assert.Throws<CpuStackSegmentFaultException>(() => mmu.CheckAccess(0, 0xFFFF, 2, SegmentAccessKind.Stack));
    }

    [Theory]
    [InlineData(0xFFFDu)]
    [InlineData(0x10000u)]
    public void StrictDataDwordOutsideSegmentShouldRaiseGeneralProtectionFault(uint offset) {
        // Arrange
        IMmu mmu = new RealModeMmu386();

        // Act & Assert
        Assert.Throws<CpuGeneralProtectionFaultException>(() => mmu.CheckAccess(0, offset, 4, SegmentAccessKind.Data));
    }

    [Fact]
    public void WrapPolicyShouldTranslateOffsetPastSegmentLimitLikeOffsetZero() {
        // Arrange
        IMmu mmu = new RealModeMmu8086();

        // Act
        uint wrappedAddress = mmu.TranslateAddress(0x1234, 0x10000);
        uint zeroAddress = mmu.TranslateAddress(0x1234, 0);

        // Assert
        Assert.Equal(zeroAddress, wrappedAddress);
    }

    [Fact]
    public void WrapPolicyShouldAllowCrossBoundaryAccess() {
        // Arrange
        IMmu mmu = new RealModeMmu8086();

        // Act & Assert
        mmu.CheckAccess(0, 0xFFFF, 4, SegmentAccessKind.Data);
    }

    [Fact]
    public void StrictCodeNextIpWithinSegmentLimitShouldSucceed() {
        // Arrange
        IMmu mmu = new RealModeMmu386();

        // Act & Assert — 1-byte instruction at 0xFFFE: next IP = 0xFFFF, within segment
        mmu.CheckAccess(0, 0xFFFF, 1, SegmentAccessKind.Data);
    }

    [Fact]
    public void StrictCodeNextIpPastSegmentLimitShouldRaiseGeneralProtectionFault() {
        // Arrange
        IMmu mmu = new RealModeMmu386();

        // Act & Assert — 1-byte instruction at 0xFFFF: next IP = 0x10000, overflows segment
        Assert.Throws<CpuGeneralProtectionFaultException>(() => mmu.CheckAccess(0, 0x10000u, 1, SegmentAccessKind.Data));
    }

    [Fact]
    public void WrapPolicyShouldAllowCodeCrossBoundaryAccess() {
        // Arrange
        IMmu mmu = new RealModeMmu8086();

        // Act & Assert — 2-byte instruction at 0xFFFF: next IP = 0x10001, wraps on 8086
        mmu.CheckAccess(0, 0x10001u, 1, SegmentAccessKind.Data);
    }
}