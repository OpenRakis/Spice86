namespace Spice86.Core.Emulator.IOPorts;

/// <summary>
/// Interface for all classes that handle port reads and writes.
/// </summary>
public interface IIOPortHandler {
    /// <summary>
    /// Reads a single byte from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to read from.</param>
    /// <returns>The value read from the port.</returns>
    byte ReadByte(ushort port);

    /// <summary>
    /// Reads a 16-bit word (two bytes) from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to read from.</param>
    /// <returns>The value read from the port.</returns>
    ushort ReadWord(ushort port);

    /// <summary>
    /// Reads a 32-bit double word (four bytes) from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to read from.</param>
    /// <returns>The value read from the port.</returns>
    uint ReadDWord(ushort port);

    /// <summary>
    /// Writes a single byte to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to write to.</param>
    /// <param name="value">The value to write to the port.</param>
    void WriteByte(ushort port, byte value);

    /// <summary>
    /// Writes a 16-bit word (two bytes) to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to write to.</param>
    /// <param name="value">The value to write to the port.</param>
    void WriteWord(ushort port, ushort value);

    /// <summary>
    /// Writes a 32-bit double word (four bytes) to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to write to.</param>
    /// <param name="value">The value to write to the port.</param>
    void WriteDWord(ushort port, uint value);

    /// <summary>
    /// Updates the <see cref="LastPortRead"/> for the internal UI debugger.
    /// </summary>
    /// <param name="port">The port number</param>
    void UpdateLastPortRead(ushort port);

    /// <summary>
    /// Updates the <see cref="LastPortWritten"/> and value for the internal UI debugger.
    /// </summary>
    /// <param name="port">The port number</param>
    /// <param name="value">The value written to the port.</param>
    void UpdateLastPortWrite(ushort port, uint value);
}