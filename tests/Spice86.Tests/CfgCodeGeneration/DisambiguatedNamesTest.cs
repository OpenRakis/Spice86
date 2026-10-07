namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;

using Xunit;

public sealed class DisambiguatedNamesTest {
    private sealed record Item(int Key, string Name, string Suffix);

    [Fact]
    public void UniqueBaseNameIsKept() {
        // Arrange
        Item[] items = [new(0, "a", "x"), new(1, "b", "y")];

        // Act
        Dictionary<Item, string> result = DisambiguatedNames.Build(items, item => item.Name, item => item.Suffix);

        // Assert
        result[items[0]].Should().Be("a");
        result[items[1]].Should().Be("b");
    }

    [Fact]
    public void EveryMemberOfACollidingGroupGetsTheSuffix() {
        // Arrange
        Item[] items = [new(0, "L", "1"), new(1, "L", "2"), new(2, "M", "3")];

        // Act
        Dictionary<Item, string> result = DisambiguatedNames.Build(items, item => item.Name, item => item.Suffix);

        // Assert
        result[items[0]].Should().Be("L_1");
        result[items[1]].Should().Be("L_2");
        result[items[2]].Should().Be("M");
    }

    [Fact]
    public void SuffixCollisionInsideAGroupThrows() {
        // Arrange
        Item[] items = [new(0, "L", "1"), new(1, "L", "1")];

        // Act
        Action act = () => DisambiguatedNames.Build(items, item => item.Name, item => item.Suffix);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*L_1*");
    }
}
