namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Serilog.Events;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
///     Manages deterministic scheduling of PIC events relative to the CPU tick index.
/// </summary>
/// <remarks>
///     Pools entries to avoid allocations and relies on the shared <see cref="PicPitCpuState" /> for cycle accounting.
/// </remarks>
internal sealed class PicEventQueue {
    private const int PicQueueSize = 8192; // Larger value from DosBox-X. Staging uses 512.
    private readonly PicPitCpuState _cpuState;
    private readonly PicEntry[] _entryPool = new PicEntry[PicQueueSize];

    private readonly ILoggerService _logger;

    private PicEntry? _freeEntry;
    private bool _inEventService;
    private PicEntry? _nextEntry;
    private double _srvLag; // Captures the active entry index while the queue is executing handlers.

    /// <summary>
    ///     Initializes a new queue bound to the provided CPU state and logger.
    /// </summary>
    /// <param name="cpuState">Shared CPU timing state that provides cycle counters.</param>
    /// <param name="logger">Logger used for diagnostic reporting.</param>
    public PicEventQueue(PicPitCpuState cpuState, ILoggerService logger) {
        _cpuState = cpuState;
        _logger = logger;
        Initialize();
    }

    /// <summary>
    ///     Resets the queue and returns all entries to the free list.
    /// </summary>
    /// <remarks>
    ///     Reinitialises the pool to its pristine state, mirroring the bootstrap performed during construction.
    /// </remarks>
    public void Initialize() {
        for (int i = 0; i < _entryPool.Length; i++) {
            _entryPool[i] = new PicEntry();
        }

        for (int i = 0; i < _entryPool.Length - 1; i++) {
            _entryPool[i].Next = _entryPool[i + 1];
        }

        _entryPool[^1].Next = null;
        _freeEntry = _entryPool[0];
        _nextEntry = null;
        _inEventService = false;
        _srvLag = 0.0;
    }

    /// <summary>
    ///     Schedules an event to fire after the specified fractional tick delay.
    /// </summary>
    /// <param name="handler">Callback to invoke.</param>
    /// <param name="delay">Delay expressed in tick units relative to the current index.</param>
    /// <param name="val">Value forwarded to the callback.</param>
    /// <remarks>
    ///     When invoked from inside <see cref="RunQueue" />, the new entry inherits the in-flight index instead of the
    ///     current tick position, which preserves ordering.
    /// </remarks>
    public void AddEvent(PicEventHandler handler, double delay, uint val = 0) {
        if (_freeEntry == null) {
            if (_logger.IsEnabled(LogEventLevel.Error)) {
                string handlerName = GetHandlerName(handler);
                _logger.Error(
                    "Event queue full when scheduling handler {Handler} (delay {DelayTicks}, value {Value})",
                    handlerName,
                    delay,
                    val);
            }

            return;
        }

        PicEntry? entry = _freeEntry;
        _freeEntry = _freeEntry!.Next;

        entry.Index = (_inEventService ? _srvLag : _cpuState.GetTickIndex()) + delay;
        entry.Handler = handler;
        entry.Value = val;

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            string handlerName = GetHandlerName(handler);
            _logger.Verbose(
                "Queued PIC event {Handler} with delay {DelayTicks}, scheduled index {Index}, value {Value}",
                handlerName,
                delay,
                entry.Index,
                val);
        }

