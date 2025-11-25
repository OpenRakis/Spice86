namespace Spice86.Core.Emulator.IOPorts;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

/// <summary>
/// Routes I/O port read and write operations to the appropriate hardware device handlers.
/// </summary>
/// <remarks>
/// In x86 architecture, I/O ports provide a separate address space from memory for communicating with hardware devices.
/// The dispatcher maintains a registry of handlers for different port numbers and routes IN/OUT instructions to them.
/// <para>
/// Common port ranges include:
/// <list type="bullet">
/// <item>0x20-0x21: Master Programmable Interrupt Controller (PIC)</item>
/// <item>0x40-0x43: Programmable Interval Timer (PIT)</item>
/// <item>0x60-0x64: Keyboard Controller (8042)</item>
/// <item>0x3C0-0x3DF: VGA graphics controller</item>
/// <item>0x220-0x22F: Sound Blaster</item>
/// </list>
/// </para>
/// </remarks>
public class IOPortDispatcher : DefaultIOPortHandler {
    private readonly Dictionary<int, IIOPortHandler> _ioPortHandlers = new();
    private readonly AddressReadWriteBreakpoints _ioBreakpoints;

    /// <summary>
    /// Initializes a new instance of the <see cref="IOPortDispatcher"/> class.
    /// </summary>
    /// <param name="ioBreakpoints">Breakpoints related to IO.</param>1
    /// <param name="state">The CPU state, such as registers.</param>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    public IOPortDispatcher(AddressReadWriteBreakpoints ioBreakpoints,
        State state, ILoggerService loggerService, bool failOnUnhandledPort) :
        base(state, failOnUnhandledPort, loggerService) {
        _ioBreakpoints = ioBreakpoints;
        _failOnUnhandledPort = failOnUnhandledPort;
    }

    /// <summary>
    /// Adds an I/O port handler to the dispatcher.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <param name="ioPortHandler">The I/O port handler to add.</param>
    public void AddIOPortHandler(int port, IIOPortHandler ioPortHandler) {
        _ioPortHandlers.Add(port, ioPortHandler);
    }

    /// <summary>
    ///     Removes a previously registered I/O port handler from the dispatcher.
    /// </summary>
    /// <param name="port">The port number to remove.</param>
    /// <returns><see langword="true" /> if the handler was removed; otherwise <see langword="false" />.</returns>
    public bool RemoveIOPortHandler(int port) {
        return _ioPortHandlers.Remove(port);
    }

    /// <inheritdoc/>
    public override byte ReadByte(ushort port) {
        UpdateLastPortRead(port);
        _ioBreakpoints.MonitorReadAccess(port);
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("{MethodName} {PortHandlerTypeName} {PortNumber}", nameof(ReadByte),
                    entry.GetType(), port);
            }
            entry.UpdateLastPortRead(port);
            return entry.ReadByte(port);
        }

        return base.ReadByte(port);
    }

    /// <inheritdoc/>
    public override ushort ReadWord(ushort port) {
        UpdateLastPortRead(port);
        _ioBreakpoints.MonitorReadAccess(port);
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("{MethodName} {PortHandlerTypeName} {PortNumber}", nameof(ReadWord),
                    entry.GetType(), port);
            }
            entry.UpdateLastPortRead(port);
            return entry.ReadWord(port);
        }

        return base.ReadWord(port);
    }

    /// <inheritdoc/>
    public override uint ReadDWord(ushort port) {
        UpdateLastPortRead(port);
        _ioBreakpoints.MonitorReadAccess(port);
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("{MethodName} {PortHandlerTypeName} {PortNumber}", nameof(ReadDWord),
                    entry.GetType(), port);
            }
            entry.UpdateLastPortRead(port);
            return entry.ReadDWord(port);
        }

        return base.ReadDWord(port);
    }

    /// <inheritdoc/>
    public override void WriteByte(ushort port, byte value) {
        UpdateLastPortWrite(port, value);
        _ioBreakpoints.MonitorWriteAccess(port);
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("{MethodName} {PortHandlerTypeName} {PortNumber} {WrittenValue}",
                    nameof(WriteByte), entry.GetType(), port, value);
            }
            entry.UpdateLastPortWrite(port, value);
            entry.WriteByte(port, value);
        } else {
            base.WriteByte(port, value);
        }
    }

    /// <inheritdoc/>
    public override void WriteWord(ushort port, ushort value) {
        UpdateLastPortWrite(port, value);
        _ioBreakpoints.MonitorWriteAccess(port);
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("{MethodName} {PortHandlerTypeName} {PortNumber} {WrittenValue}",
                    nameof(WriteWord), entry.GetType(), port, value);
            }
            entry.UpdateLastPortWrite(port, value);
            entry.WriteWord(port, value);
        } else {
            base.WriteWord(port, value);
        }
    }

    /// <inheritdoc/>
    public override void WriteDWord(ushort port, uint value) {
        UpdateLastPortWrite(port, value);
        _ioBreakpoints.MonitorWriteAccess(port);
        if (_ioPortHandlers.TryGetValue(port, out IIOPortHandler? entry)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("{MethodName} {PortHandlerTypeName} {PortNumber} {WrittenValue}",
                    nameof(WriteDWord), entry.GetType(), port, value);
            }
            entry.UpdateLastPortWrite(port, value);
            entry.WriteDWord(port, value);
        } else {
            base.WriteDWord(port, value);
        }
    }
}