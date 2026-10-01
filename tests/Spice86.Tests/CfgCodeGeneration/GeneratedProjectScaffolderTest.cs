namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Xunit;

/// <summary>
/// Tests for the project files written by <see cref="GeneratedProjectScaffolder"/>.
/// </summary>
public class GeneratedProjectScaffolderTest {
    private const string OverflowChecksOff = "<CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>";

    [Fact]
    public void ProjectReferenceCsProjDisablesOverflowChecks() {
        string csProj = GeneratedProjectScaffolder.BuildCsProjWithProjectReference("../src/Spice86/Spice86.csproj");

        csProj.Should().Contain(OverflowChecksOff);
    }

    [Fact]
    public void PackageReferenceCsProjDisablesOverflowChecks() {
        string csProj = GeneratedProjectScaffolder.BuildCsProjWithPackageReference("1.0.0");

        csProj.Should().Contain(OverflowChecksOff);
    }
}
