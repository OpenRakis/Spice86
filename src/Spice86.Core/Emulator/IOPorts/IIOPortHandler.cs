namespace Spice86.Core.Emulator.IOPorts;
/// <summary>
/// Interface classes handling port data through <see cref="IIOPortHandler" /> have to follow
/// </summary>
public interface IIOPortHandler {

    byte ReadByte(int port);

    void InitPortHandlers(IOPortDispatcher ioPortDispatcher);

    ushort ReadWord(int port);

    void WriteByte(int port, byte value);

    void WriteWord(int port, ushort value);
}