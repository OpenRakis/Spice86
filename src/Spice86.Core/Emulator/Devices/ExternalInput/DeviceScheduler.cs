namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Serilog.Events;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
///     Manages deterministic scheduling of device events relative to the CPU tick index.
/// </summary>
/// <remarks>
///     Pools entries to avoid allocations and relies on the shared <see cref="ExecutionStateSlice" /> for cycle accounting.
/// </remarks>
internal sealed class DeviceScheduler {
    private const int PicQueueSize = 8192; // Larger value from DosBox-X. Staging uses 512.
    private readonly ExecutionStateSlice _executionStateSlice;
    private readonly ScheduledEntry[] _entryPool = new ScheduledEntry[PicQueueSize];

    private readonly ILoggerService _logger;

    private ScheduledEntry? _freeEntry;
    private bool _isServicingEvents;
    private ScheduledEntry? _nextEntry;
    /// <summary>
    /// Captures the active entry index while the queue is executing handlers.
    /// </summary>
    private double _activeEntryIndex;

    /// <summary>
    ///     Initializes a new queue bound to the provided CPU state and logger.
    /// </summary>
    /// <param name="cpuState">Shared CPU timing state that provides cycle counters.</param>
    /// <param name="logger">Logger used for diagnostic reporting.</param>
    public DeviceScheduler(ExecutionStateSlice cpuState, ILoggerService logger) {
        _executionStateSlice = cpuState;
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
            _entryPool[i] = new ScheduledEntry();
        }

        for (int i = 0; i < _entryPool.Length - 1; i++) {
            _entryPool[i].Next = _entryPool[i + 1];
        }

        _entryPool[^1].Next = null;
        _freeEntry = _entryPool[0];
        _nextEntry = null;
        _isServicingEvents = false;
        _activeEntryIndex = 0.0;
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
    public void AddEvent(DeviceEventHandler handler, double delay, uint val = 0) {
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

        ScheduledEntry? entry = _freeEntry;
        _freeEntry = _freeEntry!.Next;

        entry.Deadline = (_isServicingEvents ? _activeEntryIndex : _executionStateSlice.NormalizedSliceProgress) + delay;
        entry.Handler = handler;
        entry.Value = val;

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            string handlerName = GetHandlerName(handler);
            _logger.Verbose(
                "Queued Device scheduler event {Handler} with delay {DelayTicks}, scheduled index {Index}, value {Value}",
                handlerName,
                delay,
                entry.Deadline,
                val);
        }

