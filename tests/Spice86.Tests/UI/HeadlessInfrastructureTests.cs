namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using FluentAssertions;

using Spice86.Views;

public class HeadlessInfrastructureTests {
    [AvaloniaFact]
    public void ProcessUiEvents_DoesNotThrow() {
        bool completed = false;

        Dispatcher.UIThread.Post(() => completed = true, DispatcherPriority.Background);
        Dispatcher.UIThread.RunJobs();

        completed.Should().BeTrue();
    }

    [AvaloniaFact]
    public void TestDisassemblyViewCanBeCreated() {
        DisassemblyView disassemblyView = new();

        disassemblyView.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void TestBreakpointsViewCanBeCreated() {
        BreakpointsView breakpointsView = new();

        breakpointsView.Should().NotBeNull();
    }
}
