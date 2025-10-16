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
    private long _serviceBaseTicks = 0; // base used while inside RunEventQueue

    public record ScheduledEvent {
        public required string Name { get; init; }
        public required Action<uint> Handler { get; init; }
        public required uint Parameter { get; init; }
        public required long DueTicks { get; init; }
        public required long StartTicks { get; init; }
        public required double DelayMs { get; init; }
        public required double EffectiveDelayMs { get; init; }
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
        if (multiplier <= 0) {
            throw new DivideByZeroException(nameof(multiplier));
        }

        _timeMultiplier = multiplier;
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

    public void UnregisterPeriodicCallback(Action callback) =>
        _deviceCallbacks.RemoveAll(x => x.Callback == callback);

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
        long baseTicks;
        if (_inEventService) {
            var now = _nowTicks();
            if (now > _serviceBaseTicks) {
                _serviceBaseTicks = now; // advance base if time progressed inside handler
            }

            baseTicks = _serviceBaseTicks;
        } else {
            baseTicks = _nowTicks();
            _serviceBaseTicks = baseTicks;
        }

        double effectiveDelayMs = delayMs / _timeMultiplier;
        long dueTicks = baseTicks + MsToTicks(effectiveDelayMs);

        var ev = new ScheduledEvent {
            Name = name,
            Handler = handler,
            Parameter = value,
            DueTicks = dueTicks,
            StartTicks = baseTicks,
            DelayMs = delayMs,
            EffectiveDelayMs = effectiveDelayMs,
            Canceled = false
        };
        _eventQueue.Enqueue(ev, ev.DueTicks);
        return ev;
    }

    public int RemoveEvents(Action<uint> handler) {
        int count = 0;
        foreach ((ScheduledEvent? e, long _) in _eventQueue.UnorderedItems) {
            if (!e.Canceled && e.Handler == handler) { e.Canceled = true; count++; }
        }

        return count;
    }

    public int RemoveSpecificEvents(Action<uint> handler, uint value) {
        int count = 0;
        foreach ((ScheduledEvent? e, long _) in _eventQueue.UnorderedItems) {
            if (!e.Canceled && e.Handler == handler && e.Parameter == value) { e.Canceled = true; count++; }
        }

        return count;
    }

    public void RunEventQueue() {
        long now = _nowTicks();
        _inEventService = true;
        if (_serviceBaseTicks == 0) {
            _serviceBaseTicks = now;
        }

        while (_eventQueue.TryPeek(out ScheduledEvent? ev, out var due)) {
            if (due > now) {
                break; // not yet due
            }

            _eventQueue.Dequeue();
            if (!ev.Canceled) {
                try {
                    _serviceBaseTicks = now; // base for chained events
                    ev.Handler(ev.Parameter);
                } catch (Exception ex) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("Timer event '{Name}' failed: {Error}", ev.Name, ex.Message);
                    }

                    throw;
                }
                now = _nowTicks();
            }
        }
        _inEventService = false;
    }

    private long MsToTicks(double ms) => (long)Math.Ceiling(ms * _ticksPerSecond / 1000.0);
}