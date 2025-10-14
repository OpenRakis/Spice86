using Xunit;
using System.Diagnostics;
using Spice86.Core.Emulator.Devices;
using Spice86.Shared.Interfaces;
using NSubstitute;
using Spice86.Logging;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.CPU;

namespace Spice86.Tests.Devices;

public class DeviceSchedulerIntegrationTests {
    private const double OneShotDelayMs = 3.0;

    [Fact]
    public void OneShotEvent_FiresWithRealTime() {
        // Arrange
        LoggerService loggerService = new();
        State state = new();
        CounterConfiguratorFactory counterConfiguratorFactory = new(new Core.CLI.Configuration(), state, new PauseHandler(loggerService), loggerService);
        DeviceScheduler scheduler = new DeviceScheduler(counterConfiguratorFactory, loggerService);

        //Act
        int fired = 0;
        scheduler.ScheduleEvent("oneshot-3ms", OneShotDelayMs, () => fired++);

        var stopwatch = Stopwatch.StartNew();
        while(fired is 0) {
            scheduler.RunEventQueue();
        }
        stopwatch.Stop();

        //Assert
        Assert.Equal(1, fired);
        Assert.Equal(TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds), TimeSpan.FromMilliseconds(OneShotDelayMs));
    }

    [Fact]
    public void PeriodicCallback_FiresWithRealTime() {
        //Arrange
        LoggerService loggerService = new();
        State state = new();
        CounterConfiguratorFactory counterConfiguratorFactory = new(new Core.CLI.Configuration(), state, new PauseHandler(loggerService), loggerService);
        DeviceScheduler scheduler = new DeviceScheduler(counterConfiguratorFactory, loggerService);

        //Act
        int fired = 0;
        scheduler.RegisterPeriodicCallback("tick-1k", 1000.0, () => fired++);

        while (fired is 0) {
            scheduler.DeviceTick();
        }

        //Assert
        // Should have fired at least once over ~10ms window.
        Assert.True(fired >= 1);
    }
}