        AddEntry(entry);
    }

    /// <summary>
    ///     Removes queued events matching both handler and value.
    /// </summary>
    /// <param name="handler">Handler to match.</param>
    /// <param name="val">Value to match.</param>
    public void RemoveSpecificEvents(DeviceEventHandler handler, uint val) {
        ScheduledEntry? entry = _nextEntry;
        ScheduledEntry? prevEntry = null;
        int removedCount = 0;

        while (entry != null) {
            if (entry.Handler == handler && entry.Value == val) {
                ScheduledEntry? next = entry.Next;
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
                "Removed {RemovedCount} device events for handler {Handler} with value {Value}",
                removedCount,
                handlerName,
                val);
        }
    }

    /// <summary>
    ///     Removes all queued events matching the provided handler.
    /// </summary>
    /// <param name="handler">Handler to remove.</param>
    public void RemoveEvents(DeviceEventHandler handler) {
        ScheduledEntry? entry = _nextEntry;
        ScheduledEntry? prevEntry = null;
        int removedCount = 0;

        while (entry != null) {
            if (entry.Handler == handler) {
                ScheduledEntry? next = entry.Next;
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
                "Removed {RemovedCount} device events for handler {Handler}",
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
        _executionStateSlice.CyclesLeft += _executionStateSlice.CyclesUntilReevaluation;
        _executionStateSlice.CyclesUntilReevaluation = 0;
        if (_executionStateSlice.CyclesLeft <= 0) {
            return false;
        }

        double indexNd = _executionStateSlice.CyclesConsumed;
        double cyclesMax = _executionStateSlice.CyclesAllocated;

        _isServicingEvents = true;
        int processedCount = 0;
        while (_nextEntry != null &&
               _nextEntry.Deadline * cyclesMax <= indexNd) {
            ScheduledEntry? entry = _nextEntry;
            _nextEntry = entry.Next;

            _activeEntryIndex = entry.Deadline;
            entry.Handler?.Invoke(entry.Value);
            processedCount++;

            entry.Next = _freeEntry;
            _freeEntry = entry;
        }

        _isServicingEvents = false;

        int scheduledCycles = 0;
        if (_nextEntry != null && _executionStateSlice.CyclesLeft > 0) {
            double targetIndexNd = _nextEntry.Deadline * cyclesMax;
            int cyclesToNext = (int)(targetIndexNd - indexNd);
            if (cyclesToNext <= 0) {
                cyclesToNext = 1;
            }

            scheduledCycles = cyclesToNext <= _executionStateSlice.CyclesLeft ? cyclesToNext : _executionStateSlice.CyclesLeft;
        } else if (_executionStateSlice.CyclesLeft > 0 && _nextEntry == null) {
            scheduledCycles = _executionStateSlice.CyclesLeft;
        }

        _executionStateSlice.CyclesUntilReevaluation = scheduledCycles;
        _executionStateSlice.CyclesLeft -= scheduledCycles;

        if (processedCount > 0 && _logger.IsEnabled(LogEventLevel.Debug)) {
            double? nextIndex = _nextEntry?.Deadline;
            _logger.Debug(
                "Processed {ProcessedCount} device events; scheduled {ScheduledCycles} cycles; remaining cycles {RemainingCycles}; next index {NextIndex}",
                processedCount,
                scheduledCycles,
                _executionStateSlice.CyclesLeft,
                nextIndex);
        }

        return _executionStateSlice.CyclesUntilReevaluation > 0 || _executionStateSlice.CyclesLeft > 0;
    }

    /// <summary>
    ///     Decrements pending event indices to account for a full tick elapsing.
    /// </summary>
    /// <remarks>
    ///     Each entry stores its delay in 1.0 tick units, so subtracting one advances the schedule by a full
    ///     millisecond tick.
    /// </remarks>
    public void DecrementIndicesForTick() {
        ScheduledEntry? entry = _nextEntry;
        while (entry != null) {
            entry.Deadline -= 1.0f;
            entry = entry.Next;
        }
    }

    /// <summary>
    ///     Inserts the provided entry into the queue, preserving ordering and updating CPU cycle scheduling.
    /// </summary>
    /// <param name="entry">Entry retrieved from the pool that should be enqueued.</param>
    private void AddEntry(ScheduledEntry entry) {
        // Maintain the list in ascending order of the scheduled index.
        ScheduledEntry? findEntry = _nextEntry;
        if (findEntry == null) {
            entry.Next = null;
            _nextEntry = entry;
        } else if (findEntry.Deadline > entry.Deadline) {
            _nextEntry = entry;
            entry.Next = findEntry;
        } else {
            while (findEntry != null) {
                if (findEntry.Next != null) {
                    if (findEntry.Next.Deadline > entry.Deadline) {
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

        int cycles = _executionStateSlice.ConvertNormalizedToCycles(_nextEntry!.Deadline - _executionStateSlice.NormalizedSliceProgress);
        if (cycles >= _executionStateSlice.CyclesUntilReevaluation) {
            return;
        }

        _executionStateSlice.CyclesLeft += _executionStateSlice.CyclesUntilReevaluation;
        _executionStateSlice.CyclesUntilReevaluation = 0;
    }

    /// <summary>
    ///     Provides a readable name for a device event handler to enrich log statements.
    /// </summary>
    /// <param name="handler">The delegate whose name is requested.</param>
    /// <returns>The method name when available; otherwise a placeholder.</returns>
    private static string GetHandlerName(DeviceEventHandler? handler) {
        if (handler == null) {
            return "<null>";
        }

        string name = handler.Method.Name;
        return string.IsNullOrEmpty(name) ? "<anonymous>" : name;
    }

    /// <summary>
    ///     Represents a pooled event entry associated with the Device Scheduler.
    /// </summary>
    private sealed class ScheduledEntry {
        /// <summary>
        /// Callback stored for dispatch.
        /// </summary>
        public DeviceEventHandler? Handler;
        /// <summary>
        /// Fractional tick deadline.
        /// </summary>
        public double Deadline;
        /// <summary>
        /// Gets or sets the next scheduled entry in the sequence, or null if there are no further entries.
        /// </summary>
        public ScheduledEntry? Next;
        /// <summary>
        /// Return value passed to the handler.
        /// </summary>
        public uint Value;
    }
}