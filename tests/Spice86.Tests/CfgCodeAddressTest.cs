namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Shared.Emulator.Memory;

using Xunit;

public class CfgCodeAddressTest {
    [Fact]
    public void SegmentedAndLinearConstruction_WithSameLinearValue_AreEqual() {
        CfgCodeAddress fromSegmented = new SegmentedAddress(0x1000, 0x0050);
        CfgCodeAddress fromLinear = new(0x1000u * 16 + 0x50);

        fromSegmented.Should().Be(fromLinear);
        fromSegmented.GetHashCode().Should().Be(fromLinear.GetHashCode());
        fromSegmented.CompareTo(fromLinear).Should().Be(0);
    }

    [Fact]
    public void DifferentLinearValues_AreNotEqual() {
        CfgCodeAddress a = new(0x1000u);
        CfgCodeAddress b = new(0x2000u);

        a.Should().NotBe(b);
        a.CompareTo(b).Should().BeLessThan(0);
    }

    [Fact]
    public void LinearOnlyAddress_HasNoSegmentedAddress() {
        CfgCodeAddress linearOnly = new(0x1000u);

        linearOnly.SegmentedAddress.Should().BeNull();
        linearOnly.Linear.Should().Be(0x1000u);
    }
}
