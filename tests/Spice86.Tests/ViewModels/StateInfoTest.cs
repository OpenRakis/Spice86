namespace Spice86.Tests.ViewModels;

using Spice86.ViewModels.ValueViewModels.Debugging;
using Xunit;

public class StateInfoTest {
    [Fact]
    public void TestCyclesCanBeSet() {
        var stateInfo = new StateInfo();
        Assert.Equal(0, stateInfo.Cycles);
        
        stateInfo.Cycles = 42;
        Assert.Equal(42, stateInfo.Cycles);
        
        stateInfo.Cycles = 100;
        Assert.Equal(100, stateInfo.Cycles);
    }
}
