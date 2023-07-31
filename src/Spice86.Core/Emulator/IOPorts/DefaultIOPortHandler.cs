namespace Spice86.Core.Emulator.IOPorts;

using System.Numerics;
using System.Runtime.CompilerServices;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Abstract base class for all classes that handle port reads and writes. Provides a default implementation for handling unhandled ports.
/// </summary>
public abstract class DefaultIOPortHandler : IIOPortHandler {
    /// <summary>
    /// The logger service implementation.
    /// </summary>
    protected readonly ILoggerService _loggerService;

    /// <summary>
    /// The CPU interpreter.
    /// </summary>
    protected Cpu _cpu;

    /// <summary>
    /// Whether we raise an exception when a port wasn't handled.
    /// </summary>
    protected bool _failOnUnhandledPort;

    /// <summary>
    /// The CPU state.
    /// </summary>
    protected readonly State _state;

    /// <summary>
    /// The memory bus.
    /// </summary>
    protected readonly IMemory _memory;

    /// <summary>
    /// Constructor for DefaultIOPortHandler
    /// </summary>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">Logger service implementation.</param>
    /// <param name="memory">The memory bus.</param>
    protected DefaultIOPortHandler(IMemory memory, Cpu cpu, bool failOnUnhandledPort, ILoggerService loggerService) {
        _loggerService = loggerService;
        _memory = memory;
        _cpu = cpu;
        _state = cpu.State;
        _failOnUnhandledPort = failOnUnhandledPort;
    }

    /// <summary>
    /// Initialize the port handlers.
    /// </summary>
    /// <param name="ioPortDispatcher">The I/O port dispatcher.</param>
    public virtual void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
    }

    /// <summary>
    /// Read a byte from the specified port.
    /// </summary>
    /// <param name="port">The port to read from.</param>
    /// <returns>The value read from the port.</returns>
    public virtual byte ReadByte(int port) {
        LogUnhandledPortRead(port);
        return OnUnandledIn(port);
    }

    /// <summary>
    /// Logs that an unhandled port read error occured.
    /// </summary>
    /// <param name="port">The port number that was read.</param>
    /// <param name="methodName">The name of the calling method. Automatically populated if not specified.</param>
    protected void LogUnhandledPortRead(int port, [CallerMemberName] string? methodName = null) {
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Unhandled port read: {PortNumber} in {MethodName}", port, methodName);
        }
    }


    /// <summary>
    /// Logs that an unhandled port write error occured.
    /// </summary>
    /// <param name="port">The port number that was written.</param>
    /// <param name="value">The value that was supposed to be written to the port.</param>
    /// <param name="methodName">The name of the calling method. Automatically populated if not specified.</param>
    protected void LogUnhandledPortWrite<T>(int port, T value, [CallerMemberName] string? methodName = null)
        where T : INumber<T> {
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Unhandled port write: {PortNumber}, {Value} in {MethodName}", port, value,
                methodName);
        }
    }

    /// <summary>
    /// Reads a word from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>The value read from the port.</returns>
    public virtual ushort ReadWord(int port) {
        LogUnhandledPortRead(port);
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_state, port);
        }

        return ushort.MaxValue;
    }

    /// <summary>
    /// Reads a double word from the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>The value read from the port.</returns>
    public virtual uint ReadDWord(int port) {
        LogUnhandledPortRead(port);
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_state, port);
        }

        return uint.MaxValue;
    }

    /// <summary>
    /// Writes a byte to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <param name="value">The value to write to the port.</param>
    public virtual void WriteByte(int port, byte value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    /// <summary>
    /// Writes a word to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <param name="value">The value to write to the port.</param>
    public virtual void WriteWord(int port, ushort value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    /// <summary>
    /// Writes a double word to the specified I/O port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <param name="value">The value to write to the port.</param>
    public virtual void WriteDWord(int port, uint value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    /// <summary>
    /// Invoked when an unhandled input operation is performed on a port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>A default value.</returns>
    protected virtual byte OnUnandledIn(int port) {
        LogUnhandledPortRead(port);
        OnUnhandledPort(port);
        return byte.MaxValue;
    }

    /// <summary>
    /// Invoked when an unhandled port operation is performed.
    /// </summary>
    /// <param name="port">The port number.</param>
    protected virtual void OnUnhandledPort(int port) {
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_state, port);
        }
    }
}