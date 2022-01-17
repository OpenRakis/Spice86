namespace Spice86.Emulator.Devices.Timer;

using Serilog;

using Spice86.Emulator.Devices.ExternalInput;
using Spice86.Emulator.Devices.Video;
using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;

/// <summary>
/// Emulates a PIT8254 Programmable Interval Timer.<br/>
/// As a shortcut also triggers screen refreshes 60 times per second.<br/>
/// Triggers interrupt 8 on the CPU via the PIC.<br/>
/// https://k.lse.epita.fr/internals/8254_controller.html
/// </summary>
public class Timer : DefaultIOPortHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<Timer>();
    private static readonly int COUNTER_REGISTER_0 = 0x40;
    private static readonly int COUNTER_REGISTER_1 = 0x41;
    private static readonly int COUNTER_REGISTER_2 = 0x42;
    private static readonly int MODE_COMMAND_REGISTER = 0x43;
    private readonly Counter[] counters = new Counter[3];
    private readonly Pic pic;

    private readonly VgaCard vgaCard;

    // Cheat: display at 60fps
    private readonly Counter vgaCounter;

    public Timer(Machine machine, Pic pic, VgaCard vgaCard, CounterConfigurator counterConfigurator, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {
        this.pic = pic;
        this.vgaCard = vgaCard;
        this.cpu = machine.GetCpu();
        for (int i = 0; i < counters.Length; i++) {
            counters[i] = new Counter(machine, i, counterConfigurator.InstanciateCounterActivator(cpu.GetState()));
        }

        vgaCounter = new Counter(machine, 4, new TimeCounterActivator(1));

        // 30fps
        vgaCounter.SetValue((int)(Counter.HardwareFrequency / 30));
    }

    public Counter GetCounter(int counterIndex) {
        if (counterIndex > counters.Length || counterIndex < 0) {
            throw new InvalidCounterIndexException(machine, counterIndex);
        }
        return counters[counterIndex];
    }

    public long GetNumberOfTicks() {
        return counters[0].GetTicks();
    }

    public override int Inb(int port) {
        if (IsCounterRegisterPort(port)) {
            Counter counter = GetCounterIndexFromPortNumber(port);
            int value = counter.GetValueUsingMode();
            _logger.Information("READING COUNTER {@Counter}, partial value is {@Value}", counter, value);
            return value;
        }

        return base.Inb(port);
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(MODE_COMMAND_REGISTER, this);
        ioPortDispatcher.AddIOPortHandler(COUNTER_REGISTER_0, this);
        ioPortDispatcher.AddIOPortHandler(COUNTER_REGISTER_1, this);
        ioPortDispatcher.AddIOPortHandler(COUNTER_REGISTER_2, this);
    }

    public override void Outb(int port, int value) {
        if (IsCounterRegisterPort(port)) {
            Counter counter = GetCounterIndexFromPortNumber(port);
            counter.SetValueUsingMode(value);
            _logger.Information("SETTING COUNTER {@Index} to partial value {@Value}. {@Counter}", counter.GetIndex(), value, counter);
            return;
        } else if (port == MODE_COMMAND_REGISTER) {
            int counterIndex = (value >> 6);
            Counter counter = GetCounter(counterIndex);
            counter.SetReadWritePolicy((value >> 4) & 0b11);
            counter.SetMode((value >> 1) & 0b111);
            counter.SetBcd(value & 1);
            _logger.Information("SETTING CONTROL REGISTER FOR COUNTER {@CounterIndex}. {@Counter}", counterIndex, counter);
            return;
        }
        base.Outb(port, value);
    }

    public void Tick() {
        long cycles = cpu.GetState().GetCycles();
        if (counters[0].ProcessActivation(cycles)) {
            pic.ProcessInterrupt(0x8);
        }

        if (vgaCounter.ProcessActivation(cycles)) {
            vgaCard.UpdateScreen();
        }
    }

    private static bool IsCounterRegisterPort(int port) => port >= COUNTER_REGISTER_0 && port <= COUNTER_REGISTER_2;

    private Counter GetCounterIndexFromPortNumber(int port) {
        int counter = port & 0b11;
        return GetCounter(counter);
    }
}