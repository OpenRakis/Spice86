namespace Spice86.Tests.UI;

using FluentAssertions;

using Spice86.Core.Emulator.StateSerialization;
using Spice86.ViewModels;

using Xunit;

public class DumpProgressViewModelTest {
    [Fact]
    public void MessagesAreAppendedAndDriveThePhaseText() {
        DumpProgressViewModel viewModel = new();

        viewModel.AddMessage("first");
        viewModel.AddMessage("second");

        viewModel.Messages.Should().Equal("first", "second");
        viewModel.PhaseText.Should().Be("second");
    }

    [Fact]
    public void ReportStepMakesTheProgressDeterminate() {
        DumpProgressViewModel viewModel = new();

        viewModel.IsIndeterminate.Should().BeTrue();

        viewModel.ReportStep(DumpPhase.CodeMethods, 2, 5, "test_F000_0000_F0000");

        viewModel.IsIndeterminate.Should().BeFalse();
        viewModel.ProgressValue.Should().BeApproximately(0.16, 1e-9);
        viewModel.ProgressMaximum.Should().Be(1);
        viewModel.PhaseText.Should().Be("Code generation (3/5): preparing method 2 / 5 (40%): test_F000_0000_F0000");
    }

    [Fact]
    public void AddMessageKeepsTheStepText() {
        // Arrange
        DumpProgressViewModel viewModel = new();
        viewModel.ReportStep(DumpPhase.CodeMethods, 2, 5, "test_F000_0000_F0000");

        // Act
        viewModel.AddMessage("slow line");

        // Assert
        viewModel.PhaseText.Should().Be("Code generation (3/5): preparing method 2 / 5 (40%): test_F000_0000_F0000");
        viewModel.Messages.Last().Should().Be("slow line");
    }

    [Fact]
    public void AddMessageDrivesThePhaseTextAgainAfterBeginPhase() {
        // Arrange
        DumpProgressViewModel viewModel = new();
        viewModel.ReportStep(DumpPhase.CodeMethods, 2, 5, "test_F000_0000_F0000");
        viewModel.BeginPhase(DumpPhase.CodeSource);

        // Act
        viewModel.AddMessage("x");

        // Assert
        viewModel.PhaseText.Should().Be("x");
    }

    [Fact]
    public void BeginPhaseResetsTheProgressToIndeterminate() {
        DumpProgressViewModel viewModel = new();
        viewModel.ReportStep(DumpPhase.CodeMethods, 2, 5, "method_a");

        viewModel.BeginPhase(DumpPhase.CodeSource);

        viewModel.IsIndeterminate.Should().BeTrue();
        viewModel.ProgressValue.Should().Be(0);
        viewModel.ProgressMaximum.Should().Be(1);
    }

    [Fact]
    public void CodeGenerationPhasesShareOneProgressBar() {
        DumpProgressViewModel viewModel = new();
        viewModel.ReportStep(DumpPhase.CodeMethods, 5, 5, "m");

        viewModel.BeginPhase(DumpPhase.CodeProgramAnalysis);

        viewModel.IsIndeterminate.Should().BeFalse();
        viewModel.ProgressValue.Should().BeApproximately(0.4, 1e-9);

        viewModel.ReportStep(DumpPhase.CodeProgramAnalysis, 2, 4, "calling conventions, round 3");

        viewModel.ProgressValue.Should().BeApproximately(0.65, 1e-9);
        viewModel.PhaseText.Should().Be(
            "Code generation (4/5): program analysis step 2 / 4 (50%): calling conventions, round 3");

        viewModel.BeginPhase(DumpPhase.CodePrinting);
        viewModel.ReportStep(DumpPhase.CodePrinting, 1, 1, "m");

        viewModel.ProgressValue.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void FinishAllowsTheWindowToClose() {
        DumpProgressViewModel viewModel = new();

        viewModel.IsFinished.Should().BeFalse();

        viewModel.Finish();

        viewModel.IsFinished.Should().BeTrue();
    }
}
