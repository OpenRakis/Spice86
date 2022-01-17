namespace Spice86.Emulator.Devices.Sound;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;
using Spice86.Utils;

/// <summary>
/// PC speaker implementation. Does not produce any sound, just handles the bare minimum to make programs run.
/// </summary>
public class PcSpeaker : DefaultIOPortHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<PcSpeaker>();
    private static readonly int PC_SPEAKER_PORT_NUMBER = 0x61;
    private int value;

    public PcSpeaker(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {
    }

    public override int Inb(int port) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PC Speaker get value {@PCSpeakerValue}", ConvertUtils.ToHex8(this.value));
        }

        return this.value;
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(PC_SPEAKER_PORT_NUMBER, this);
    }

    public override void Outb(int port, int value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PC Speaker set value {@PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }

        this.value = value;
    }
}