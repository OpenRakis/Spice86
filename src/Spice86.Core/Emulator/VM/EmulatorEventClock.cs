namespace Spice86.Core.Emulator.VM;

using Serilog.Events;

using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Manages timed events for the VM similar to DOSBox's PIC event system.
/// </summary>
public class EmulatorEventClock {
    private readonly Stopwatch _stopwatch = new();
    private readonly ILoggerService _loggerService;

    // Constants for the event queue
    private const int QueueSize = 512; // Like DOSBox PIC_QUEUESIZE

    // Event entry for our linked list implementation
    private class EventEntry {
        public double Index { get; set; }
        public uint Value { get; set; }
        public Action<uint> Callback { get; set; } = null!;
        public EventEntry? Next { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private readonly EventEntry[] _entries = new EventEntry[QueueSize];
    private EventEntry? _freeEntry; // Head of free entries list
    private EventEntry? _nextEntry; // Head of active entries list
    private bool _isProcessingTick = false;
    private double _serviceEventIndex = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulatorEventClock"/> class.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    public EmulatorEventClock(ILoggerService loggerService) {
        _loggerService = loggerService;
        _stopwatch.Start();

        // Initialize event queue similar to DOSBox
        for (int i = 0; i < QueueSize - 1; i++) {
            _entries[i] = new EventEntry();
            _entries[i].Next = _entries[i + 1];
        }
        _entries[QueueSize - 1] = new EventEntry();
        _entries[QueueSize - 1].Next = null;
        _freeEntry = _entries[0];
        _nextEntry = null;
    }

    /// <summary>
    /// Gets the time in milliseconds since the emulation started.
    /// </summary>
    public long EmulatorUpTimeInMs => _stopwatch.ElapsedMilliseconds;

    /// <summary>
    /// Gets or sets the value to use for indexing events.
    /// </summary>
    public double TickIndex => _stopwatch.Elapsed.TotalMilliseconds;

    /// <summary>
    /// Adds a scheduled event to be executed after a specified delay.
    /// </summary>
    /// <param name="callback">The action to execute when the event is due.</param>
    /// <param name="delayMs">The delay in milliseconds before executing the event.</param>
    /// <param name="name">Optional name for the event.</param>
    public void AddEvent(Action callback, double delayMs, string name = "") {
        if (_freeEntry == null) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Event queue full");
            }
            return;
        }

        // Get an entry from the free list
        EventEntry entry = _freeEntry;
        _freeEntry = entry.Next;

        // Set the entry properties
        entry.Index = _isProcessingTick ? delayMs + _serviceEventIndex : delayMs + TickIndex;
        entry.Callback = _ => callback(); // Adapt Action to Action<uint>
        entry.Value = 0; // Not used with parameterless callback
        entry.Name = name;

        // Add the entry to the active entries list
        AddEntry(entry);

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Added event: {Name} at index {Index}", name, entry.Index);
        }
    }

    private void AddEntry(EventEntry entry) {
        EventEntry? findEntry = _nextEntry;
        if (findEntry == null) {
            // No entries in the list yet
            entry.Next = null;
            _nextEntry = entry;
        } else if (findEntry.Index > entry.Index) {
            // New entry goes at the beginning
            _nextEntry = entry;
            entry.Next = findEntry;
        } else {
            // Find the right position in the list
            while (findEntry != null) {
                if (findEntry.Next != null) {
                    if (findEntry.Next.Index > entry.Index) {
                        // Insert after current entry
                        entry.Next = findEntry.Next;
                        findEntry.Next = entry;
                        break;
                    } else {
                        // Move to next entry
                        findEntry = findEntry.Next;
                    }
                } else {
                    // Add at the end
                    entry.Next = null;
                    findEntry.Next = entry;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Removes an event by its name.
    /// </summary>
    /// <param name="name">The name of the event to remove.</param>
    public void RemoveEvent(string name) {
        if (string.IsNullOrEmpty(name)) {
            return;
        }

        // Find and remove by name
        EventEntry? entry = _nextEntry;
        EventEntry? prevEntry = null;

        while (entry != null) {
            if (entry.Name == name) {
                if (prevEntry != null) {
                    prevEntry.Next = entry.Next;
                    entry.Next = _freeEntry;
                    _freeEntry = entry;

                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("Removed event: {Name}", name);
                    }
                    return;
                } else {
                    _nextEntry = entry.Next;
                    entry.Next = _freeEntry;
                    _freeEntry = entry;

                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("Removed event: {Name}", name);
                    }
                    return;
                }
            }

            prevEntry = entry;
            entry = entry.Next;
        }
    }

    /// <summary>
    /// Processes all due events. Should be called regularly by the emulation loop.
    /// </summary>
    public void Tick() {
        double currentTime = TickIndex;

        // Check if there are any events to process
        if (_nextEntry == null) {
            return;
        }

        _isProcessingTick = true;
        // Process all events that are due
        while (_nextEntry != null && _nextEntry.Index <= currentTime) {
            EventEntry entry = _nextEntry;
            _nextEntry = entry.Next;

            _serviceEventIndex = entry.Index;
            entry.Callback(entry.Value);

            // Return entry to free list
            entry.Next = _freeEntry;
            _freeEntry = entry;
        }
        _isProcessingTick = false;
    }
}