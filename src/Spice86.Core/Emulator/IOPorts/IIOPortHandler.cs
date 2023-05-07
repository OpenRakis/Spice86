namespace Spice86.Core.Emulator.IOPorts;

/// <summary>
/// Interface for all classes that handle port reads and writes.
/// </summary>
public interface IIOPortHandler {
    /// <summary>
    /// Initializes the port handlers with an IOPortDispatcher object.
    /// </summary>
    /// <param name="ioPortDispatcher">The IOPortDispatcher object used to dispatch port accesses.</param>
    void InitPortHandlers(IOPortDispatcher ioPortDispatcher);

    /// <summary>
    /// Reads a single byte from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to read from.</param>
    /// <returns>The value read from the port.</returns>
    byte ReadByte(int port);

    /// <summary>
    /// Reads a 16-bit word (two bytes) from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to read from.</param>
    /// <returns>The value read from the port.</returns>
    ushort ReadWord(int port);

    /// <summary>
    /// Reads a 32-bit double word (four bytes) from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to read from.</param>
    /// <returns>The value read from the port.</returns>
    uint ReadDWord(int port);

    /// <summary>
    /// Writes a single byte to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to write to.</param>
    /// <param name="value">The value to write to the port.</param>
    void WriteByte(int port, byte value);

    /// <summary>
    /// Writes a 16-bit word (two bytes) to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to write to.</param>
    /// <param name="value">The value to write to the port.</param>
    void WriteWord(int port, ushort value);

    /// <summary>
    /// Writes a 32-bit double word (four bytes) to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number to write to.</param>
    /// <param name="value">The value to write to the port.</param>
    void WriteDWord(int port, uint value);
}