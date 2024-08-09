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

    private readonly Counter[] _counters = new Counter[3];
    private readonly DualPic _dualPic;

    /// <summary>
    /// Initializes a new instance of the <see cref="Timer"/> class.
    /// </summary>
    public Timer(Configuration configuration, State state, ILoggerService loggerService, DualPic dualPic) : base(state, configuration.FailOnUnhandledPort, loggerService) {
        _dualPic = dualPic;
        for(int i = 0; i < _counters.Length; i++) {
            _counters[i] = new Counter(state, loggerService, CreateCounterActivator(state, loggerService, configuration)) {
                Index = i
            };
        }
    }
    
    /// <summary>
    /// Returns the appropriate <see cref="CounterActivator"/> based on the configuration.
    /// </summary>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="loggerService">The service used for logging.</param>
    /// <param name="configuration">The emulator's configuration.</param>
    /// <returns>The appropriate <see cref="CyclesCounterActivator"/> or <see cref="TimeCounterActivator"/></returns>
    private static CounterActivator CreateCounterActivator(State state, ILoggerService loggerService, Configuration configuration) {
        const long DefaultInstructionsPerSecond = 1000000L;
        long? instructionsPerSecond = configuration.InstructionsPerSecond;
        if (instructionsPerSecond == null && configuration.GdbPort != null) {
            // With GDB, force to instructions per seconds as time based timers could perturb steps
            instructionsPerSecond = DefaultInstructionsPerSecond;
            if (loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                loggerService.Warning("Forcing Counter to use instructions per seconds since we are in GDB mode. If speed is too slow or too fast adjust the --InstructionsPerSecond parameter");
            }
        }
        if (instructionsPerSecond != null) {
            return new CyclesCounterActivator(state, instructionsPerSecond.Value, configuration.TimeMultiplier);
        }
        return new TimeCounterActivator(configuration.TimeMultiplier);
    }

    /// <inheritdoc cref="ITimeMultiplier" />
    public void SetTimeMultiplier(double multiplier) {
        if (multiplier <= 0) {
            throw new DivideByZeroException(nameof(multiplier));
        }
        foreach (Counter counter in _counters) {
            counter.Activator.Multiplier = multiplier;
        }
    }

    public Counter GetCounter(int counterIndex) {
        if (counterIndex > _counters.Length || counterIndex < 0) {
            throw new InvalidCounterIndexException(_state, counterIndex);
        }
        return _counters[counterIndex];
    }

    public long NumberOfTicks => _counters[0].Ticks;

    /// <inheritdoc />
    public override byte ReadByte(int port) {
        if (IsCounterRegisterPort(port)) {
            Counter counter = GetCounterIndexFromPortNumber(port);
            byte value = counter.ValueUsingMode;
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("READING COUNTER {Counter}, partial value is {Value}", counter, value);
            }
            return value;
        }
        return base.ReadByte(port);
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(ModeCommandeRegister, this);
        ioPortDispatcher.AddIOPortHandler(CounterRegisterZero, this);
        ioPortDispatcher.AddIOPortHandler(CounterRegisterOne, this);
        ioPortDispatcher.AddIOPortHandler(CounterRegisterTwo, this);
    }

    /// <inheritdoc />
    public override void WriteByte(int port, byte value) {
        if (IsCounterRegisterPort(port)) {
            Counter counter = GetCounterIndexFromPortNumber(port);
            counter.SetValueUsingMode(value);
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("SETTING COUNTER {Index} to partial value {Value}. {Counter}", counter.Index, value, counter);
            }
            return;
        }
        if (port == ModeCommandeRegister) {
            int counterIndex = value >> 6;
            Counter counter = GetCounter(counterIndex);
            counter.Configure(value);
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("SETTING CONTROL REGISTER FOR COUNTER {CounterIndex}. {Counter}", counterIndex, counter);
            }
            return;
        }
        base.WriteByte(port, value);
    }


    public void Tick() {
        if (_counters[0].ProcessActivation()) {
            _dualPic.ProcessInterruptRequest(0);
        }
    }

    private static bool IsCounterRegisterPort(int port) => port is >= CounterRegisterZero and <= CounterRegisterTwo;

    private Counter GetCounterIndexFromPortNumber(int port) {
        int counter = port & 0b11;
        return GetCounter(counter);
    }
}
