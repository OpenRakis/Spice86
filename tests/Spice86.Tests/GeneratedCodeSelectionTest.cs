namespace Spice86.Tests;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using System;

using FluentAssertions;

using Xunit;

public class GeneratedCodeSelectionTest {
    [Fact]
    public void EmptyListKeepsTheDefaults() {
        GeneratedCodeSelection.Resolved resolved = GeneratedCodeSelection.Resolve(null);
        resolved.AsmComments.Should().BeFalse();
        resolved.BranchProbes.Should().BeFalse();
        resolved.ServiceCallNotifications.Should().BeFalse();
        resolved.DeviceOperations.Should().BeTrue();
        resolved.Relaxations.Should().Be(GenerationRelaxation.None);
        GeneratedCodeSelection.Resolve("").DeviceOperations.Should().BeTrue();
    }

    [Fact]
    public void OneNameEnablesItsSetting() {
        GeneratedCodeSelection.Resolved resolved = GeneratedCodeSelection.Resolve("AsmComments");
        resolved.AsmComments.Should().BeTrue();
        resolved.BranchProbes.Should().BeFalse();
    }

    [Fact]
    public void SeveralNamesApplyLeftToRight() {
        GeneratedCodeSelection.Resolved resolved =
            GeneratedCodeSelection.Resolve("BranchProbes,AssumeUnknownMemoryIsRam,-BranchProbes");
        resolved.BranchProbes.Should().BeFalse();
        resolved.Relaxations.Should().Be(GenerationRelaxation.UnknownMemoryIsRam);
    }

    [Fact]
    public void MinusAfterThePresetOverridesIt() {
        GeneratedCodeSelection.Resolved checkBuild = GeneratedCodeSelection.Resolve("CheckBuild");
        checkBuild.ServiceCallNotifications.Should().BeTrue();
        checkBuild.BranchProbes.Should().BeTrue();
        checkBuild.DeviceOperations.Should().BeFalse();

        GeneratedCodeSelection.Resolved overridden =
            GeneratedCodeSelection.Resolve("CheckBuild,-BranchProbes,+DeviceOperations");
        overridden.ServiceCallNotifications.Should().BeTrue();
        overridden.BranchProbes.Should().BeFalse();
        overridden.DeviceOperations.Should().BeTrue();
    }

    [Fact]
    public void UnknownNameFailsAndListsTheValidNames() {
        Action act = () => GeneratedCodeSelection.Resolve("NoSuchName");
        act.Should().Throw<FormatException>().WithMessage("*AsmComments*CheckBuild*AssumeSavedSlotsWrittenOnlyByPops*");
    }

    [Fact]
    public void BadSignFails() {
        Action act = () => GeneratedCodeSelection.Resolve("++AsmComments");
        act.Should().Throw<FormatException>().WithMessage("*AsmComments*");
        Action minusOnly = () => GeneratedCodeSelection.Resolve("-");
        minusOnly.Should().Throw<FormatException>();
    }

    [Fact]
    public void PlusSignEnablesExplicitly() {
        GeneratedCodeSelection.Resolved resolved = GeneratedCodeSelection.Resolve("+BranchProbes");
        resolved.BranchProbes.Should().BeTrue();
    }
}
