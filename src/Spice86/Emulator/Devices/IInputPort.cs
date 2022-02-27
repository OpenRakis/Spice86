namespace Spice86.Emulator.Devices;

using System.Collections.Generic;

/// <summary>
/// Defines an input device for a virtual machine.
/// </summary>
public interface IInputPort : IVirtualDevice
{
    /// <summary>
    /// Gets the input ports implemented by the device.
    /// </summary>
    IEnumerable<int> InputPorts { get; }

    /// <summary>
    /// Reads a single byte from one of the device's supported ports.
    /// </summary>
    /// <param name="port">Port from which byte is read.</param>
    /// <returns>Byte read from the specified port.</returns>
    byte ReadByte(int port);
    /// <summary>
    /// Reads two bytes from one of the device's supported ports.
    /// </summary>
    /// <param name="port">Port from which bytes are read.</param>
    /// <returns>Bytes read from the specified port.</returns>
    ushort ReadWord(int port);
}
