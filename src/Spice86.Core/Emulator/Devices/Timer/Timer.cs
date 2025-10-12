namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Interfaces;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

/// <summary>
/// Emulates a PIT8254 Programmable Interval Timer.<br/>
/// Triggers interrupt 8 on the CPU via the PIC.<br/>
/// https://k.lse.epita.fr/internals/8254_controller.html
/// </summary>
public class Timer : DefaultIOPortHandler, ITimeMultiplier {
    private const int CounterRegisterZero = 0x40;
    private const int CounterRegisterOne = 0x41;
    private const int CounterRegisterTwo = 0x42;
    private const int ModeCommandeRegister = 0x43;

    /// <summary>
    /// The number of <see cref="Stopwatch"/> timer ticks per millisecond.
    /// </summary>
    public static readonly long StopwatchTicksPerMillisecond = Stopwatch.Frequency / 1000;

    private readonly Pit8254Counter[] _counters = new Pit8254Counter[3];
    private readonly DualPic _dualPic;

    /// <summary>
    /// Represents an entry that associates a periodic callback with its activator and name.
    /// </summary>
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    private readonly record struct PeriodicCallbackEntry {
        public required CounterActivator Activator { get; init; }
        public required Action Callback { get; init; }
        public required string Name { get; init; }

        private string GetDebuggerDisplay() {
            return $"{Name} (Frequency: {Activator.Frequency} Hz, Multiplier: {Activator.Multiplier}, IsActivated: {Activator.IsActivated})";
        }
    }

    private readonly List<PeriodicCallbackEntry> _deviceCallbacks = new();
    private readonly CounterConfiguratorFactory _counterConfiguratorFactory;
    private double _timeMultiplier = 1.0;

    /// <summary>
    /// Sub-ms event queue item
    /// </summary>
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    private record class ScheduledEvent {
        public required Guid Id { get; init; }
        public required string Name { get; init; }
        public required Action<uint> Handler { get; init; }
        public required uint Value { get; init; }
        public required long DueTicks { get; init; }
        public bool Canceled { get; set; }

        private string GetDebuggerDisplay() {
            return $"{Name} (Id: {Id}, Due in {(DueTicks - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency:F2} ms, Canceled: {Canceled})";
        }
    }

    private readonly object _eventLock = new();
    private readonly PriorityQueue<ScheduledEvent, long> _eventQueue = new();
    private bool _inEventService = false;
    private long _serviceNowTicks = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="Timer"/> class.
    /// </summary>
    public Timer(Configuration configuration, State state, IOPortDispatcher ioPortDispatcher,
        CounterConfiguratorFactory counterConfiguratorFactory, ILoggerService loggerService, DualPic dualPic)
        : base(state, configuration.FailOnUnhandledPort, loggerService) {
        _dualPic = dualPic;
        _counterConfiguratorFactory = counterConfiguratorFactory;

        for (int i = 0; i < _counters.Length; i++) {
            _counters[i] = new Pit8254Counter(_loggerService, i, counterConfiguratorFactory.InstantiateCounterActivator());
        }
        InitPortHandlers(ioPortDispatcher);
    }

    /// <inheritdoc cref="ITimeMultiplier.SetTimeMultiplier(double)" />
    public void SetTimeMultiplier(double multiplier) {
        if (multiplier <= 0) {
            throw new DivideByZeroException(nameof(multiplier));
        }
        _timeMultiplier = multiplier;

        foreach (Pit8254Counter counter in _counters) {
            counter.Activator.Multiplier = multiplier;
        }
        foreach (PeriodicCallbackEntry entry in _deviceCallbacks) {
            entry.Activator.Multiplier = multiplier;
        }
    }

    /// <summary>
    /// Registers a periodic callback driven by a CounterActivator at the given frequency (Hz).
    /// </summary>
    public void RegisterPeriodicCallback(string name, double frequencyHz, Action callback) {
        CounterActivator activator = _counterConfiguratorFactory.InstantiateCounterActivator();
        activator.Multiplier = _timeMultiplier;
        activator.Frequency = (long)Math.Max(1, Math.Round(frequencyHz));
        _deviceCallbacks.Add(new PeriodicCallbackEntry {
            Activator = activator,
            Callback = callback,
            Name = name
        });
    }

    /// <summary>
    /// Unregisters a previously registered periodic callback.
    /// </summary>
    public void UnregisterPeriodicCallback(Action callback) {
        _deviceCallbacks.RemoveAll(x => x.Callback == callback);
    }

    /// <summary>
    /// Drives registered device callbacks; call this once per emulation iteration after Tick().
    /// </summary>
    public void DeviceTick() {
        // Pausing is handled by CounterActivator.IsFrozen via IPauseHandler.
        foreach (PeriodicCallbackEntry entry in _deviceCallbacks) {
            if (entry.Activator.IsActivated) {
                entry.Callback();
            }
        }
    }

    /// <summary>
    /// Schedule a single-shot event to run after delayMs (sub-ms supported).
    /// Time multiplier > 1 speeds up events, < 1 slows down.
    /// </summary>
    public Guid ScheduleEvent(string name, double delayMs, Action action) {
        // Wrap Action as Action<uint> with dummy value
        return ScheduleEvent(name, delayMs, _ => action(), 0);
    }

