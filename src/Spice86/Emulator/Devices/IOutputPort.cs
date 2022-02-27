namespace Spice86.Emulator.Devices;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

/// <summary>
/// Defines an output device for a virtual machine.
/// </summary>
public interface IOutputPort : IVirtualDevice
{
    /// <summary>
    /// Gets the output ports implemented by the device.
    /// </summary>
    IEnumerable<int> OutputPorts { get; }

    /// <summary>
    /// Writes a single byte to one of the device's supported ports.
    /// </summary>
    /// <param name="port">Port where byte will be written.</param>
    /// <param name="value">Value to write to the port.</param>
    void WriteByte(int port, byte value);
    /// <summary>
    /// Writes two bytes to one or two of the device's supported ports.
    /// </summary>
    /// <param name="port">Port where first byte will be written.</param>
    /// <param name="value">Value to write to the ports.</param>
    void WriteWord(int port, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        this.WriteByte(port, buffer[0]);
        this.WriteByte(port + 1, buffer[1]);
    }
}
