using Serilog.Events;

using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

namespace Spice86.Core.Emulator.Devices;

/// <summary>
/// Provides scheduling and periodic callback services for device emulation, allowing timed events and periodic actions
/// to be registered and executed with adjustable timing.
/// </summary>
public sealed class DeviceScheduler {
    private readonly ILoggerService _loggerService;
    private readonly CounterConfiguratorFactory _counterConfiguratorFactory;
    private readonly Func<long> _nowTicks;
    private readonly long _ticksPerSecond;

    private double _timeMultiplier = 1.0;

    private readonly record struct PeriodicCallbackEntry(string Name, CounterActivator Activator, Action Callback);

    private readonly List<PeriodicCallbackEntry> _deviceCallbacks = new();

    private readonly PriorityQueue<ScheduledEvent, long> _eventQueue = new();
    private bool _inEventService = false;

    // Equivalent to srv_lag in pic.cpp: base index for events scheduled during servicing
    private long _serviceBaseTicks = 0;

    public record ScheduledEvent {
        public required string Name { get; init; }
        public required Action<uint> Handler { get; init; }
        public required uint Parameter { get; init; }
        public required long DueTicks { get; init; }
        // Real-time guard: ensure actual elapsed wall time since scheduling >= DelayMs
        public required long StartTicks { get; init; }
        public required double DelayMs { get; init; }
        public bool Canceled { get; set; }
        public override string ToString() => $"{Name} (DueTicks: {DueTicks}, Canceled: {Canceled})";
    }

    public DeviceScheduler(CounterConfiguratorFactory counterConfiguratorFactory,
        ILoggerService loggerService) {
        _counterConfiguratorFactory = counterConfiguratorFactory;
        _loggerService = loggerService;
        _nowTicks = Stopwatch.GetTimestamp;
        _ticksPerSecond = Stopwatch.Frequency;
    }

    public void SetTimeMultiplier(double multiplier) {
        if (multiplier <= 0) throw new DivideByZeroException(nameof(multiplier));
        _timeMultiplier = multiplier;
        // Update already registered periodic activators
        foreach (PeriodicCallbackEntry entry in _deviceCallbacks) {
            entry.Activator.Multiplier = multiplier;
        }
    }

    public void RegisterPeriodicCallback(string name, double frequencyHz, Action callback) {
        CounterActivator activator = _counterConfiguratorFactory.InstantiateCounterActivator();
        activator.Multiplier = _timeMultiplier;
        activator.Frequency = (long)Math.Max(1, Math.Round(frequencyHz));
        _deviceCallbacks.Add(new PeriodicCallbackEntry(name, activator, callback));
    }

    public void UnregisterPeriodicCallback(Action callback) {
        _deviceCallbacks.RemoveAll(x => x.Callback == callback);
    }

    public void DeviceTick() {
        foreach (PeriodicCallbackEntry entry in _deviceCallbacks) {
            if (entry.Activator.IsActivated) {
                entry.Callback();
            }
        }
    }

    public ScheduledEvent ScheduleEvent(string name, double delayMs, Action action) =>
        ScheduleEvent(name, delayMs, _ => action(), 0);

    public ScheduledEvent ScheduleEvent(string name, double delayMs, Action<uint> handler, uint value) {
        // When in event service, base new events on the last serviced event time
        long startTicks = _inEventService ? _serviceBaseTicks : _nowTicks();
        long dueTicks = startTicks + MsToTicks(delayMs / _timeMultiplier);

        var ev = new ScheduledEvent {
            Name = name,
            Handler = handler,
            Parameter = value,
            DueTicks = dueTicks,
            StartTicks = startTicks,
            DelayMs = delayMs,
            Canceled = false
        };

        _eventQueue.Enqueue(ev, ev.DueTicks);
        return ev;
    }

    public int RemoveEvents(Action<uint> handler) {
        int count = 0;
        foreach ((ScheduledEvent? Element, _) in _eventQueue.UnorderedItems) {
            if (!Element.Canceled && Element.Handler == handler) {
                Element.Canceled = true;
                count++;
            }
        }
        return count;
    }

    public int RemoveSpecificEvents(Action<uint> handler, uint value) {
        int count = 0;
        foreach ((ScheduledEvent? Element, _) in _eventQueue.UnorderedItems) {
            if (!Element.Canceled && Element.Handler == handler && Element.Parameter == value) {
                Element.Canceled = true;
                count++;
            }
        }
        return count;
    }

    public void RunEventQueue() {
        long now = _nowTicks();
        _inEventService = true;
        _serviceBaseTicks = now;

        while (_eventQueue.TryPeek(out ScheduledEvent? ev, out long due)) {
            if (due > now) {
                break; // Not yet due by scheduled ticks
            }

            // Ensure true wall time elapsed meets or exceeds requested delay (guards against firing just before integer ms boundary)
            double elapsedMs = (now - ev!.StartTicks) * 1000.0 / _ticksPerSecond;
            if (elapsedMs + 1e-9 < ev.DelayMs) { // +epsilon for floating precision
                break; // Wait a little longer
            }

            _eventQueue.Dequeue();
            if (ev.Canceled) {
                continue;
            }
            try {
                _serviceBaseTicks = ev.DueTicks; // update base
                ev.Handler(ev.Parameter);
            } catch (Exception ex) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Timer event '{Name}' failed: {Error}", ev?.Name, ex.Message);
                }
                throw;
            }
            now = _nowTicks(); // refresh after handler
        }

        _inEventService = false;
    }

    private long MsToTicks(double ms) => (long)Math.Ceiling(ms * _ticksPerSecond / 1000.0);
}