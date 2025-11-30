namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;

using Xunit;

public sealed class EmulationLoopSchedulerTests {
    [Fact]
    public void RunQueueDoesNotProcessWhenNoCyclesRemain() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        var emulatedClock = new EmulatedClock();
        var emulationLoopScheduler = new EmulationLoopScheduler(emulatedClock, logger);

        bool invoked = false;
        emulationLoopScheduler.AddEvent(_ => invoked = true, 0.25);

        emulationLoopScheduler.ProcessEvents(); // Call ProcessEvents directly
        
        invoked.Should().BeFalse();
    }
}
