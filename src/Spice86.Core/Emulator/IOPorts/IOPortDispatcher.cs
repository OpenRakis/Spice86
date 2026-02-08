namespace Spice86.Core.Emulator.IOPorts;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Shared.Interfaces;

/// <summary>
/// Handles calling the correct dispatcher depending on port number for I/O reads and writes.
/// Applies generic IO port delays matching DOSBox's IO_USEC_read_delay() / IO_USEC_write_delay()
/// from src/hardware/port.cpp.
/// </summary>
public class IOPortDispatcher : DefaultIOPortHandler {
    private readonly Dictionary<int, IIOPortHandler> _ioPortHandlers = new();
    private readonly AddressReadWriteBreakpoints _ioBreakpoints;
    private readonly ICyclesLimiter _cyclesLimiter;

    /// <summary>
    /// IO read delay: ~1 microsecond. DOSBox uses CPU_CycleMax / (1024 / 1.0) = CPU_CycleMax / 1024.
    /// Reference: DOSBox src/hardware/port.cpp IODELAY_READ_MICROS = 1.0, IODELAY_READ_MICROSk = 1024.
    /// </summary>
    private const int IoDelayReadDivisor = 1024;

    /// <summary>
    /// IO write delay: ~0.75 microseconds. DOSBox uses CPU_CycleMax / (1024 / 0.75) = CPU_CycleMax / 1365.
    /// Reference: DOSBox src/hardware/port.cpp IODELAY_WRITE_MICROS = 0.75, IODELAY_WRITE_MICROSk = 1365.
    /// </summary>
    private const int IoDelayWriteDivisor = 1365;

    /// <summary>
    /// Initializes a new instance of the <see cref="IOPortDispatcher"/> class.
    /// </summary>
    /// <param name="ioBreakpoints">Breakpoints related to IO.</param>
    /// <param name="state">The CPU state, such as registers.</param>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="cyclesLimiter">The cycle limiter for IO port access delays.</param>
    public IOPortDispatcher(AddressReadWriteBreakpoints ioBreakpoints,
        State state, ILoggerService loggerService, bool failOnUnhandledPort,
        ICyclesLimiter cyclesLimiter) :
        base(state, failOnUnhandledPort, loggerService) {
        _ioBreakpoints = ioBreakpoints;
        _failOnUnhandledPort = failOnUnhandledPort;
        _cyclesLimiter = cyclesLimiter;
    }

    /// <summary>
    /// Adds an I/O port handler to the dispatcher.
    /// </summary>
    /// <param name="port">The port number.</param>
    /// <param name="ioPortHandler">The I/O port handler to add.</param>
    public void AddIOPortHandler(int port, IIOPortHandler ioPortHandler) {
        _ioPortHandlers.Add(port, ioPortHandler);
    }

    /// <inheritdoc/>
    public override byte ReadByte(ushort port) {
        UpdateLastPortRead(port);
        IoReadDelay();
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
        IoReadDelay();
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
        IoReadDelay();
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
        IoWriteDelay();
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
        IoWriteDelay();
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
        IoWriteDelay();
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

    /// <summary>
    /// Applies a ~1 microsecond IO read delay by consuming cycles from the current tick budget.
    /// Reference: DOSBox src/hardware/port.cpp IO_USEC_read_delay()
    /// </summary>
    private void IoReadDelay() {
        int delayCycles = _cyclesLimiter.TickCycleMax / IoDelayReadDivisor;
        if (delayCycles > 0) {
            _cyclesLimiter.ConsumeIoCycles(delayCycles);
        }
    }

    /// <summary>
    /// Applies a ~0.75 microsecond IO write delay by consuming cycles from the current tick budget.
    /// Reference: DOSBox src/hardware/port.cpp IO_USEC_write_delay()
    /// </summary>
    private void IoWriteDelay() {
        int delayCycles = _cyclesLimiter.TickCycleMax / IoDelayWriteDivisor;
        if (delayCycles > 0) {
            _cyclesLimiter.ConsumeIoCycles(delayCycles);
        }
    }
}
