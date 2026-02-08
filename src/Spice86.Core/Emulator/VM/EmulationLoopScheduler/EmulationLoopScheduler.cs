namespace Spice86.Core.Emulator.VM.EmulationLoopScheduler;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
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
    private readonly State _state;

    private readonly FastPriorityQueue<ScheduledEntry> _queue = new(MaxQueueSize);
    private readonly Stack<ScheduledEntry> _entryPool = new(MaxQueueSize);
    private readonly Dictionary<EventHandler, List<ScheduledEntry>> _activeEventsByHandler = new();
    private readonly EmulationLoopSchedulerMonitor _monitor;

    /// <summary>
    ///     Linked list of tick handlers called every tick.
    /// </summary>
    private readonly LinkedList<TickHandler> _tickHandlers = new();

    /// <summary>
    ///     Tracks the last tick time to determine when to fire tick handlers.
    /// </summary>
    private double _lastTickTimeMs;

    /// <summary>
    ///     Cycle threshold: the next absolute cycle count at which events need processing.
    ///     Matches DOSBox's pattern where PIC_RunQueue computes CPU_Cycles = cycles until
    ///     next event, and cpudecoder runs that many instructions without rechecking.
    ///     This eliminates the expensive FullIndex computation on every instruction.
    /// </summary>
    private long _nextCheckCycles;

    private bool _isServicingEvents;
    private double _activeEventScheduledTime;

    /// <summary>
    ///     Initializes a new scheduler.
    /// </summary>
    /// <param name="clock">The emulated clock that provides the master time source.</param>
    /// <param name="state">CPU state, used to read the current cycle count for fast-path gating.</param>
    /// <param name="logger">Logger used for diagnostic reporting.</param>
    public EmulationLoopScheduler(IEmulatedClock clock, State state, ILoggerService logger) {
        _clock = clock;
        _state = state;
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

        // If this event fires sooner than the current threshold, lower it.
        // Matches DOSBox AddEntry() which sets CPU_Cycles=0 when a new event
        // is earlier than the current batch boundary.
        long eventCycles = _clock.ConvertTimeToCycles(absoluteScheduledTime);
        if (eventCycles < _nextCheckCycles) {
            _nextCheckCycles = eventCycles;
        }
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

        if (count > 0) {
            RecomputeNextCheckCycles();
            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug("Cancelled all {EventCount} events for handler {Handler}", count, GetHandlerName(handler));
            }
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
    ///     The fast path is a single long comparison per instruction, matching DOSBox's
    ///     pattern where PIC_RunQueue sets CPU_Cycles to the batch size and cpudecoder
    ///     runs that many instructions without rechecking.
    /// </summary>
    public void ProcessEvents() {
        // Cheap tick handler check (~4 ops: uint read + double compare).
        // Tick handlers fire based on TickCount which advances in RegulateCycles,
        // so this detects ticks one instruction after the boundary — same as DOSBox.
        double elapsedMs = _clock.ElapsedTimeMs;
        if (elapsedMs >= _lastTickTimeMs + 1.0) {
            while (elapsedMs >= _lastTickTimeMs + 1.0) {
                _lastTickTimeMs += 1.0;
                LinkedListNode<TickHandler>? ticker = _tickHandlers.First;
                while (ticker is not null) {
                    LinkedListNode<TickHandler>? nextTicker = ticker.Next;
                    ticker.Value.Invoke();
                    ticker = nextTicker;
                }
            }
        }

        // Cycle-gated event check: single long comparison on fast path.
        // This eliminates the expensive FullIndex computation (double division)
        // on every instruction. Events are only checked when the CPU reaches the
        // cycle count where the next queued event fires.
        if (_state.Cycles < _nextCheckCycles) {
            return;
        }

        // Expensive path: compute FullIndex and process due events.
        if (_queue.Count == 0) {
            _nextCheckCycles = long.MaxValue;
            return;
        }

        double currentTime = _clock.FullIndex;

        _isServicingEvents = true;

        while (_queue.Count > 0 && _queue.First.ScheduledTime <= currentTime) {
            ScheduledEntry entry = _queue.Dequeue();
            _monitor.OnEventExecuted(entry.ScheduledTime, currentTime, _queue.Count);

            if (_activeEventsByHandler.TryGetValue(entry.Handler, out List<ScheduledEntry>? events)) {
                events.Remove(entry);
                if (events.Count == 0) {
                    _activeEventsByHandler.Remove(entry.Handler);
                }
            }

            _activeEventScheduledTime = entry.ScheduledTime;
            try {
                entry.Handler.Invoke(entry.Value);
            } finally {
                ReturnEntry(entry);
            }
        }

        _isServicingEvents = false;

        RecomputeNextCheckCycles();
    }

    /// <summary>
    ///     Recomputes the cycle threshold for the next event check.
    ///     Matches DOSBox PIC_RunQueue's batch size computation:
    ///     CPU_Cycles = min(cycles_until_next_event, cycles_remaining_in_tick).
    /// </summary>
    private void RecomputeNextCheckCycles() {
        if (_queue.Count == 0) {
            _nextCheckCycles = long.MaxValue;
            return;
        }
        _nextCheckCycles = _clock.ConvertTimeToCycles(_queue.First.ScheduledTime);
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
