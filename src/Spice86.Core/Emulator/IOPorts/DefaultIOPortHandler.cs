namespace Spice86.Core.Emulator.IOPorts;

using System.Numerics;
using System.Runtime.CompilerServices;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Interfaces;

/// <summary>
/// Abstract base class for all classes that handle port reads and writes. Provides a default implementation for handling unhandled ports.
/// </summary>
public abstract class DefaultIOPortHandler : IIOPortHandler {
    /// <summary>
    /// Contains the argument of the last <see cref="ReadByte"/> operation.
    /// </summary>
    public ushort LastPortRead { get; private set; }

    /// <summary>
    /// Contains the first argument of the last <see cref="WriteByte"/> operation.
    /// </summary>
    public ushort LastPortWritten { get; private set; }

    /// <summary>
    /// Contains the second argument of the last <see cref="WriteByte"/> operation.
    /// </summary>
    public uint LastPortWrittenValue { get; private set; }

    /// <summary>
    /// The logger service implementation.
    /// </summary>
    protected readonly ILoggerService _loggerService;

    /// <summary>
    /// Whether we raise an exception when a port wasn't handled.
    /// </summary>
    protected bool _failOnUnhandledPort;

    /// <summary>
    /// The CPU state.
    /// </summary>
    protected readonly State _state;

    /// <summary>
    /// Constructor for DefaultIOPortHandler
    /// </summary>
    /// <param name="state">The CPU Registers and Flags.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">Logger service implementation.</param>
    protected DefaultIOPortHandler(State state, bool failOnUnhandledPort, ILoggerService loggerService) {
        _loggerService = loggerService;
        _state = state;
        _failOnUnhandledPort = failOnUnhandledPort;
    }


    /// <inheritdoc />
    public void UpdateLastPortRead(ushort port) {
        LastPortRead = port;
    }


    /// <inheritdoc />
    public void UpdateLastPortWrite(ushort port, uint value) {
        LastPortWritten = port;
        LastPortWrittenValue = value;
    }

    /// <inheritdoc />
    public virtual byte ReadByte(ushort port) {
        LogUnhandledPortRead(port);
        return OnUnandledIn(port);
    }

    /// <summary>
    /// Logs that an unhandled port read error occured.
    /// </summary>
    /// <param name="port">The port number that was read.</param>
    /// <param name="methodName">The name of the calling method. Automatically populated if not specified.</param>
    protected void LogUnhandledPortRead(ushort port, [CallerMemberName] string? methodName = null) {
        if (_failOnUnhandledPort && _loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Unhandled port read: 0x{PortNumber:X4} in {MethodName}", port, methodName);
        }
    }


    /// <summary>
    /// Logs that an unhandled port write error occured.
    /// </summary>
    /// <param name="port">The port number that was written.</param>
    /// <param name="value">The value that was supposed to be written to the port.</param>
    /// <param name="methodName">The name of the calling method. Automatically populated if not specified.</param>
    protected void LogUnhandledPortWrite<T>(ushort port, T value, [CallerMemberName] string? methodName = null)
        where T : INumber<T> {
        if (_failOnUnhandledPort && _loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Unhandled port write: 0x{PortNumber:X4}, 0x{Value:X4} in {MethodName}", port, value,
                methodName);
        }
    }

    /// <inheritdoc />
    public virtual ushort ReadWord(ushort port) {
        LogUnhandledPortRead(port);
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_state, port);
        }

        return ushort.MaxValue;
    }

    /// <inheritdoc />
    public virtual uint ReadDWord(ushort port) {
        LogUnhandledPortRead(port);
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_state, port);
        }

        return uint.MaxValue;
    }

    /// <inheritdoc />
    public virtual void WriteByte(ushort port, byte value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    /// <inheritdoc />
    public virtual void WriteWord(ushort port, ushort value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    /// <inheritdoc />
    public virtual void WriteDWord(ushort port, uint value) {
        LogUnhandledPortWrite(port, value);
        OnUnhandledPort(port);
    }

    /// <summary>
    /// Invoked when an unhandled input operation is performed on a port.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <returns>A default value.</returns>
    protected virtual byte OnUnandledIn(ushort port) {
        LogUnhandledPortRead(port);
        OnUnhandledPort(port);
        return byte.MaxValue;
    }

    /// <summary>
    /// Invoked when an unhandled port operation is performed.
    /// </summary>
    /// <param name="port">The port number.</param>
    protected virtual void OnUnhandledPort(ushort port) {
        if (_failOnUnhandledPort) {
            throw new UnhandledIOPortException(_state, port);
        }
    }
}