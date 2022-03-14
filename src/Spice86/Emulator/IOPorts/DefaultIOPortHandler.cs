namespace Spice86.Emulator.IOPorts;

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;

public abstract class DefaultIOPortHandler : IIOPortHandler {
    protected Cpu _cpu;

    protected bool _failOnUnhandledPort;

    protected Machine _machine;

    protected Memory _memory;

    protected Configuration Configuration { get; init; }

    protected DefaultIOPortHandler(Machine machine, Configuration configuration) {
        this.Configuration = configuration;
        this._machine = machine;
        this._memory = machine.Memory;
        this._cpu = machine.Cpu;
        this._failOnUnhandledPort = Configuration.FailOnUnhandledPort;
    }

    public virtual byte ReadByte(int port) {
        return OnUnandledIn(port);
    }

    /// <summary> NOP for <see cref="DefaultIOPortHandler" /> </summary>
    public virtual void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
    }

    public virtual ushort ReadWord(int port) {
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_machine, port);
        }
        return ushort.MaxValue;
    }

    public virtual void WriteByte(int port, byte value) {
        OnUnhandledPort(port);
    }

    public virtual void WriteWord(int port, ushort value) {
        OnUnhandledPort(port);
    }

    protected virtual byte OnUnandledIn(int port) {
        OnUnhandledPort(port);
        return byte.MaxValue;
    }

    protected virtual void OnUnhandledPort(int port) {
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_machine, port);
        }
    }
}
