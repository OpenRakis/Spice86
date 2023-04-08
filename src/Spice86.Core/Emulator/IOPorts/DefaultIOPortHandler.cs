namespace Spice86.Core.Emulator.IOPorts;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System.Numerics;
using System.Runtime.CompilerServices;

public abstract class DefaultIOPortHandler : IIOPortHandler {
    protected ILoggerService _loggerService;
    
    protected Cpu _cpu;

    protected bool _failOnUnhandledPort;

    protected Machine _machine;

    protected Memory _memory;

    protected Configuration Configuration { get; init; }

    protected DefaultIOPortHandler(Machine machine, Configuration configuration, ILoggerService loggerService) {
        Configuration = configuration;
        _machine = machine;
        _loggerService = loggerService;
        _memory = machine.Memory;
        _cpu = machine.Cpu;
        _failOnUnhandledPort = Configuration.FailOnUnhandledPort;
    }

    /// <summary> NOP for <see cref="DefaultIOPortHandler" /> </summary>
    public virtual void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
    }

    public virtual byte ReadByte(int port) {
        LogUnhandledPortRead(port);
        return OnUnandledIn(port);
    }

    protected void LogUnhandledPortRead(int port, [CallerMemberName] string? methodName = null)
    {
        if (_loggerService.IsEnabled(LogEventLevel.Error))
        {
            _loggerService.Error("Unhandled port read: {@PortNumber} in {@MethodName}", port, methodName);
        }
    }
    
    protected void LogUnhandledPortWrite<T>(int port, T value, [CallerMemberName] string? methodName = null) where T : INumber<T>
    {
        if (_loggerService.IsEnabled(LogEventLevel.Error))
        {
            _loggerService.Error("Unhandled port write: {@PortNumber:X4}, {@Value:X4} in {@MethodName}", port, value, methodName);
        }
    }

    public virtual ushort ReadWord(int port) {
        LogUnhandledPortRead(port);
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_machine, port);
        }
        return ushort.MaxValue;
    }

    public virtual uint ReadDWord(int port) {
        LogUnhandledPortRead(port);
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_machine, port);
        }
        return uint.MaxValue;
    }

    public virtual void WriteByte(int port, byte value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    public virtual void WriteWord(int port, ushort value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    public virtual void WriteDWord(int port, uint value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    protected virtual byte OnUnandledIn(int port) {
        LogUnhandledPortRead(port);
        OnUnhandledPort(port);
        return byte.MaxValue;
    }

    protected virtual void OnUnhandledPort(int port) {
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_machine, port);
        }
    }
}
