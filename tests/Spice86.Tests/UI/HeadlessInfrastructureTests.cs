namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using FluentAssertions;

using Spice86.Views;

public class HeadlessInfrastructureTests {
    [AvaloniaFact]
    public void ProcessUiEvents_DoesNotThrow() {
        //Arrange
        bool completed = false;

        //Act
        Dispatcher.UIThread.Post(() => completed = true, DispatcherPriority.Background);
        Dispatcher.UIThread.RunJobs();

        //Assert
        completed.Should().BeTrue();
    }

    [AvaloniaFact]
    public void TestDisassemblyViewCanBeCreated() {
        //Arrange

        //Act
        DisassemblyView disassemblyView = new();

        //Assert
        disassemblyView.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void TestBreakpointsViewCanBeCreated() {
        //Arrange

        //Act
        BreakpointsView breakpointsView = new();

        //Assert
        breakpointsView.Should().NotBeNull();
    }
}
