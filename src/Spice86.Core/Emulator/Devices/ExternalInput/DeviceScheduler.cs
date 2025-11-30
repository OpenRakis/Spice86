namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Diagnostics;

// The new, abstract clock architecture. These should be moved to their own files.

/// <summary>
/// Represents a source of time within the emulation, abstracting away the underlying mechanism.
/// </summary>
public interface IEmulatedClock {
    /// <summary>
    /// Gets the current time in milliseconds.
    /// </summary>
    double CurrentTime { get; }

    /// <summary>
    /// Initializes or resets the clock.
    /// </summary>
    void Initialize();
}

/// <summary>
/// A real-time clock based on the system's Stopwatch, independent of CPU cycles for its progression.
/// </summary>
public class EmulatedClock : IEmulatedClock {
    private int _ticks;
    private readonly Stopwatch _stopwatch = new();
    private double _cachedTime;


    public void Initialize() {
        _stopwatch.Restart();
        _cachedTime = 0;
        _ticks = 0;
    }

    public double CurrentTime {
        get {
            // Stopwatch.GetTimestamp can be slow, so we only query it periodically.
            if (_ticks++ % 100 != 0) {
                return _cachedTime;
            }
            _cachedTime = _stopwatch.Elapsed.TotalMilliseconds;
            return _cachedTime;
        }
    }
}


/// <summary>
///     Manages deterministic scheduling of device events using an emulated clock.
/// </summary>
public class DeviceScheduler {
    private const int PicQueueSize = 8192;

    private readonly IEmulatedClock _clock;
    private readonly ILoggerService _logger;

    private readonly PriorityQueue<ScheduledEntry, double> _queue = new();
    private readonly Dictionary<EmulatedTimeEventHandler, List<ScheduledEntry>> _activeEventsByHandler = new();

    private bool _isServicingEvents;
    private double _activeEventScheduledTime;

    /// <summary>
    ///     Initializes a new scheduler.
    /// </summary>
    /// <param name="clock">The emulated clock that provides the master time source.</param>
    /// <param name="logger">Logger used for diagnostic reporting.</param>
    public DeviceScheduler(IEmulatedClock clock, ILoggerService logger) {
        _clock = clock;
        _logger = logger;
        Initialize();
    }

    /// <summary>
    ///     Resets the queue and clears all scheduled events.
    /// </summary>
    public void Initialize() {
        _queue.Clear();
        _activeEventsByHandler.Clear();
        _isServicingEvents = false;
        _activeEventScheduledTime = 0.0;
        _clock.Initialize();
    }

    /// <summary>
    ///     Schedules an event to fire after the specified delay.
    /// </summary>
    /// <param name="handler">Callback to invoke.</param>
    /// <param name="delay">Delay in milliseconds relative to the current time.</param>
    /// <param name="val">Value forwarded to the callback.</param>
    public void AddEvent(EmulatedTimeEventHandler handler, double delay, uint val = 0) {
        if (_queue.Count >= PicQueueSize) {
            if (_logger.IsEnabled(LogEventLevel.Error)) {
                _logger.Error("Event queue full when scheduling handler {Handler}", GetHandlerName(handler));
            }
            return;
        }

        double baseTime = _isServicingEvents ? _activeEventScheduledTime : _clock.CurrentTime;
        double absoluteScheduledTime = baseTime + delay;

        var entry = new ScheduledEntry {
            ScheduledTime = absoluteScheduledTime,
            Handler = handler,
            Value = val
        };

        _queue.Enqueue(entry, entry.ScheduledTime);

        if (!_activeEventsByHandler.TryGetValue(handler, out List<ScheduledEntry>? events)) {
            events = new List<ScheduledEntry>();
            _activeEventsByHandler[handler] = events;
        }
        events.Add(entry);
    }

    /// <summary>
    ///     Removes queued events matching both handler and value.
    /// </summary>
    public void RemoveSpecificEvents(EmulatedTimeEventHandler handler, uint val) {
        if (!_activeEventsByHandler.TryGetValue(handler, out List<ScheduledEntry>? events)) {
            return;
        }

        int cancelledCount = 0;
        foreach (ScheduledEntry entry in events) {
            if (entry.Value == val && !entry.IsCancelled) {
                entry.IsCancelled = true;
                cancelledCount++;
            }
        }
        
        if (cancelledCount > 0 && _logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Cancelled {CancelledCount} events for handler {Handler} with value {Value}", cancelledCount, GetHandlerName(handler), val);
        }
    }

    /// <summary>
    ///     Removes all queued events matching the provided handler.
    /// </summary>
    public void RemoveEvents(EmulatedTimeEventHandler handler) {
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
        _isServicingEvents = true;
        double currentTime = _clock.CurrentTime;

        while (_queue.TryPeek(out ScheduledEntry? entry, out double scheduledTime) && scheduledTime <= currentTime) {
            _queue.Dequeue();

            // Remove from active handler tracking
            if (_activeEventsByHandler.TryGetValue(entry.Handler!, out List<ScheduledEntry>? events)) {
                events.Remove(entry);
                if (events.Count == 0) {
                    _activeEventsByHandler.Remove(entry.Handler!);
                }
            }

            if (entry.IsCancelled) {
                continue;
            }

            _activeEventScheduledTime = entry.ScheduledTime;
            entry.Handler?.Invoke(entry.Value);
        }
        _isServicingEvents = false;
    }

    private static string GetHandlerName(EmulatedTimeEventHandler? handler) {
        if (handler == null) return "<null>";
        string name = handler.Method.Name;
        return string.IsNullOrEmpty(name) ? "<anonymous>" : name;
    }

    private sealed class ScheduledEntry {
        public EmulatedTimeEventHandler? Handler;
        public double ScheduledTime;
        public uint Value;
        public bool IsCancelled;
    }
}