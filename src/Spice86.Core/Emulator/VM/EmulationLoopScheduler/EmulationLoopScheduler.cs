namespace Spice86.Core.Emulator.VM.EmulationLoopScheduler;

using Serilog.Events;

using Spice86.Core.Emulator.VM.Clock;
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
public class EmulationLoopScheduler {
    private const int MaxQueueSize = 8192;

    private readonly IEmulatedClock _clock;
    private readonly ILoggerService _logger;

    private readonly PriorityQueue<ScheduledEntry, double> _queue = new();
    private readonly Dictionary<EventHandler, List<ScheduledEntry>> _activeEventsByHandler = new();
    private readonly EmulationLoopSchedulerMonitor _monitor;

    private bool _isServicingEvents;
    private double _activeEventScheduledTime;

    /// <summary>
    ///     Initializes a new scheduler.
    /// </summary>
    /// <param name="clock">The emulated clock that provides the master time source.</param>
    /// <param name="logger">Logger used for diagnostic reporting.</param>
    public EmulationLoopScheduler(IEmulatedClock clock, ILoggerService logger) {
        _clock = clock;
        _logger = logger;
        _monitor = new EmulationLoopSchedulerMonitor(logger);
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

        double baseTime = _isServicingEvents ? _activeEventScheduledTime : _clock.CurrentTimeMs;
        double absoluteScheduledTime = baseTime + delay;

        var entry = new ScheduledEntry(handler, absoluteScheduledTime, val, false);

        _queue.Enqueue(entry, entry.ScheduledTime);

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
            entry.IsCancelled = true;
        }

        if (events.Count > 0 && _logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Cancelled all {EventCount} events for handler {Handler}", events.Count, GetHandlerName(handler));
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
        double currentTime = _clock.CurrentTimeMs;

        while (_queue.TryPeek(out ScheduledEntry? entry, out double scheduledTime) && scheduledTime <= currentTime) {
            _queue.Dequeue();
            _monitor.OnEventExecuted(scheduledTime, currentTime, _queue.Count);
            // Remove from active handler tracking
            if (_activeEventsByHandler.TryGetValue(entry.Handler, out List<ScheduledEntry>? events)) {
                events.Remove(entry);
                if (events.Count == 0) {
                    _activeEventsByHandler.Remove(entry.Handler);
                }
            }

            if (entry.IsCancelled) {
                continue;
            }

            _activeEventScheduledTime = entry.ScheduledTime;
            entry.Handler.Invoke(entry.Value);
        }
        _isServicingEvents = false;
    }

    private static string GetHandlerName(EventHandler? handler) {
        if (handler == null) return "<null>";
        string name = handler.Method.Name;
        return string.IsNullOrEmpty(name) ? "<anonymous>" : name;
    }

    private sealed class ScheduledEntry(EventHandler handler, double scheduledTime, uint value, bool isCancelled) {
        public EventHandler Handler { get; } = handler;
        public double ScheduledTime { get; } = scheduledTime;
        public uint Value { get; } = value;
        public bool IsCancelled { get; set; } = isCancelled;
    }
}
