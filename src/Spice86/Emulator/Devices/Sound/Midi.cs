namespace Spice86.Emulator.Devices.Sound;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Machine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


/// <summary>
/// MPU401 (Midi) implementation. Emulates an absent card :)
/// </summary>
public class Midi : DefaultIOPortHandler
{
    private static readonly int DATA = 0x330;
    private static readonly int COMMAND = 0x331;
    public Midi(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort)
    {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher)
    {
        ioPortDispatcher.AddIOPortHandler(DATA, this);
        ioPortDispatcher.AddIOPortHandler(COMMAND, this);
    }

    public override int Inb(int port)
    {
        if (port == DATA)
        {
            return ReadData();
        }
        else
        {
            return ReadStatus();
        }
    }

    public override void Outb(int port, int value)
    {
        if (port == DATA)
        {
            WriteData(value);
        }
        else
        {
            WriteCommand(value);
        }
    }

    public virtual void WriteData(int value)
    {
    }

    public virtual void WriteCommand(int value)
    {
    }

    public virtual int ReadData()
    {
        return 0;
    }

    public virtual int ReadStatus()
    {
        return 0;
    }
}
