namespace Spice86.Core.Emulator.VM.EmulationLoopScheduler;

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
///     Represents the callback signature for tick handlers that are called every tick.
///     Reference: DOSBox staging src/hardware/pic.cpp TIMER_TickHandler
/// </summary>
public delegate void TickHandler();

/// <summary>
///     Manages deterministic scheduling of emulation events using an emulated clock.
/// </summary>
public class EmulationLoopScheduler {
    public const int MaxQueueSize = 8192;

    private readonly IEmulatedClock _clock;
    private readonly ILoggerService _logger;

    private readonly FastPriorityQueue<ScheduledEntry> _queue = new(MaxQueueSize);
    private readonly Stack<ScheduledEntry> _entryPool = new(MaxQueueSize);
    private readonly Dictionary<EventHandler, List<ScheduledEntry>> _activeEventsByHandler = new();
    private readonly EmulationLoopSchedulerMonitor _monitor;

    /// <summary>
    ///     Linked list of tick handlers called every tick.
    ///     Reference: DOSBox staging src/hardware/pic.cpp firstticker linked list
    /// </summary>
    private readonly LinkedList<TickHandler> _tickHandlers = new();

    /// <summary>
    ///     Tracks the last tick time to determine when to fire tick handlers.
    ///     Reference: DOSBox staging calls TIMER_AddTick() every ~1ms (1000 cycles at 1MHz)
    /// </summary>
    private double _lastTickTimeMs;

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

        double baseTime = _isServicingEvents ? _activeEventScheduledTime : _clock.FullIndex;
        double absoluteScheduledTime = baseTime + delay;

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
    ///     Registers a tick handler to be called every tick.
    ///     Reference: DOSBox staging src/hardware/pic.cpp TIMER_AddTickHandler()
    /// </summary>
    /// <param name="handler">The handler to call every tick.</param>
    public void AddTickHandler(TickHandler handler) {
        // Add to front of list (matches DOSBox: newticker->next=firstticker; firstticker=newticker)
        _tickHandlers.AddFirst(handler);

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Added tick handler {Handler}", handler.Method.Name);
        }
    }

    /// <summary>
    ///     Removes a previously registered tick handler.
    ///     Reference: DOSBox staging src/hardware/pic.cpp TIMER_DelTickHandler()
    /// </summary>
    /// <param name="handler">The handler to remove.</param>
    public void DelTickHandler(TickHandler handler) {
        bool removed = _tickHandlers.Remove(handler);

        if (removed && _logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Removed tick handler {Handler}", handler.Method.Name);
        }
    }

    /// <summary>
    ///     Executes all events that are due as of the current time provided by the clock.
    ///     Also invokes all registered tick handlers every 1ms.
    ///     Reference: DOSBox staging src/hardware/pic.cpp TIMER_AddTick() + PIC_RunQueue()
    /// </summary>
    public void ProcessEvents() {
        // Use ElapsedTimeMs for tick handler detection — this is cheap (integer read for EmulatedClock).
        // Reference: DOSBox calls TIMER_AddTick() from normal_loop() at integer tick boundaries,
        // separate from the sub-ms event processing in PIC_RunQueue().
        double elapsedMs = _clock.ElapsedTimeMs;

        // Call tick handlers every 1ms (like DOSBox's TIMER_AddTick)
        // Reference: src/hardware/pic.cpp lines 607-624
        while (elapsedMs >= _lastTickTimeMs + 1.0) {
            _lastTickTimeMs += 1.0;

            // Call our list of ticker handlers
            // Reference: DOSBox iterates through firstticker linked list
            LinkedListNode<TickHandler>? ticker = _tickHandlers.First;
            while (ticker is not null) {
                LinkedListNode<TickHandler>? nextTicker = ticker.Next;
                ticker.Value.Invoke();
                ticker = nextTicker;
            }
        }

        // Only compute expensive FullIndex when there are queued events.
        // This is the key optimization: on the fast path (no events), we skip
        // the GetCycleProgressionPercentage() double division entirely.
        // Reference: DOSBox PIC_RunQueue() only checks events when they exist in pic_queue.
        if (_queue.Count == 0) {
            return;
        }

        double currentTime = _clock.FullIndex;

        _isServicingEvents = true;

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
