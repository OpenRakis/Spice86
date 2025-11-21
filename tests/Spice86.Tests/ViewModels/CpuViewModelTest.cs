namespace Spice86.Tests.ViewModels;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

using Xunit;

public class CpuViewModelTest {
    [Fact]
    public void TestIsVisibleIsSetToTrue() {
        // Arrange
        var state = new State(CpuModel.INTEL_8086);
        var memory = Substitute.For<IMemory>();
        var pauseHandler = Substitute.For<IPauseHandler>();
        var uiDispatcher = Substitute.For<IUIDispatcher>();

        // Act
        var cpuViewModel = new CpuViewModel(state, memory, pauseHandler, uiDispatcher);

        // Assert
        Assert.True(cpuViewModel.IsVisible, "IsVisible should be set to true in constructor");
    }

    [Fact]
    public void TestCyclesUpdateInStateInfo() {
        // Arrange
        var state = new State(CpuModel.INTEL_8086);
        var memory = Substitute.For<IMemory>();
        var pauseHandler = Substitute.For<IPauseHandler>();
        pauseHandler.IsPaused.Returns(false);
        var uiDispatcher = Substitute.For<IUIDispatcher>();

        var cpuViewModel = new CpuViewModel(state, memory, pauseHandler, uiDispatcher);

        // Initially cycles should be 0
        Assert.Equal(0, state.Cycles);
        Assert.Equal(0, cpuViewModel.State.Cycles);

        // Act: Increment cycles in the state
        state.IncCycles();
        state.IncCycles();
        state.IncCycles();

        // Manually trigger UpdateValues since we don't have a real dispatcher timer
        cpuViewModel.UpdateValues(null, EventArgs.Empty);

        // Assert: StateInfo should be updated with the new cycles value
        Assert.Equal(3, state.Cycles);
        Assert.Equal(3, cpuViewModel.State.Cycles);
    }
}
