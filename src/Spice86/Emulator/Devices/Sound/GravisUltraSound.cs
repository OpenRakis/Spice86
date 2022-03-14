namespace Spice86.Emulator.Devices.Sound;

using Spice86.Emulator.IOPorts;

using Spice86.Emulator.VM;

/// <summary>
/// Gravis Ultra Sound implementation. Emulates an absent card :)
/// </summary>
public class GravisUltraSound : DefaultIOPortHandler {
    private const int IrqControlRegister = 0x24B;
    private const int IrqStatusRegister = 0x246;
    private const int MixControlRegister = 0x240;
    private const int ReadDataOrTriggerStatus = 0x241;
    private const int RegisterControls = 0x24F;
    private const int TimerControlRegister = 0x248;

    public GravisUltraSound(Machine machine, Configuration configuration) : base(machine, configuration) {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(MixControlRegister, this);
        ioPortDispatcher.AddIOPortHandler(ReadDataOrTriggerStatus, this);

        // Not sure what those are but some programs search the card in those ports as well
        ioPortDispatcher.AddIOPortHandler(0x243, this);
        ioPortDispatcher.AddIOPortHandler(0x280, this);
        ioPortDispatcher.AddIOPortHandler(0x281, this);
        ioPortDispatcher.AddIOPortHandler(0x283, this);
        ioPortDispatcher.AddIOPortHandler(0x2C0, this);
        ioPortDispatcher.AddIOPortHandler(0x2C1, this);
        ioPortDispatcher.AddIOPortHandler(0x2C3, this);
        ioPortDispatcher.AddIOPortHandler(IrqStatusRegister, this);
        ioPortDispatcher.AddIOPortHandler(TimerControlRegister, this);
        ioPortDispatcher.AddIOPortHandler(IrqControlRegister, this);
        ioPortDispatcher.AddIOPortHandler(RegisterControls, this);
    }
}