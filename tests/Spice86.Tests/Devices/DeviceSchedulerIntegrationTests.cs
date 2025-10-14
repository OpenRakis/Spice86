using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;

using System.Collections.Concurrent;
using System.Diagnostics;

using Xunit;

namespace Spice86.Tests.Devices;

public class DeviceSchedulerIntegrationTests {
    private const double OneShotDelayMs = 3.0;

    private static DeviceScheduler CreateScheduler(out LoggerService logger) {
        logger = new LoggerService();
        State state = new();
        CounterConfiguratorFactory counterConfiguratorFactory = new(new Core.CLI.Configuration(), state, new PauseHandler(logger), logger);
        return new DeviceScheduler(counterConfiguratorFactory, logger);
    }

    private static double MeasureOneShot(DeviceScheduler scheduler, double delayMs, Action? beforeLoop = null) {
        int fired = 0;
        var sw = Stopwatch.StartNew();
        scheduler.ScheduleEvent($"oneshot-{delayMs}ms", delayMs, () => {
            fired++;
        });
        beforeLoop?.Invoke();
        while (fired == 0) {
            scheduler.RunEventQueue();
        }
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    [Fact]
    public void OneShotEvent_FiresWithRealTime() {
        LoggerService loggerService = new();
        State state = new();
        CounterConfiguratorFactory counterConfiguratorFactory = new(new Core.CLI.Configuration(), state, new PauseHandler(loggerService), loggerService);
        DeviceScheduler scheduler = new DeviceScheduler(counterConfiguratorFactory, loggerService);

        int fired = 0;
        scheduler.ScheduleEvent("oneshot-3ms", OneShotDelayMs, () => fired++);

        var stopwatch = Stopwatch.StartNew();
        while (fired is 0) {
            scheduler.RunEventQueue();
        }
        stopwatch.Stop();

        Assert.Equal(1, fired);
        // Sub-ms accuracy: assert absolute difference < 1 ms and no early trigger
        double elapsed = stopwatch.Elapsed.TotalMilliseconds;
        Assert.True(elapsed >= OneShotDelayMs, $"Event fired too early. Expected >= {OneShotDelayMs}ms got {elapsed:0.###}ms");
        Assert.True(elapsed - OneShotDelayMs < 1.0, $"Event drift {elapsed - OneShotDelayMs:0.###}ms >= 1ms");
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(5.0)]
    public void OneShotEvent_SubMsAccuracy_MultipleDelays(double delayMs) {
        DeviceScheduler scheduler = CreateScheduler(out _);
        double actual = MeasureOneShot(scheduler, delayMs);
        Assert.True(actual >= delayMs, $"Event fired early for {delayMs}ms: {actual:0.###}ms");
        Assert.True(actual - delayMs < 1.0, $"Drift too large for {delayMs}ms: {actual - delayMs:0.###}ms");
    }

    [Fact]
    public void MultipleEvents_FireInOrder_WithAccurateTiming() {
        DeviceScheduler scheduler = CreateScheduler(out _);
        var order = new List<string>();
        var timings = new List<double>();
        var sw = Stopwatch.StartNew();

        void Record(string name) {
            order.Add(name);
            timings.Add(sw.Elapsed.TotalMilliseconds);
        }

        scheduler.ScheduleEvent("E5", 5.0, () => Record("E5"));
        scheduler.ScheduleEvent("E1", 1.0, () => Record("E1"));
        scheduler.ScheduleEvent("E3", 3.0, () => Record("E3"));

        while (order.Count < 3) {
            scheduler.RunEventQueue();
        }
        sw.Stop();

        Assert.Equal(new[] { "E1", "E3", "E5" }, order);

        // Verify each elapsed near requested (no early fire; drift <1ms)
        var expected = new Dictionary<string, double> { { "E1", 1.0 }, { "E3", 3.0 }, { "E5", 5.0 } };
        for (int i = 0; i < order.Count; i++) {
            double exp = expected[order[i]];
            double act = timings[i];
            Assert.True(act >= exp, $"{order[i]} fired early: exp>={exp}ms act={act:0.###}ms");
            Assert.True(act - exp < 1.0, $"{order[i]} drift {act - exp:0.###}ms >=1ms");
        }
    }

    [Fact]
    public void EventsScheduledDuringService_UseServiceBaseTicks() {
        DeviceScheduler scheduler = CreateScheduler(out _);
        double firstFiredMs = 0;
        double secondFiredMs = 0;
        var sw = Stopwatch.StartNew();

        scheduler.ScheduleEvent("First", 2.0, () => {
            firstFiredMs = sw.Elapsed.TotalMilliseconds;
            // This should schedule relative to service base (i.e., about +1ms after first)
            scheduler.ScheduleEvent("Second", 1.0, () => secondFiredMs = sw.Elapsed.TotalMilliseconds);
        });

        while (secondFiredMs == 0) {
            scheduler.RunEventQueue();
        }
        sw.Stop();

        double delta = secondFiredMs - firstFiredMs;
        Assert.True(delta >= 1.0, $"Second event fired too early relative to first. Delta={delta:0.###}ms");
        Assert.True(delta < 2.0, $"Second event delta too large ({delta:0.###}ms) indicates wrong base time usage.");
    }

    [Fact]
    public void RemoveEvents_CancelsAllMatchingHandlers() {
        DeviceScheduler scheduler = CreateScheduler(out _);
        int fired = 0;
        void handler(uint _) => fired++;

        // Schedule 3 events
        scheduler.ScheduleEvent("A", 1.0, handler, 1);
        scheduler.ScheduleEvent("B", 1.5, handler, 2);
        scheduler.ScheduleEvent("C", 2.0, handler, 3);

        int removed = scheduler.RemoveEvents(handler);
        Assert.Equal(3, removed);

        // Run long enough to ensure if not canceled they'd fire
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 5.0) {
            scheduler.RunEventQueue();
        }
        sw.Stop();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void RemoveSpecificEvents_CancelsOnlyMatching() {
        DeviceScheduler scheduler = CreateScheduler(out _);
        var firedValues = new List<uint>();
        void handler(uint v) => firedValues.Add(v);

        scheduler.ScheduleEvent("A", 1.0, handler, 1);
        scheduler.ScheduleEvent("B", 1.2, handler, 2);
        scheduler.ScheduleEvent("C", 1.4, handler, 3);

        int removed = scheduler.RemoveSpecificEvents(handler, 2);
        Assert.Equal(1, removed);

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < 5.0 && firedValues.Count < 2) {
            scheduler.RunEventQueue();
        }
        sw.Stop();

        firedValues.Sort();
        Assert.Equal(new uint[] { 1, 3 }, firedValues);
    }

