namespace Spice86.Emulator.IOPorts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Interface classes handling port data through <see cref="IIOPortHandler"/> have to follow
/// </summary>
public interface IIOPortHandler
{
    void InitPortHandlers(IOPortDispatcher ioPortDispatcher);
    int Inb(int port);
    int Inw(int port);
    void Outb(int port, int value);
    void Outw(int port, int value);
}
