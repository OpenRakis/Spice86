namespace Ix86.Emulator.IOPorts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using Ix86.Emulator.Machine;
    using Ix86.Emulator.Cpu;
    using Ix86.Emulator.Memory;
    public abstract class DefaultIOPortHandler : IIOPortHandler
    {
        protected Machine machine;
        protected Memory? memory;
        protected CPU cpu;
        protected bool failOnUnhandledPort;
        protected DefaultIOPortHandler(Machine machine, bool failOnUnhandledPort)
        {
            this.machine = machine;
            this.memory = machine.GetMemory();
            this.cpu = machine.GetCpu();
            this.failOnUnhandledPort = failOnUnhandledPort;
        }

        public virtual int Inb(int port)
        {
            return OnUnandledIn(port);
        }

        /// <summary>
        /// NOP for <see cref="DefaultIOPortHandler"/>
        /// </summary>
        public virtual void InitPortHandlers(IOPortDispatcher ioPortDispatcher) { }

        public virtual int Inw(int port)
        {
            return OnUnandledIn(port);
        }

        public virtual void Outb(int port, int value)
        {
            OnUnhandledPort(port);
        }

        public virtual void Outw(int port, int value)
        {
            OnUnhandledPort(port);
        }

        protected virtual int OnUnandledIn(int port)
        {
            OnUnhandledPort(port);
            return 0;
        }

        protected virtual void OnUnhandledPort(int port)
        {
            if (failOnUnhandledPort)
            {
                throw new UnhandledIOPortException(machine, port);
            }
        }
    }
}
