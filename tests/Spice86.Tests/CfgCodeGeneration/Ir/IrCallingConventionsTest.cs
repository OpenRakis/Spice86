namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Xunit;

/// <summary>
/// The round-two guard of <see cref="IrCallingConventions.VerifySubset"/>: round two of the prepare
/// only removes writes justified by round one, so every convention set it reports must be a subset
/// of the round-one set. No catalogue program can violate it by construction, so the check is
/// exercised on synthetic sets.
/// </summary>
public sealed class IrCallingConventionsTest {
    [Fact]
    public void VerifySubsetAcceptsAShrinkingSet() {
        Dictionary<string, CpuValues> earlier = new() { ["entry"] = CpuValues.Eax };
        Dictionary<string, CpuValues> later = new() { ["entry"] = CpuValues.Ax };

        Action act = () => IrCallingConventions.VerifySubset(earlier, later, "outputs");

        act.Should().NotThrow();
    }

    [Fact]
    public void VerifySubsetThrowsWhenASetGrows() {
        Dictionary<string, CpuValues> earlier = new() { ["entry"] = CpuValues.Ax };
        Dictionary<string, CpuValues> later = new() { ["entry"] = CpuValues.Eax };

        Action act = () => IrCallingConventions.VerifySubset(earlier, later, "outputs");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*entry*not a subset*");
    }

    [Fact]
    public void VerifySubsetTreatsAMissingEarlierEntryAsTheFullReportableSet() {
        Dictionary<string, CpuValues> earlier = new();
        Dictionary<string, CpuValues> later = new() { ["entry"] = CpuValues.Eax };

        Action act = () => IrCallingConventions.VerifySubset(earlier, later, "outputs");

        act.Should().NotThrow();
    }
}
