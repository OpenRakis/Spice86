namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.CPU;

using System.Diagnostics;

using Spice86.Shared.Interfaces;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Emulates a PIT8254 Programmable Interval Timer.<br/>
/// As a shortcut also triggers screen refreshes 60 times per second.<br/>
/// Triggers interrupt 8 on the CPU via the PIC.<br/>
/// https://k.lse.epita.fr/internals/8254_controller.html
/// </summary>
public class Timer : DefaultIOPortHandler {
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

    private readonly IVideoCard? _vgaCard;

    // screen refresh
    private readonly Counter _vgaScreenRefreshCounter;

    public Timer(IMemory memory, Cpu cpu, State state, ILoggerService loggerService, DualPic dualPic, IVideoCard? vgaCard, CounterConfigurator counterConfigurator, Configuration configuration) : base(memory, cpu, state, configuration, loggerService) {
        _dualPic = dualPic;
        _vgaCard = vgaCard;
        for (int i = 0; i < _counters.Length; i++) {
            _counters[i] = new Counter(state,
                _loggerService,
                i, counterConfigurator.InstanciateCounterActivator(_cpu.State));
        }
        // screen refresh is 60hz regardless of the configuration
        _vgaScreenRefreshCounter = new Counter(state, _loggerService, 4, new TimeCounterActivator(1));
        _vgaScreenRefreshCounter.SetValue((int)(Counter.HardwareFrequency / 60));
    }

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
        long cycles = _cpu.State.Cycles;
        if (_counters[0].ProcessActivation(cycles)) {
            _dualPic.ProcessInterruptRequest(0);
        }

        if (_vgaScreenRefreshCounter.ProcessActivation(cycles)) {
            _vgaCard?.UpdateScreen();
        }
    }

    private static bool IsCounterRegisterPort(int port) => port is >= CounterRegisterZero and <= CounterRegisterTwo;

    private Counter GetCounterIndexFromPortNumber(int port) {
        int counter = port & 0b11;
        return GetCounter(counter);
    }
}
