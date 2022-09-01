namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Joystick implementation. Emulates an unplugged joystick for now.
/// </summary>
public class Joystick : DefaultIOPortHandler {
    private const int JoystickPositionAndStatus = 0x201;

    public Joystick(Machine machine, Configuration configuration) : base(machine, configuration) {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(JoystickPositionAndStatus, this);
    }
}