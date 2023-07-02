namespace Spice86.Core.Emulator.Gdb;

using System.Diagnostics;

using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Serilog.Events;

/// <summary>
/// Handles GDB commands related to breakpoints and stepping through instructions.
/// </summary>
public class GdbCommandBreakpointHandler {
    private readonly ILoggerService _loggerService;
    private readonly GdbIo _gdbIo;
    private readonly Machine _machine;
    private volatile bool _resumeEmulatorOnCommandEnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="GdbCommandBreakpointHandler"/> class.
    /// </summary>
    /// <param name="gdbIo">The GDB I/O handler.</param>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public GdbCommandBreakpointHandler(GdbIo gdbIo, Machine machine, ILoggerService loggerService) {
        _loggerService = loggerService;
        _gdbIo = gdbIo;
        _machine = machine;
    }

    /// <summary>
    /// Adds a breakpoint to the machine.
    /// </summary>
    /// <param name="commandContent">The breakpoint command string.</param>
    /// <returns>A response string to send back to GDB.</returns>
    public string AddBreakpoint(string commandContent) {
        BreakPoint? breakPoint = ParseBreakPoint(commandContent);
        _machine.MachineBreakpoints.ToggleBreakPoint(breakPoint, true);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Breakpoint added!\n{@BreakPoint}", breakPoint);
        }
        return _gdbIo.GenerateResponse("OK");
    }

    /// <summary>
    /// Sends a response to GDB and requests that the emulator be resumed after the breakpoint is hit.
    /// </summary>
    /// <returns>A response string to send back to GDB.</returns>
    public string ContinueCommand() {
        _resumeEmulatorOnCommandEnd = true;
        _machine.MachineBreakpoints.PauseHandler.RequestResume();

        // Do not send anything to GDB, CPU thread will send something when breakpoint is reached
        return _gdbIo.GenerateResponse("OK");
    }
    
    /// <summary>
    /// Gets or sets a value indicating whether the emulator should be resumed when GDB command has ended.
    /// </summary>
    public bool ResumeEmulatorOnCommandEnd { get => _resumeEmulatorOnCommandEnd; set => _resumeEmulatorOnCommandEnd = value; }

    /// <summary>
    /// Handles a breakpoint being hit.
    /// </summary>
    /// <param name="breakPoint">The <see cref="BreakPoint"/> object representing the breakpoint that was hit.</param>
    public void OnBreakPointReached(BreakPoint breakPoint) {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Breakpoint reached!\n@{@BreakPoint}", breakPoint);
        }
        if (!_gdbIo.IsClientConnected) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Breakpoint reached but client is not connected. Nothing to do.\n{@BreakPoint}", breakPoint);
            }
            return;
        }
        _machine.MachineBreakpoints.PauseHandler.RequestPause();
        _resumeEmulatorOnCommandEnd = false;
        try {
            _gdbIo.SendResponse(_gdbIo.GenerateResponse("S05"));
        } catch (IOException e) {
            e.Demystify();
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(e, "IOException while sending breakpoint info");
            }
        }
    }
    
    /// <summary>
    /// Parses a breakpoint command string and returns a <see cref="BreakPoint"/> object.
    /// </summary>
    /// <param name="command">The breakpoint command string to parse.</param>
    /// <returns>A <see cref="BreakPoint"/> object if parsing succeeds, otherwise null.</returns>
    public BreakPoint? ParseBreakPoint(string command) {
        try {
            string[] commandSplit = command.Split(",");
            int type = int.Parse(commandSplit[0]);
            BreakPointType? breakPointType = type switch {
                0 => BreakPointType.EXECUTION,
                1 => BreakPointType.EXECUTION,
                2 => BreakPointType.WRITE,
                3 => BreakPointType.READ,
                4 => BreakPointType.ACCESS,
                _ => null
            };
            if (breakPointType == null) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("Cannot parse breakpoint type {Type} for command {Command}", type, command);
                }
                return null;
            }
            long address = ConvertUtils.ParseHex32(commandSplit[1]);
            if (address > Memory.EndOfHighMemoryArea) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Cannot install breakpoint at address {Address} because it is higher than ram size {RamSize}", address, Memory.EndOfHighMemoryArea);
                }
                return null;
            }
            return new AddressBreakPoint((BreakPointType)breakPointType, address, OnBreakPointReached, false);
        } catch (FormatException nfe) {
            nfe.Demystify();
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(nfe, "Cannot parse breakpoint {Command}", command);
            }
            return null;
        }
    }

    /// <summary>
    /// Removes a breakpoint
    /// </summary>
    /// <param name="commandContent">The breakpoint command string.</param>
    /// <returns></returns>
    public string RemoveBreakpoint(string commandContent) {
        BreakPoint? breakPoint = ParseBreakPoint(commandContent);
        if (breakPoint == null) {
            return _gdbIo.GenerateResponse("");
        }
        _machine.MachineBreakpoints.ToggleBreakPoint(breakPoint, false);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Breakpoint removed@!\n{@BreakPoint}", breakPoint);
        }
        return _gdbIo.GenerateResponse("OK");
    }

    /// <summary>
    /// Executes a single CPU instruction
    /// </summary>
    /// <returns><c>null</c></returns>
    public string? Step() {
        _resumeEmulatorOnCommandEnd = true;

        // will pause the CPU at the next instruction unconditionally
        BreakPoint stepBreakPoint = new UnconditionalBreakPoint(BreakPointType.EXECUTION, OnBreakPointReached, true);
        _machine.MachineBreakpoints.ToggleBreakPoint(stepBreakPoint, true);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Breakpoint added for st@ep!\n{@StepBreakPoint}", stepBreakPoint);
        }

        // Do not send anything to GDB, CPU thread will send something when breakpoint is reached
        return null;
    }
}