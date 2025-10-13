using Spice86.Tests;
using Xunit;
using System.Diagnostics;
using System.Threading;

namespace Spice86.Tests.Devices;

public class DeviceSchedulerIntegrationTests {
    [Fact]
    public void OneShotEvent_FiresWithRealStopwatchTime() {
        // Arrange: full VM via Spice86Creator (no custom logger/config here)
        using Spice86DependencyInjection di = new Spice86Creator("add", enableCfgCpu: false, enablePit: false).Create();

        // You must expose DeviceScheduler via Machine (e.g., di.Machine.DeviceScheduler)
        var scheduler = di.Machine.DeviceScheduler;

        int fired = 0;
        scheduler.ScheduleEvent("oneshot-3ms", 3.0, () => Interlocked.Increment(ref fired));

        var startTicks = Stopwatch.GetTimestamp();
        var freq = Stopwatch.Frequency;

        // Busy-wait up to ~20ms; the scheduler only fires when RunEventQueue is polled.
        while (fired == 0 && (Stopwatch.GetTimestamp() - startTicks) < (freq / 50)) {
            scheduler.RunEventQueue();
            Thread.SpinWait(200);
        }

        Assert.Equal(1, Volatile.Read(ref fired));
    }

    [Fact]
    public void PeriodicCallback_FiresWithoutFakes() {
        using Spice86DependencyInjection di = new Spice86Creator("add", enableCfgCpu: false, enablePit: false).Create();

        var scheduler = di.Machine.DeviceScheduler;

        int hits = 0;
        scheduler.RegisterPeriodicCallback("tick-1k", 1000.0, () => Interlocked.Increment(ref hits));

        // Drive the periodic loop for a few milliseconds using real Stopwatch time.
        var start = Stopwatch.GetTimestamp();
        var freq = Stopwatch.Frequency;
        while ((Stopwatch.GetTimestamp() - start) < (long)(freq * 0.010)) {
            scheduler.DeviceTick();
            // Allow time to pass; periodic activator is time-based internally
            Thread.SpinWait(500);
        }

        // Should have fired at least once over ~10ms window.
        Assert.True(Volatile.Read(ref hits) >= 1);
    }
}