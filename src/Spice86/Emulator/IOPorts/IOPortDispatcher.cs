namespace Spice86.Emulator.IOPorts {

    using Spice86.Emulator.VM;

    using System.Collections.Generic;

    /// <summary>
    /// Handles calling the correct dispatcher depending on port number for I/O reads and writes.
    /// </summary>
    public class IOPortDispatcher : DefaultIOPortHandler {
        private readonly Dictionary<int, IIOPortHandler> ioPortHandlers = new();

        public IOPortDispatcher(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {
            this.failOnUnhandledPort = failOnUnhandledPort;
        }

        public void AddIOPortHandler(int port, IIOPortHandler ioPortHandler) {
            ioPortHandlers.Add(port, ioPortHandler);
        }

        public override byte Inb(int port) {
            if (ioPortHandlers.ContainsKey(port)) {
                return ioPortHandlers[port].Inb(port);
            }

            return base.Inb(port);
        }

        public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        }

        public override ushort Inw(int port) {
            if (ioPortHandlers.ContainsKey(port)) {
                return ioPortHandlers[port].Inw(port);
            }

            return base.Inw(port);
        }

        public override void Outb(int port, byte value) {
            if (ioPortHandlers.ContainsKey(port)) {
                ioPortHandlers[port].Outb(port, value);
            } else {
                base.Outb(port, value);
            }
        }

        public override void Outw(int port, ushort value) {
            if (ioPortHandlers.ContainsKey(port)) {
                ioPortHandlers[port].Outw(port, value);
            } else {
                base.Outw(port, value);
            }
        }
    }
}