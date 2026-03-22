namespace Spice86.Core.Emulator.VM.DeviceScheduler;

using Serilog.Events;

using Spice86.Core.Emulator.VM.Clock;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

using Priority_Queue;

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

    private readonly FastPriorityQueue<ScheduledEntry> _queue = new(MaxQueueSize);
    private readonly Stack<ScheduledEntry> _entryPool = new(MaxQueueSize);
    private readonly Dictionary<EventHandler, List<ScheduledEntry>> _activeEventsByHandler = new();
    private readonly DeviceSchedulerMonitor _monitor;

    private bool _isServicingEvents;
    private double _activeEventScheduledTime;
    private double _loopEntryTime;

    /// <summary>
    ///     Gets the scheduled time of the next queued event, or <see cref="double.MaxValue"/> if the queue is empty.
    /// </summary>
    public double? NextEventTime => _queue.Count > 0 ? _queue.First.ScheduledTime : null;

    /// <summary>
    ///     Initializes a new scheduler with a descriptive instance name.
    /// </summary>
    /// <param name="clock">The emulated clock that provides the master time source.</param>
    /// <param name="logger">Logger used for diagnostic reporting.</param>
    /// <param name="instanceName">The name used to identify this scheduler in logs.</param>
    public DeviceScheduler(IEmulatedClock clock, ILoggerService logger, string instanceName) {
        _clock = clock;
        _logger = logger;
        _monitor = new DeviceSchedulerMonitor(logger, instanceName);
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
            if (candidate >= _loopEntryTime) {
                absoluteScheduledTime = candidate;
            } else {
                absoluteScheduledTime = _loopEntryTime + MinFutureOffsetMs;
            }
        }

        ScheduledEntry entry = GetEntry(handler, absoluteScheduledTime, val);

        _queue.Enqueue(entry, (float)absoluteScheduledTime);

        if (!_activeEventsByHandler.TryGetValue(handler, out List<ScheduledEntry>? events)) {
            events = new List<ScheduledEntry>();
            _activeEventsByHandler[handler] = events;
        }

        events.Add(entry);
    }

    /// <summary>
    ///     Removes all queued events matching the provided handler.
    /// </summary>
    public void RemoveEvents(EventHandler handler) {
        if (!_activeEventsByHandler.TryGetValue(handler, out List<ScheduledEntry>? events)) {
            return;
        }

        foreach (ScheduledEntry entry in events) {
            if (_queue.Contains(entry)) {
                _queue.Remove(entry);
                ReturnEntry(entry);
            }
        }

        int count = events.Count;
        events.Clear();
        _activeEventsByHandler.Remove(handler);

        if (count > 0 && _logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Cancelled all {EventCount} events for handler {Handler}", count, GetHandlerName(handler));
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

        while (_queue.Count > 0 && _queue.First.ScheduledTime <= currentTime) {
            ScheduledEntry entry = _queue.Dequeue();
            _monitor.OnEventExecuted(entry.ScheduledTime, currentTime, _queue.Count);

            // Remove from active handler tracking
            if (_activeEventsByHandler.TryGetValue(entry.Handler, out List<ScheduledEntry>? events)) {
                events.Remove(entry);
                if (events.Count == 0) {
                    _activeEventsByHandler.Remove(entry.Handler);
                }
            }

            // Important for events that re-schedules another event, to ensure it will fire at the correct time not taking into account lag
            _activeEventScheduledTime = entry.ScheduledTime;
            try {
                entry.Handler.Invoke(entry.Value);
            } finally {
                ReturnEntry(entry);
            }
        }

        _isServicingEvents = false;
    }

    private ScheduledEntry GetEntry(EventHandler handler, double scheduledTime, uint value) {
        if (_entryPool.TryPop(out ScheduledEntry? entry)) {
            entry.Handler = handler;
            entry.ScheduledTime = scheduledTime;
            entry.Value = value;
            return entry;
        }

        return new ScheduledEntry(handler, scheduledTime, value);
    }

    private void ReturnEntry(ScheduledEntry entry) {
        _entryPool.Push(entry);
    }

    private static string GetHandlerName(EventHandler? handler) {
        if (handler == null) return "<null>";
        string name = handler.Method.Name;
        return string.IsNullOrEmpty(name) ? "<anonymous>" : name;
    }

    private sealed class ScheduledEntry : FastPriorityQueueNode {
        public EventHandler Handler { get; set; }
        public double ScheduledTime { get; set; }
        public uint Value { get; set; }

        public ScheduledEntry(EventHandler handler, double scheduledTime, uint value) {
            Handler = handler;
            ScheduledTime = scheduledTime;
            Value = value;
        }
    }
}
