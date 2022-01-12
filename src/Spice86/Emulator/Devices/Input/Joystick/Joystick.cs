namespace Spice86.Emulator.Devices.Input.Joystick;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Machine;

/// <summary>
/// Joystick implementation. Emulates an unplugged joystick for now.
/// </summary>
public class Joystick : DefaultIOPortHandler
{
    private static readonly int JOYSTIC_POSITON_AND_STATUS = 0x201;

    public Joystick(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort)
    {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher)
    {
        ioPortDispatcher.AddIOPortHandler(JOYSTIC_POSITON_AND_STATUS, this);
    }
}