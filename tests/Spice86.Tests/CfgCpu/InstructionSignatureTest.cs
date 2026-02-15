using JetBrains.Annotations;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Collections.Immutable;

using Xunit;

namespace Spice86.Tests.CfgCpu;

public class InstructionSignatureTest {
    private static readonly ImmutableList<byte?> SignatureValue1 =
        ImmutableList.CreateRange(new byte?[] { 0x01, 0x2 });

    private static readonly ImmutableList<byte?> SignatureValue1Same =
        ImmutableList.CreateRange(new byte?[] { 0x1, 0x2 });

    private static readonly ImmutableList<byte?> SignatureValue2SameLengthAs1DifferentNumber =
        ImmutableList.CreateRange(new byte?[] { 0x1, 0x3 });

    private static readonly ImmutableList<byte?> SignatureValue3SameNumberDifferentLength =
        ImmutableList.CreateRange(new byte?[] { 0x1 });

    private static readonly ImmutableList<byte?> SignatureValue1WithNull =
        ImmutableList.CreateRange(new byte?[] { null, 0x2 });

    [Fact]
    public void SignaturesEqualsSameValues() {
        // Arrange
        Signature d1 = new Signature(SignatureValue1);
        Signature d2 = new Signature(SignatureValue1Same);

        // Assert
        AssertEquals(d1, d2);
    }

    [Fact]
    public void SignaturesEqualsWithNull() {
        // Arrange
        Signature d1 = new Signature(SignatureValue1);
        Signature d2 = new Signature(SignatureValue1WithNull);

        // Assert
        AssertEquals(d1, d2);
    }

    [Fact]
    public void SignaturesNotEqualsWithDifferentLength() {
        // Arrange
        Signature d1 = new Signature(SignatureValue1);
        Signature d2 = new Signature(SignatureValue3SameNumberDifferentLength);

        // Assert
        AssertMore(d1, d2);
    }

    [Fact]
    public void SignaturesNotEqualsWithDifferentValues() {
        // Arrange
        Signature d1 = new Signature(SignatureValue1);
        Signature d2 = new Signature(SignatureValue2SameLengthAs1DifferentNumber);

        // Assert
        AssertMore(d2, d1);
    }

    [Fact]
    public void SignaturesNotEqualsWithNull() {
        // Arrange
        Signature d1 = new Signature(SignatureValue1WithNull);
        Signature d2 = new Signature(SignatureValue2SameLengthAs1DifferentNumber);

        // Assert
        AssertMore(d2, d1);
    }

    [AssertionMethod]
    private void AssertEquals(Signature d1, Signature d2) {
        Assert.Equal(d1, d2);
        Assert.True(d1.Equals(d2));
        Assert.True(d2.Equals(d1));
        Assert.Equal(0, d1.CompareTo(d2));
    }

    [AssertionMethod]
    private void AssertNotEquals(Signature d1, Signature d2) {
        Assert.NotEqual(d1, d2);
        Assert.False(d1.Equals(d2));
        Assert.False(d2.Equals(d1));
        Assert.NotEqual(0, d1.CompareTo(d2));
        Assert.NotEqual(0, d2.CompareTo(d1));
    }

    [AssertionMethod]
    private void AssertMore(Signature d1, Signature d2) {
        AssertNotEquals(d1, d2);
        Assert.Equal(1, d1.CompareTo(d2));
        Assert.Equal(-1, d2.CompareTo(d1));
    }
}



