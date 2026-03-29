namespace Spice86.Core.Emulator.VM.DeviceScheduler;

using Serilog.Events;

using Spice86.Core.Emulator.VM.Clock;
using Spice86.Shared.Collections;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

/// <summary>
///     Represents the callback signature invoked when a queued event fires.
/// </summary>
/// <param name="value">Controller-supplied value associated with the event.</param>
public delegate void EventHandler(uint value);

/// <summary>
///     Manages deterministic scheduling of emulation events using an emulated clock.
/// </summary>
public class DeviceScheduler {
    public const int MaxQueueSize = 8192;

    private readonly IEmulatedClock _clock;
    private readonly ILoggerService _logger;

    private readonly TimePriorityQueue<ScheduledEntry> _queue = new(MaxQueueSize);
    private readonly Stack<ScheduledEntry> _entryPool = new(MaxQueueSize);
    
    private readonly DeviceSchedulerMonitor? _monitor;

    private bool _isServicingEvents;
    private double _activeEventScheduledTime;
    private double _loopEntryTime;

    /// <summary>
    ///     Gets the scheduled time of the next queued event, or <see cref="double.MaxValue"/> if the queue is empty.
    /// </summary>
    public double? NextEventTime => _queue.Count > 0 ? _queue.First.Priority : null;

    /// <summary>
    ///     Initializes a new scheduler with a descriptive instance name.
    /// </summary>
    /// <param name="clock">The emulated clock that provides the master time source.</param>
    /// <param name="logger">Logger used for diagnostic reporting.</param>
    /// <param name="instanceName">The name used to identify this scheduler in logs.</param>
    public DeviceScheduler(IEmulatedClock clock, ILoggerService logger, string instanceName) {
        _clock = clock;
        _logger = logger;
        _monitor = _logger.IsEnabled(LogEventLevel.Debug) ? new DeviceSchedulerMonitor(logger, instanceName) : null;
    }

    /// <summary>
    ///     Schedules an event to fire after the specified delay.
    /// </summary>
    /// <param name="handler">Callback to invoke.</param>
    /// <param name="delay">Delay in milliseconds relative to the current time.</param>
    /// <param name="val">Value forwarded to the callback.</param>
    public void AddEvent(EventHandler handler, double delay, uint val = 0) {
        if (_queue.Count >= MaxQueueSize) {
            if (_logger.IsEnabled(LogEventLevel.Error)) {
                _logger.Error("Event queue full when scheduling handler {Handler}", GetHandlerName(handler));
            }

            return;
        }

        // Compute absolute scheduled time taking into account we're currently servicing events.
        // Use the active event's scheduled time + delay when that yields a time in the future
        // relative to the loop entry. Otherwise schedule just after the loop entry to avoid
        // putting events in the past (prevents infinite re-queuing on slow machines).
        const double MinFutureOffsetMs = 0.0001;
        double absoluteScheduledTime;
        if (!_isServicingEvents) {
            absoluteScheduledTime = _clock.ElapsedTimeMs + delay;
        } else {
            double candidate = _activeEventScheduledTime + delay;
            absoluteScheduledTime = candidate >= _loopEntryTime
                ? candidate
                : _loopEntryTime + MinFutureOffsetMs;
        }

        ScheduledEntry entry = GetEntry(handler, val);

        _queue.Enqueue(entry, absoluteScheduledTime);
    }

    /// <summary>
    ///     Removes all queued events matching the provided handler.
    /// </summary>
    public void RemoveEvents(EventHandler handler) {
        if (_queue.Count == 0) {
            return;
        }

        int removedCount = 0;
        List<ScheduledEntry> toRemove = new();

        for (int i = 1; i <= _queue.Count; i++) {
            ScheduledEntry entry = _queue.NodeAt(i);
            if (entry.Handler == handler) {
                toRemove.Add(entry);
            }
        }

        foreach (ScheduledEntry entry in toRemove) {
            _queue.Remove(entry);
            ReturnEntry(entry);
            removedCount++;
        }

        if (removedCount > 0 && _logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Cancelled all {EventCount} events for handler {Handler}", removedCount, GetHandlerName(handler));
        }
    }

    /// <summary>
    ///     Executes all events that are due as of the current time provided by the clock.
    /// </summary>
    public void ProcessEvents() {
        if (_queue.Count == 0) {
            return;
        }

        _isServicingEvents = true;
        double currentTime = _clock.ElapsedTimeMs;
        _loopEntryTime = currentTime;

        while (_queue.Count > 0 && _queue.First.Priority <= currentTime) {
            ScheduledEntry entry = _queue.Dequeue();
            _monitor?.OnEventExecuted(entry.Priority, currentTime, _queue.Count);

            // Important for events that re-schedules another event, to ensure it will fire at the correct time not taking into account lag
            _activeEventScheduledTime = entry.Priority;
            try {
                entry.Handler.Invoke(entry.Value);
            } finally {
                ReturnEntry(entry);
            }
        }

        _isServicingEvents = false;
    }

    private ScheduledEntry GetEntry(EventHandler handler, uint value) {
        if (_entryPool.TryPop(out ScheduledEntry? entry)) {
            entry.Handler = handler;
            entry.Value = value;
            return entry;
        }

        return new ScheduledEntry(handler, value);
    }

    private void ReturnEntry(ScheduledEntry entry) {
        _entryPool.Push(entry);
    }

    private static string GetHandlerName(EventHandler? handler) {
        if (handler == null) return "<null>";
        string name = handler.Method.Name;
        return string.IsNullOrEmpty(name) ? "<anonymous>" : name;
    }

    private sealed class ScheduledEntry : TimePriorityQueueNode {
        public EventHandler Handler { get; set; }
        public uint Value { get; set; }

        public ScheduledEntry(EventHandler handler, uint value) {
            Handler = handler;
            Value = value;
        }
    }
}
