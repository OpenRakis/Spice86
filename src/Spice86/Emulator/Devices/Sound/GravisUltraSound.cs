namespace Spice86.Emulator.Devices.Sound;
using Spice86.Emulator.IOPorts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Spice86.Emulator.Machine;

/// <summary>
/// Gravis Ultra Sound implementation. Emulates an absent card :)
/// </summary>
public class GravisUltraSound : DefaultIOPortHandler
{
    private static readonly int MIX_CONTROL_REGISTER = 0x240;
    private static readonly int READ_DATA_OR_TRIGGER_STATUS = 0x241;
    private static readonly int IRQ_STATUS_REGISTER = 0x246;
    private static readonly int TIMER_CONTROL_REGISTER = 0x248;
    private static readonly int IRQ_CONTROL_REGISTER = 0x24B;
    private static readonly int REGISTER_CONTROLS = 0x24F;
    public GravisUltraSound(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort)
    {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher)
    {
        ioPortDispatcher.AddIOPortHandler(MIX_CONTROL_REGISTER, this);
        ioPortDispatcher.AddIOPortHandler(READ_DATA_OR_TRIGGER_STATUS, this);

        // Not sure what those are but some programs search the card in those ports as well
        ioPortDispatcher.AddIOPortHandler(0x243, this);
        ioPortDispatcher.AddIOPortHandler(0x280, this);
        ioPortDispatcher.AddIOPortHandler(0x281, this);
        ioPortDispatcher.AddIOPortHandler(0x283, this);
        ioPortDispatcher.AddIOPortHandler(0x2C0, this);
        ioPortDispatcher.AddIOPortHandler(0x2C1, this);
        ioPortDispatcher.AddIOPortHandler(0x2C3, this);
        ioPortDispatcher.AddIOPortHandler(IRQ_STATUS_REGISTER, this);
        ioPortDispatcher.AddIOPortHandler(TIMER_CONTROL_REGISTER, this);
        ioPortDispatcher.AddIOPortHandler(IRQ_CONTROL_REGISTER, this);
        ioPortDispatcher.AddIOPortHandler(REGISTER_CONTROLS, this);
    }
}
