namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using Xunit;

public sealed class DeviceSchedulerTests {
    [Fact]
    public void RunQueueDoesNotProcessWhenNoCyclesRemain() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        var emulatedClock = new EmulatedClock();
        var queue = new DeviceScheduler(emulatedClock, logger);

        bool invoked = false;
        queue.AddEvent(_ => invoked = true, 0.25);

        queue.ProcessEvents(); // Call ProcessEvents directly
        
        invoked.Should().BeFalse();
    }
}
