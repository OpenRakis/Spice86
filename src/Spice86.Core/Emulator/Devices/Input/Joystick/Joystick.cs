namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Joystick implementation. Emulates an unplugged joystick for now.
/// </summary>
public class Joystick : DefaultIOPortHandler {
    private const int JoystickPositionAndStatus = 0x201;

    private byte _joystickPositionAndStatusValue = 0xFF;

    /// <summary>
    /// Initializes a new instance of the <see cref="Joystick"/>
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="configuration">The emulator configuration.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public Joystick(IMemory memory, Cpu cpu, State state, Configuration configuration, ILoggerService loggerService) : base(memory, cpu, state, configuration, loggerService) {
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(JoystickPositionAndStatus, this);
    }

    /// <inheritdoc />
    public override byte ReadByte(int port) {
        return port switch {
            JoystickPositionAndStatus => _joystickPositionAndStatusValue,
            _ => base.ReadByte(port),
        };
    }

    /// <inheritdoc />
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