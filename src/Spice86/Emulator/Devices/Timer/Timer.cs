namespace Spice86.Emulator.Devices.Timer;

using Serilog;

using Spice86.Emulator.Devices.ExternalInput;
using Spice86.Emulator.Devices.Video;
using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;

using System.Diagnostics;

/// <summary>
/// Emulates a PIT8254 Programmable Interval Timer.<br/>
/// As a shortcut also triggers screen refreshes 60 times per second.<br/>
/// Triggers interrupt 8 on the CPU via the PIC.<br/>
/// https://k.lse.epita.fr/internals/8254_controller.html
/// </summary>
public class Timer : DefaultIOPortHandler {
    private static readonly ILogger _logger = Program.Logger.ForContext<Timer>();
    private const int CounterRegisterZero = 0x40;
    private const int CounterRegisterOne = 0x41;
    private const int CounterRegisterTwo = 0x42;
    private const int ModeCommandeRegister = 0x43;

    /// <summary>
    /// The number of <see cref="System.Diagnostics.Stopwatch"/> timer ticks per millisecond.
    /// </summary>
    public static readonly long StopwatchTicksPerMillisecond = Stopwatch.Frequency / 1000;

    private readonly Counter[] _counters = new Counter[3];
    private readonly Pic _pic;

    private readonly VgaCard _vgaCard;

    // Cheat: display at 60fps
    private readonly Counter _vgaCounter;

    public Timer(Machine machine, Pic pic, VgaCard vgaCard, CounterConfigurator counterConfigurator, Configuration configuration) : base(machine, configuration) {
        this._pic = pic;
        this._vgaCard = vgaCard;
        this._cpu = machine.Cpu;
        for (int i = 0; i < _counters.Length; i++) {
            _counters[i] = new Counter(machine, i, counterConfigurator.InstanciateCounterActivator(_cpu.State));
        }

        _vgaCounter = new Counter(machine, 4, new TimeCounterActivator(1));

        // 30fps
        _vgaCounter.SetValue((int)(Counter.HardwareFrequency / 30));
    }

    public Counter GetCounter(int counterIndex) {
        if (counterIndex > _counters.Length || counterIndex < 0) {
            throw new InvalidCounterIndexException(_machine, counterIndex);
        }
        return _counters[counterIndex];
    }

    public long NumberOfTicks => _counters[0].Ticks;

    public override byte ReadByte(int port) {
        if (IsCounterRegisterPort(port)) {
            Counter counter = GetCounterIndexFromPortNumber(port);
            byte value = counter.ValueUsingMode;
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("READING COUNTER {@Counter}, partial value is {@Value}", counter, value);
            }
            return value;
        }

        return base.ReadByte(port);
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(ModeCommandeRegister, this);
        ioPortDispatcher.AddIOPortHandler(CounterRegisterZero, this);
        ioPortDispatcher.AddIOPortHandler(CounterRegisterOne, this);
        ioPortDispatcher.AddIOPortHandler(CounterRegisterTwo, this);
    }

    public override void WriteByte(int port, byte value) {
        if (IsCounterRegisterPort(port)) {
            Counter counter = GetCounterIndexFromPortNumber(port);
            counter.SetValueUsingMode(value);
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("SETTING COUNTER {@Index} to partial value {@Value}. {@Counter}", counter.Index, value, counter);
            }
            return;
        } else if (port == ModeCommandeRegister) {
            int counterIndex = (value >> 6);
            Counter counter = GetCounter(counterIndex);
            counter.ReadWritePolicy = (value >> 4) & 0b11;
            counter.Mode = (value >> 1) & 0b111;
            counter.Bcd = value & 1;
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("SETTING CONTROL REGISTER FOR COUNTER {@CounterIndex}. {@Counter}", counterIndex, counter);
            }
            return;
        }
        base.WriteByte(port, value);
    }

    public void Tick() {
        long cycles = _cpu.State.Cycles;
        if (_counters[0].ProcessActivation(cycles)) {
            _pic.ProcessInterrupt(0x8);
        }

        if (_vgaCounter.ProcessActivation(cycles)) {
            _vgaCard.UpdateScreen();
        }
    }

    private static bool IsCounterRegisterPort(int port) => port is >= CounterRegisterZero and <= CounterRegisterTwo;

    private Counter GetCounterIndexFromPortNumber(int port) {
        int counter = port & 0b11;
        return GetCounter(counter);
    }
}