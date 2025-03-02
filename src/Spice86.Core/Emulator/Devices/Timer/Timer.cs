namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.CPU;

using System.Diagnostics;

using Spice86.Shared.Interfaces;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;

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
    /// Initializes a new instance of the <see cref="Timer"/> class.
    /// </summary>
    public Timer(Configuration configuration, State state, IOPortDispatcher ioPortDispatcher,
        CounterConfiguratorFactory counterConfiguratorFactory, ILoggerService loggerService, DualPic dualPic) : base(state, configuration.FailOnUnhandledPort, loggerService) {
        _dualPic = dualPic;
        
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
        foreach (Pit8254Counter counter in _counters) {
            counter.Activator.Multiplier = multiplier;
        }
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