        AddEntry(entry);
    }

    /// <summary>
    ///     Removes queued events matching both handler and value.
    /// </summary>
    /// <param name="handler">Handler to match.</param>
    /// <param name="val">Value to match.</param>
    public void RemoveSpecificEvents(PicEventHandler handler, uint val) {
        PicEntry? entry = _nextEntry;
        PicEntry? prevEntry = null;
        int removedCount = 0;

        while (entry != null) {
            if (entry.Handler == handler && entry.Value == val) {
                PicEntry? next = entry.Next;
                if (prevEntry != null) {
                    prevEntry.Next = next;
                } else {
                    _nextEntry = next;
                }

                entry.Next = _freeEntry;
                _freeEntry = entry;
                removedCount++;
                entry = next;
                continue;
            }

            prevEntry = entry;
            entry = entry.Next;
        }

        if (removedCount > 0 && _logger.IsEnabled(LogEventLevel.Debug)) {
            string handlerName = GetHandlerName(handler);
            _logger.Debug(
                "Removed {RemovedCount} PIC events for handler {Handler} with value {Value}",
                removedCount,
                handlerName,
                val);
        }
    }

    /// <summary>
    ///     Removes all queued events matching the provided handler.
    /// </summary>
    /// <param name="handler">Handler to remove.</param>
    public void RemoveEvents(PicEventHandler handler) {
        PicEntry? entry = _nextEntry;
        PicEntry? prevEntry = null;
        int removedCount = 0;

        while (entry != null) {
            if (entry.Handler == handler) {
                PicEntry? next = entry.Next;
                if (prevEntry != null) {
                    prevEntry.Next = next;
                } else {
                    _nextEntry = next;
                }

                entry.Next = _freeEntry;
                _freeEntry = entry;
                removedCount++;
                entry = next;
                continue;
            }

            prevEntry = entry;
            entry = entry.Next;
        }

        if (removedCount > 0 && _logger.IsEnabled(LogEventLevel.Debug)) {
            string handlerName = GetHandlerName(handler);
            _logger.Debug(
                "Removed {RemovedCount} PIC events for handler {Handler}",
                removedCount,
                handlerName);
        }
    }

    /// <summary>
    ///     Executes due events, updates cycle counters, and prepares the next wake-up point.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when work was performed or remains queued; otherwise <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     Ensures the CPU cycle counters are normalized before servicing entries and recalculates the next wake-up time
    ///     after draining ready handlers.
    /// </remarks>
    public bool RunQueue() {
        _cpuState.CyclesLeft += _cpuState.Cycles;
        _cpuState.Cycles = 0;
        if (_cpuState.CyclesLeft <= 0) {
            return false;
        }

        double indexNd = _cpuState.TickIndexNd;
        double cyclesMax = _cpuState.CyclesMax;

        _inEventService = true;
        int processedCount = 0;
        while (_nextEntry != null &&
               _nextEntry.Index * cyclesMax <= indexNd) {
            PicEntry? entry = _nextEntry;
            _nextEntry = entry.Next;

            _srvLag = entry.Index;
            entry.Handler?.Invoke(entry.Value);
            processedCount++;

            entry.Next = _freeEntry;
            _freeEntry = entry;
        }

        _inEventService = false;

        int scheduledCycles = 0;
        if (_nextEntry != null && _cpuState.CyclesLeft > 0) {
            double targetIndexNd = _nextEntry.Index * cyclesMax;
            int cyclesToNext = (int)(targetIndexNd - indexNd);
            if (cyclesToNext <= 0) {
                cyclesToNext = 1;
            }

            scheduledCycles = cyclesToNext <= _cpuState.CyclesLeft ? cyclesToNext : _cpuState.CyclesLeft;
        } else if (_cpuState.CyclesLeft > 0 && _nextEntry == null) {
            scheduledCycles = _cpuState.CyclesLeft;
        }

        _cpuState.Cycles = scheduledCycles;
        _cpuState.CyclesLeft -= scheduledCycles;

        if (processedCount > 0 && _logger.IsEnabled(LogEventLevel.Debug)) {
            double? nextIndex = _nextEntry?.Index;
            _logger.Debug(
                "Processed {ProcessedCount} PIC events; scheduled {ScheduledCycles} cycles; remaining cycles {RemainingCycles}; next index {NextIndex}",
                processedCount,
                scheduledCycles,
                _cpuState.CyclesLeft,
                nextIndex);
        }

        return _cpuState.Cycles > 0 || _cpuState.CyclesLeft > 0;
    }

    /// <summary>
    ///     Decrements pending event indices to account for a full tick elapsing.
    /// </summary>
    /// <remarks>
    ///     Each entry stores its delay in 1.0 tick units, so subtracting one advances the schedule by a full
    ///     millisecond tick.
    /// </remarks>
    public void DecrementIndicesForTick() {
        PicEntry? entry = _nextEntry;
        while (entry != null) {
            entry.Index -= 1.0f;
            entry = entry.Next;
        }
    }

    /// <summary>
    ///     Inserts the provided entry into the queue, preserving ordering and updating CPU cycle scheduling.
    /// </summary>
    /// <param name="entry">Entry retrieved from the pool that should be enqueued.</param>
    private void AddEntry(PicEntry entry) {
        // Maintain the list in ascending order of the scheduled index.
        PicEntry? findEntry = _nextEntry;
        if (findEntry == null) {
            entry.Next = null;
            _nextEntry = entry;
        } else if (findEntry.Index > entry.Index) {
            _nextEntry = entry;
            entry.Next = findEntry;
        } else {
            while (findEntry != null) {
                if (findEntry.Next != null) {
                    if (findEntry.Next.Index > entry.Index) {
                        entry.Next = findEntry.Next;
                        findEntry.Next = entry;
                        break;
                    }

                    findEntry = findEntry.Next;
                } else {
                    entry.Next = findEntry.Next;
                    findEntry.Next = entry;
                    break;
                }
            }
        }

        int cycles = _cpuState.MakeCycles(_nextEntry!.Index - _cpuState.GetTickIndex());
        if (cycles >= _cpuState.Cycles) {
            return;
        }

        _cpuState.CyclesLeft += _cpuState.Cycles;
        _cpuState.Cycles = 0;
    }

    /// <summary>
    ///     Provides a readable name for a PIC event handler to enrich log statements.
    /// </summary>
    /// <param name="handler">The delegate whose name is requested.</param>
    /// <returns>The method name when available; otherwise a placeholder.</returns>
    private static string GetHandlerName(PicEventHandler? handler) {
        if (handler == null) {
            return "<null>";
        }

        string name = handler.Method.Name;
        return string.IsNullOrEmpty(name) ? "<anonymous>" : name;
    }

    /// <summary>
    ///     Represents a pooled event entry associated with the PIC.
    /// </summary>
    private sealed class PicEntry {
        public PicEventHandler? Handler; // Callback stored for dispatch.
        public double Index; // Fractional tick deadline.
        public PicEntry? Next;
        public uint Value; // Value forwarded to the handler.
    }
}