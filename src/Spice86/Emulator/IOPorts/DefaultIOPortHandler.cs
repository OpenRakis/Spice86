namespace Spice86.Emulator.IOPorts; 

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;

public abstract class DefaultIOPortHandler : IIOPortHandler {
    protected Cpu _cpu;

    protected bool _failOnUnhandledPort;

    protected Machine _machine;

    protected Memory _memory;

    protected DefaultIOPortHandler(Machine machine, bool failOnUnhandledPort) {
        this._machine = machine;
        this._memory = machine.Memory;
        this._cpu = machine.Cpu;
        this._failOnUnhandledPort = failOnUnhandledPort;
    }

    public virtual byte Inb(int port) {
        return OnUnandledIn(port);
    }

    /// <summary> NOP for <see cref="DefaultIOPortHandler" /> </summary>
    public virtual void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
    }

    public virtual ushort Inw(int port) {
        return OnUnandledIn(port);
    }

    public virtual void Outb(int port, byte value) {
        OnUnhandledPort(port);
    }

    public virtual void Outw(int port, ushort value) {
        OnUnhandledPort(port);
    }

    protected virtual byte OnUnandledIn(int port) {
        OnUnhandledPort(port);
        return 0;
    }

    protected virtual void OnUnhandledPort(int port) {
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_machine, port);
        }
    }
}
