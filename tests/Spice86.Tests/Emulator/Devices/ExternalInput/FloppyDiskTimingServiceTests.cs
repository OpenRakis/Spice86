namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Interfaces;

using System;

using Xunit;

public sealed class FloppyDiskTimingServiceTests {
    [Fact]
    public void ScheduleFloppyIoDelay_MaximumSpeed_ProcessesDueEventsWithoutAdvancingCycles() {
        // Arrange
        ILoggerService logger = CreateLogger();
        State state = new(CpuModel.INTEL_8086);
        CyclesClock clock = new(state, 1000, null, DateTimeOffset.UnixEpoch);
        DeviceScheduler scheduler = new(clock, logger, "Floppy timing test");
        FloppyDiskTimingService timingService = new(state, clock, scheduler, FloppyDiskSpeed.Maximum);
        bool invoked = false;
        scheduler.AddEvent(_ => invoked = true, 0);

        // Act
        double delayMs = timingService.ScheduleFloppyIoDelay(1);

        // Assert
        delayMs.Should().Be(0);
        invoked.Should().BeTrue();
        state.Cycles.Should().Be(0);
    }

    [Fact]
    public void ScheduleFloppyIoDelay_FastSpeed_AdvancesCyclesAndFiresDueEvents() {
        // Arrange
        ILoggerService logger = CreateLogger();
        State state = new(CpuModel.INTEL_8086);
        CyclesClock clock = new(state, 1000, null, DateTimeOffset.UnixEpoch);
        DeviceScheduler scheduler = new(clock, logger, "Floppy timing test");
        FloppyDiskTimingService timingService = new(state, clock, scheduler, FloppyDiskSpeed.Fast);
        bool invoked = false;
        scheduler.AddEvent(_ => invoked = true, 4);

        // Act
        double delayMs = timingService.ScheduleFloppyIoDelay(1);

        // Assert
        delayMs.Should().BeApproximately(4.1666666667, 0.0001);
        invoked.Should().BeTrue();
        state.Cycles.Should().Be(5);
    }

    private static ILoggerService CreateLogger() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(false);
        return logger;
    }
}
