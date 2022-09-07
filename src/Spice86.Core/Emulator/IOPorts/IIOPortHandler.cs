namespace Spice86.Core.Emulator.IOPorts;
/// <summary>
/// Interface classes handling port data through <see cref="IIOPortHandler" /> have to follow
/// </summary>
public interface IIOPortHandler {
    void InitPortHandlers(IOPortDispatcher ioPortDispatcher);

    byte ReadByte(int port);
    ushort ReadWord(int port);
    uint ReadDWord(int port);

    void WriteByte(int port, byte value);
    void WriteWord(int port, ushort value);
    void WriteDWord(int port, uint value);
}