namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the keyboard controller ports registered by <see cref="Intel8042Controller"/>.
/// Each method is a thin call to <see cref="IOPortDispatcher"/> at the port of
/// <see cref="KeyboardPorts"/> it wraps.
/// </summary>
public sealed class KeyboardControllerPorts {
    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardControllerPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public KeyboardControllerPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the controller output buffer (0x60).</summary>
    [PortWrapper(KeyboardPorts.Data, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadData() => _dispatcher.ReadByte(KeyboardPorts.Data);

    /// <summary>Writes the controller input buffer or a command parameter (0x60).</summary>
    [PortWrapper(KeyboardPorts.Data, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteData(byte value) => _dispatcher.WriteByte(KeyboardPorts.Data, value);

    /// <summary>Reads the controller status register (0x64).</summary>
    [PortWrapper(KeyboardPorts.StatusRegister, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadStatus() => _dispatcher.ReadByte(KeyboardPorts.StatusRegister);

    /// <summary>Writes a controller command (0x64).</summary>
    [PortWrapper(KeyboardPorts.Command, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCommand(byte value) => _dispatcher.WriteByte(KeyboardPorts.Command, value);
}
