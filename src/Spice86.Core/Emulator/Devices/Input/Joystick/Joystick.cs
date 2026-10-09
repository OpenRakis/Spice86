namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

/// <summary>
/// Joystick implementation. Emulates an unplugged joystick, matching a real game port with
/// no stick attached: the axis monostable timers always read back as expired (0) and all
/// buttons always read back as released (1, active low).
/// </summary>
public class Joystick : DefaultIOPortHandler {
    private const int JoystickPositionAndStatus = 0x201;

    /// <summary>
    /// Status byte read back from the game port. Bits 0-3 are the axis timers (0 = expired,
    /// meaning no stick is connected) and bits 4-7 are the buttons (1 = released, active low).
    /// </summary>
    private const byte UnpluggedJoystickStatus = 0xF0;

    /// <summary>
    /// Initializes a new instance of the <see cref="Joystick"/>
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="ioPortDispatcher"></param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public Joystick(State state, IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILogger loggerService) : base(state, failOnUnhandledPort, loggerService) {
        InitPortHandlers(ioPortDispatcher);
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(JoystickPositionAndStatus, this);
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        return port switch {
            JoystickPositionAndStatus => UnpluggedJoystickStatus,
            _ => base.ReadByte(port),
        };
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case JoystickPositionAndStatus:
                // Writing to the game port triggers the axis one-shot timers. With no stick
                // attached there is nothing to measure, so the axes read back as expired on
                // the next read and the button bits are unaffected. The written value is not
                // stored: returning it would make games observe phantom button presses.
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }
}