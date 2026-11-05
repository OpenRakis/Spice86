namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the game port registered by <see cref="Joystick"/>. Each method is a thin
/// call to <see cref="IOPortDispatcher"/> at port 0x201.
/// </summary>
public sealed class JoystickPorts {
    private const ushort GamePort = 0x201;

    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoystickPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public JoystickPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the joystick position and status register (0x201).</summary>
    [PortWrapper(GamePort, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPositionAndStatus() => _dispatcher.ReadByte(GamePort);

    /// <summary>Writes the game port, starting a position measurement (0x201).</summary>
    [PortWrapper(GamePort, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePositionAndStatus(byte value) => _dispatcher.WriteByte(GamePort, value);
}
