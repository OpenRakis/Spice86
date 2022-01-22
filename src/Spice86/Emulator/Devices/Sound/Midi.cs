namespace Spice86.Emulator.Devices.Sound;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;

/// <summary>
/// MPU401 (Midi) implementation. Emulates an absent card :)
/// </summary>
public class Midi : DefaultIOPortHandler {
    private static readonly int COMMAND = 0x331;
    private static readonly int DATA = 0x330;

    public Midi(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {
    }

    public override byte Inb(int port) {
        if (port == DATA) {
            return ReadData();
        } else {
            return ReadStatus();
        }
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(DATA, this);
        ioPortDispatcher.AddIOPortHandler(COMMAND, this);
    }

    public override void Outb(int port, byte value) {
        if (port == DATA) {
            WriteData(value);
        } else {
            WriteCommand(value);
        }
    }

    public byte ReadData() {
        return 0;
    }

    public byte ReadStatus() {
        return 0;
    }

    public void WriteCommand(byte value) {
    }

    public void WriteData(byte value) {
    }
}