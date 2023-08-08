using System.Collections.Immutable;

using JetBrains.Annotations;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using Xunit;

namespace Spice86.Tests.CfgCpu;

public class InstructionDiscriminatorTest {
    private static readonly ImmutableList<byte?> DiscriminatorValue1 =
        ImmutableList.CreateRange(new byte?[] { 0x01, 0x2 });

    private static readonly ImmutableList<byte?> DiscriminatorValue1Same =
        ImmutableList.CreateRange(new byte?[] { 0x1, 0x2 });

    private static readonly ImmutableList<byte?> DiscriminatorValue2SameLengthAs1DifferentNumber =
        ImmutableList.CreateRange(new byte?[] { 0x1, 0x3 });

    private static readonly ImmutableList<byte?> DiscriminatorValue3SameNumberDifferentLength =
        ImmutableList.CreateRange(new byte?[] { 0x1 });

    private static readonly ImmutableList<byte?> DiscriminatorValue1WithNull =
        ImmutableList.CreateRange(new byte?[] { null, 0x2 });

    [Fact]
    public void DiscriminatorsEqualsSameValues() {
        // Arrange
        Discriminator d1 = new Discriminator(DiscriminatorValue1);
        Discriminator d2 = new Discriminator(DiscriminatorValue1Same);

        // Assert
        AssertEquals(d1, d2);
    }

    [Fact]
    public void DiscriminatorsEqualsWithNull() {
        // Arrange
        Discriminator d1 = new Discriminator(DiscriminatorValue1);
        Discriminator d2 = new Discriminator(DiscriminatorValue1WithNull);

        // Assert
        AssertEquals(d1, d2);
    }

    [Fact]
    public void DiscriminatorsNotEqualsWithDifferentLength() {
        // Arrange
        Discriminator d1 = new Discriminator(DiscriminatorValue1);
        Discriminator d2 = new Discriminator(DiscriminatorValue3SameNumberDifferentLength);

        // Assert
        AssertMore(d1, d2);
    }

    [Fact]
    public void DiscriminatorsNotEqualsWithDifferentValues() {
        // Arrange
        Discriminator d1 = new Discriminator(DiscriminatorValue1);
        Discriminator d2 = new Discriminator(DiscriminatorValue2SameLengthAs1DifferentNumber);

        // Assert
        AssertMore(d2, d1);
    }

    [Fact]
    public void DiscriminatorsNotEqualsWithNull() {
        // Arrange
        Discriminator d1 = new Discriminator(DiscriminatorValue1WithNull);
        Discriminator d2 = new Discriminator(DiscriminatorValue2SameLengthAs1DifferentNumber);

        // Assert
        AssertMore(d2, d1);
    }

    [AssertionMethod]
    private void AssertEquals(Discriminator d1, Discriminator d2) {
        Assert.Equal(d1, d2);
        Assert.True(d1.Equals(d2));
        Assert.True(d2.Equals(d1));
        Assert.Equal(0, d1.CompareTo(d2));
    }

    [AssertionMethod]
    private void AssertNotEquals(Discriminator d1, Discriminator d2) {
        Assert.NotEqual(d1, d2);
        Assert.False(d1.Equals(d2));
        Assert.False(d2.Equals(d1));
        Assert.NotEqual(0, d1.CompareTo(d2));
        Assert.NotEqual(0, d2.CompareTo(d1));
    }

    [AssertionMethod]
    private void AssertMore(Discriminator d1, Discriminator d2) {
        AssertNotEquals(d1, d2);
        Assert.Equal(1, d1.CompareTo(d2));
        Assert.Equal(-1, d2.CompareTo(d1));
    }
}