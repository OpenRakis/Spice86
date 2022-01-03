namespace Spice86.Emulator.IOPorts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using Spice86.Emulator.Machine;

    /// <summary>
    /// Handles calling the correct dispatcher depending on port number for I/O reads and writes.
    /// </summary>
    public class IOPortDispatcher : DefaultIOPortHandler
    {
        private readonly Dictionary<int, IIOPortHandler> ioPortHandlers = new();
        public IOPortDispatcher(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort)
        {
            this.failOnUnhandledPort = failOnUnhandledPort;
        }

        public virtual void AddIOPortHandler(int port, IIOPortHandler ioPortHandler)
        {
            ioPortHandlers.Add(port, ioPortHandler);
        }

        public override int Inb(int port)
        {
            if (ioPortHandlers.ContainsKey(port))
            {
                return ioPortHandlers[port].Inb(port);
            }

            return base.Inb(port);
        }

        public override int Inw(int port)
        {
            if (ioPortHandlers.ContainsKey(port))
            {
                return ioPortHandlers[port].Inw(port);
            }

            return base.Inw(port);
        }

        public override void Outb(int port, int value)
        {
            if (ioPortHandlers.ContainsKey(port))
            {
                ioPortHandlers[port].Outb(port, value);
            }
            else
            {
                base.Outb(port, value);
            }
        }

        public override void Outw(int port, int value)
        {
            if (ioPortHandlers.ContainsKey(port))
            {
                ioPortHandlers[port].Outw(port, value);
            }
            else
            {
                base.Outw(port, value);
            }
        }

        public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher)
        {
        }
    }
}
