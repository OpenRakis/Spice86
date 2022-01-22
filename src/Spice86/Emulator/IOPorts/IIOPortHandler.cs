namespace Spice86.Emulator.IOPorts;

/// <summary>
/// Interface classes handling port data through <see cref="IIOPortHandler" /> have to follow
/// </summary>
public interface IIOPortHandler {

    byte Inb(int port);

    void InitPortHandlers(IOPortDispatcher ioPortDispatcher);

    ushort Inw(int port);

    void Outb(int port, byte value);

    void Outw(int port, ushort value);
}