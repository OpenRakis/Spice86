namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Joystick implementation. Emulates an unplugged joystick for now.
/// </summary>
public class Joystick : DefaultIOPortHandler {
    private const int JoystickPositionAndStatus = 0x201;

    private byte _joystickPositionAndStatusValue = 0xFF;

    public Joystick(Machine machine, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService) {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(JoystickPositionAndStatus, this);
    }

    public override byte ReadByte(int port) {
        return port switch {
            JoystickPositionAndStatus => _joystickPositionAndStatusValue,
            _ => base.ReadByte(port),
        };
    }
    
    public override void WriteByte(int port, byte value) {
        switch (port) {
            case JoystickPositionAndStatus:
                _joystickPositionAndStatusValue = value;
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }
}