    /// <summary>
    /// Schedule a single-shot event (value-carrying) to run after delayMs (sub-ms supported).
    /// Time multiplier > 1 speeds up events, < 1 slows down.
    /// </summary>
    public Guid ScheduleEvent(string name, double delayMs, Action<uint> handler, uint value) {
        var id = Guid.NewGuid();
        long baseTicks = _inEventService ? _serviceNowTicks : Stopwatch.GetTimestamp();
        long dueTicks = baseTicks + MsToTicks(delayMs / _timeMultiplier);

        var ev = new ScheduledEvent {
            Id = id,
            Name = name,
            Handler = handler,
            Value = value,
            DueTicks = dueTicks,
            Canceled = false
        };

        lock (_eventLock) {
            _eventQueue.Enqueue(ev, ev.DueTicks);
        }
        return id;
    }

    /// <summary>
    /// Remove all events for a particular handler.
    /// </summary>
    public int RemoveEvents(Action<uint> handler) {
        int count = 0;
        lock (_eventLock) {
            foreach ((ScheduledEvent Element, long Priority) item in _eventQueue.UnorderedItems) {
                if (!item.Element.Canceled && item.Element.Handler == handler) {
                    item.Element.Canceled = true;
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Remove specific events that match a handler and value.
    /// </summary>
    public int RemoveSpecificEvents(Action<uint> handler, uint value) {
        int count = 0;
        lock (_eventLock) {
            foreach ((ScheduledEvent Element, long Priority) in _eventQueue.UnorderedItems) {
                if (!Element.Canceled && Element.Handler == handler && Element.Value == value) {
                    Element.Canceled = true;
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Run the sub-ms event queue. Should be called frequently from the emulation loop.
    /// </summary>
    public void RunEventQueue() {
        long now = Stopwatch.GetTimestamp();
        _inEventService = true;
        _serviceNowTicks = now;

        while (_eventQueue.TryPeek(out ScheduledEvent? ev, out long due)) {
            if (due > now) {
                break;
            }
            lock (_eventLock) {
                _eventQueue.Dequeue();
                if (ev?.Canceled is true) {
                    continue;
                }
            }
            // Execute outside lock
            try {
                ev?.Handler(ev.Value);
            } catch (Exception ex) {
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _loggerService.Warning("Timer event '{Name}' failed: {Error}", ev?.Name, ex.Message);
                }
            }
        }

        _inEventService = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long MsToTicks(double ms) {
        // ms can be fractional; ticks are Stopwatch ticks
        return (long)Math.Round(ms * Stopwatch.Frequency / 1000.0);
    }

    /// <summary>
    /// Gets the counter at the specified index.
    /// </summary>
    /// <param name="counterIndex">The index of the counter to retrieve</param>
    /// <returns>A reference to the counter</returns>
    /// <exception cref="InvalidCounterIndexException">The index was out of range.</exception>
    public Pit8254Counter GetCounter(int counterIndex) {
        if (counterIndex > _counters.Length || counterIndex < 0) {
            throw new InvalidCounterIndexException(_state, counterIndex);
        }
        return _counters[counterIndex];
    }

    /// <summary>
    /// Gets the number of ticks in the first counter.
    /// </summary>
    public long NumberOfTicks => _counters[0].CurrentCount;

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (IsCounterRegisterPort(port)) {
            Pit8254Counter pit8254Counter = GetCounterIndexFromPortNumber(port);
            byte value = pit8254Counter.ReadCurrentCountByte();
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("READING COUNTER {Counter}, partial value is {Value}", pit8254Counter, value);
            }
            return value;
        }
        return base.ReadByte(port);
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(ModeCommandeRegister, this);
        ioPortDispatcher.AddIOPortHandler(CounterRegisterZero, this);
        ioPortDispatcher.AddIOPortHandler(CounterRegisterOne, this);
        ioPortDispatcher.AddIOPortHandler(CounterRegisterTwo, this);
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        if (IsCounterRegisterPort(port)) {
            Pit8254Counter pit8254Counter = GetCounterIndexFromPortNumber(port);
            pit8254Counter.WriteReloadValueByte(value);
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("SETTING COUNTER {Index} to partial value {Value}. {Counter}", pit8254Counter.Index, value, pit8254Counter);
            }
            return;
        }
        if (port == ModeCommandeRegister) {
            int counterIndex = value >> 6;
            Pit8254Counter pit8254Counter = GetCounter(counterIndex);
            pit8254Counter.Configure(value);
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("SETTING CONTROL REGISTER FOR COUNTER {CounterIndex}. {Counter}", counterIndex, pit8254Counter);
            }
            return;
        }
        base.WriteByte(port, value);
    }

    /// <summary>
    /// If the counter is activated, triggers the interrupt request
    /// </summary>
    public void Tick() {
        // Only do counter 0.
        // Counter 1 is unused and counter 2 is PC speaker which is handled separately.
        if (_counters[0].ProcessActivation()) {
            _dualPic.ProcessInterruptRequest(0);
        }
    }

    private static bool IsCounterRegisterPort(int port) => port is >= CounterRegisterZero and <= CounterRegisterTwo;

    private Pit8254Counter GetCounterIndexFromPortNumber(int port) {
        int counter = port & 0b11;
        return GetCounter(counter);
    }
}