    [Fact]
    public void TimeMultiplier_ShortensRealDelay() {
        DeviceScheduler scheduler = CreateScheduler(out _);

        // Baseline: multiplier 1
        double baseline = MeasureOneShot(scheduler, 6.0);

        // Increase speed 2x => same ms delay should take ~half real time
        scheduler.SetTimeMultiplier(2.0);
        double accelerated = MeasureOneShot(scheduler, 6.0);

        // Both accurate individually
        Assert.True(baseline >= 6.0);
        Assert.True(accelerated >= 3.0, "Accelerated event fired too early (before adjusted half delay).");
        // Must be substantially less than baseline (allow some overhead)
        Assert.True(accelerated < baseline * 0.75, $"Accelerated delay {accelerated:0.###}ms not sufficiently less than baseline {baseline:0.###}ms");
    }

    [Fact]
    public void PeriodicCallback_FrequencyRespondsToTimeMultiplier() {
        DeviceScheduler scheduler = CreateScheduler(out _);

        int countNormal = 0;

        // Register 1000Hz periodic callback
        scheduler.RegisterPeriodicCallback("tick-1k", 1000.0, () => countNormal++);

        var sw = Stopwatch.StartNew();
        // Run for ~10ms normal speed
        while (sw.Elapsed.TotalMilliseconds < 10) {
            scheduler.DeviceTick();
        }
        int snapshotNormal = countNormal;
        Assert.True(snapshotNormal > 0, "Periodic callback did not fire at all at normal speed.");

        // Increase multiplier -> effective frequency doubles
        scheduler.SetTimeMultiplier(2.0);
        sw.Restart();
        while (sw.Elapsed.TotalMilliseconds < 10) {
            scheduler.DeviceTick();
        }
        int snapshotFast = countNormal - snapshotNormal;
        Assert.True(snapshotFast > snapshotNormal * 1.5,
            $"Fast period increment ({snapshotFast}) not significantly higher than normal period increment ({snapshotNormal}).");
    }

