namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Xunit;

public class FlagEffectTest {
    [Fact]
    public void MustWritesAreAlsoMayWrites() {
        // Act
        FlagEffect effect = new(StatusFlags.None, StatusFlags.Carry, StatusFlags.Zero);

        // Assert
        effect.MayWrite.Should().Be(StatusFlags.Carry | StatusFlags.Zero);
    }

    [Fact]
    public void ThenHidesReadsOfFlagsTheFirstEffectWrote() {
        // Act
        FlagEffect effect = FlagEffect.Writing(StatusFlags.Carry).Then(FlagEffect.Reading(StatusFlags.Carry | StatusFlags.Zero));

        // Assert
        effect.Should().Be(new FlagEffect(StatusFlags.Zero, StatusFlags.Carry, StatusFlags.Carry));
    }

    [Fact]
    public void EitherKeepsOnlyCommonMustWrites() {
        // Act
        FlagEffect effect = FlagEffect.Either(
            new FlagEffect(StatusFlags.Sign, StatusFlags.Carry | StatusFlags.Zero, StatusFlags.None),
            FlagEffect.Writing(StatusFlags.Zero));

        // Assert
        effect.Should().Be(new FlagEffect(StatusFlags.Sign, StatusFlags.Zero, StatusFlags.Carry | StatusFlags.Zero));
    }

    [Fact]
    public void DefaultIsNone() {
        // Assert
        default(FlagEffect).Should().Be(FlagEffect.None);
    }
}
