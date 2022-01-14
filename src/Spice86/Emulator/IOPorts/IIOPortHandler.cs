namespace Spice86.Emulator.IOPorts;

/// <summary>
/// Interface classes handling port data through <see cref="IIOPortHandler" /> have to follow
/// </summary>
public interface IIOPortHandler {

    int Inb(int port);

    void InitPortHandlers(IOPortDispatcher ioPortDispatcher);

    int Inw(int port);

    void Outb(int port, int value);

    void Outw(int port, int value);
}