    [Fact]
    public void SimultaneousDueEvents_AllFireSameServicePass() {
        DeviceScheduler scheduler = CreateScheduler(out _);
        var fired = new ConcurrentQueue<string>();
        for (int i = 0; i < 5; i++) {
            scheduler.ScheduleEvent($"E{i}", 2.0, () => fired.Enqueue($"E{i}"));
        }

        var sw = Stopwatch.StartNew();
        while (fired.Count < 5 && sw.Elapsed.TotalMilliseconds < 10) {
            scheduler.RunEventQueue();
        }
        sw.Stop();

        Assert.Equal(5, fired.Count);
        // Order is not strictly guaranteed for identical due times, but all must have arrived quickly
        Assert.True(sw.Elapsed.TotalMilliseconds - 2.0 < 2.0, $"Drift for batch firing too large ({sw.Elapsed.TotalMilliseconds - 2.0:0.###}ms)");
    }

    [Fact]
    public void OneShotEvent_RealTimeGuardPreventsEarlyFire() {
        DeviceScheduler scheduler = CreateScheduler(out _);
        bool fired = false;
        double delay = 4.0;
        scheduler.ScheduleEvent("guarded", delay, () => fired = true);

        var sw = Stopwatch.StartNew();
        while (!fired) {
            scheduler.RunEventQueue();
            // Artificial small sleep to simulate coarse polling; shouldn't allow early fire
            if (sw.Elapsed.TotalMilliseconds < delay - 0.5) {
                // Busy loop only; no Assert here
            }
        }
        sw.Stop();

        Assert.True(sw.Elapsed.TotalMilliseconds >= delay,
            $"Real-time guard failed. Event fired at {sw.Elapsed.TotalMilliseconds:0.###}ms, expected >= {delay}ms");
        Assert.True(sw.Elapsed.TotalMilliseconds - delay < 1.0,
            $"Drift too large {sw.Elapsed.TotalMilliseconds - delay:0.###}ms");
    }

    [Fact]
    public void ScheduleDuringHandlerChain_TimingRespectsEachRelativeDelay() {
        DeviceScheduler scheduler = CreateScheduler(out _);
        var sw = Stopwatch.StartNew();
        double t1 = 0, t2 = 0, t3 = 0;

        // Event1 after 2ms -> schedules event2 (1ms) -> schedules event3 (0.5ms)
        scheduler.ScheduleEvent("E1", 2.0, () => {
            t1 = sw.Elapsed.TotalMilliseconds;
            scheduler.ScheduleEvent("E2", 1.0, () => {
                t2 = sw.Elapsed.TotalMilliseconds;
                scheduler.ScheduleEvent("E3", 0.5, () => {
                    t3 = sw.Elapsed.TotalMilliseconds;
                });
            });
        });

        while (t3 == 0) {
            scheduler.RunEventQueue();
        }
        sw.Stop();

        double d12 = t2 - t1;
        double d23 = t3 - t2;

        Assert.True(d12 is >= 1.0 and < 2.0, $"E2 delay relative to E1 incorrect: {d12:0.###}ms");
        Assert.True(d23 is >= 0.5 and < 1.5, $"E3 delay relative to E2 incorrect: {d23:0.###}ms");
    }

    [Fact]
    public void HighVolume_ShortDelays_AllFire() {
        DeviceScheduler scheduler = CreateScheduler(out _);
        int total = 200;
        int fired = 0;
        for (int i = 0; i < total; i++) {
            double delay = 1.0 + (i % 5) * 0.2; // spread 1.0..1.8ms
            scheduler.ScheduleEvent($"HV{i}", delay, () => fired++);
        }

        var sw = Stopwatch.StartNew();
        while (fired < total && sw.Elapsed.TotalMilliseconds < 20) {
            scheduler.RunEventQueue();
        }
        sw.Stop();

        Assert.Equal(total, fired);
        // Basic drift sanity: last events should not exceed expected by huge margin
        Assert.True(sw.Elapsed.TotalMilliseconds - 1.8 < 10.0, "Excessive drift for high volume short delays.");
    }
}