namespace Spice86.Tests.ViewModels;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

using Xunit;

public class PerformanceViewModelTest {
    private static void TriggerUpdate(PerformanceViewModel viewModel) {
        // Use reflection to call private UpdatePerformanceInfo method
        var method = typeof(PerformanceViewModel).GetMethod("UpdatePerformanceInfo", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(viewModel, new object?[] { null, EventArgs.Empty });
    }

    [Fact]
    public void TestPerformanceViewModelUpdatesWhenNotPaused() {
        // Arrange
        var state = new State(CpuModel.INTEL_8086);
        var pauseHandler = Substitute.For<IPauseHandler>();
        pauseHandler.IsPaused.Returns(false);
        var uiDispatcher = Substitute.For<IUIDispatcher>();
        var perfReader = Substitute.For<IPerformanceMeasureReader>();
        perfReader.AverageValuePerSecond.Returns(1000L);
        perfReader.ValuePerMillisecond.Returns(50L);

        var performanceViewModel = new PerformanceViewModel(state, pauseHandler, uiDispatcher, perfReader);

        // Initially should be 0
        Assert.Equal(0, performanceViewModel.InstructionsExecuted);

        // Act: Increment cycles and manually trigger update
        state.IncCycles();
        state.IncCycles();
        state.IncCycles();
        
        TriggerUpdate(performanceViewModel);

        // Assert
        Assert.Equal(3, performanceViewModel.InstructionsExecuted);
        Assert.Equal(1000.0, performanceViewModel.AverageInstructionsPerSecond);
        Assert.Equal(50.0, performanceViewModel.InstructionsPerMillisecond);
    }

    [Fact]
    public void TestPerformanceViewModelDoesNotUpdateWhenPaused() {
        // Arrange
        var state = new State(CpuModel.INTEL_8086);
        var pauseHandler = Substitute.For<IPauseHandler>();
        pauseHandler.IsPaused.Returns(true); // Start paused
        var uiDispatcher = Substitute.For<IUIDispatcher>();
        var perfReader = Substitute.For<IPerformanceMeasureReader>();

        var performanceViewModel = new PerformanceViewModel(state, pauseHandler, uiDispatcher, perfReader);

        // Act: Increment cycles and manually trigger update
        state.IncCycles();
        state.IncCycles();
        state.IncCycles();
        
        TriggerUpdate(performanceViewModel);

        // Assert: Should still be 0 because it's paused
        Assert.Equal(0, performanceViewModel.InstructionsExecuted);
    }
}
