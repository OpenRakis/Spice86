namespace Spice86.Core.Emulator.Gdb;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Handles GDB commands related to breakpoints and stepping through instructions.
/// </summary>
public class GdbCommandBreakpointHandler {
    private readonly ILoggerService _loggerService;
    private readonly GdbIo _gdbIo;
    private volatile bool _resumeEmulatorOnCommandEnd = true;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;
    private readonly CPU.State? _state;
    private readonly IMemory? _memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GdbCommandBreakpointHandler"/> class.
    /// </summary>
    /// <param name="pauseHandler">The class responsible for pausing/resuming emulation via GDB commands.</param>
    /// <param name="gdbIo">The GDB I/O handler.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="emulatorBreakpointsManager">The class that stores emulation breakpoints.</param>
    /// <param name="state">The CPU state for expression evaluation.</param>
    /// <param name="memory">The memory interface for expression evaluation.</param>
    public GdbCommandBreakpointHandler(
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IPauseHandler pauseHandler, GdbIo gdbIo, ILoggerService loggerService,
        CPU.State? state = null, IMemory? memory = null) {
        _loggerService = loggerService.WithLogLevel(LogEventLevel.Verbose);
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _pauseHandler.Paused += OnPauseFromEmulator;
        _gdbIo = gdbIo;
        _state = state;
        _memory = memory;
    }

    /// <summary>
    /// Handles a pause coming event from the emulator UI, so GDB client can inspect the state of the emulator again.
    /// </summary>
    private void OnPauseFromEmulator() {
        if(!_resumeEmulatorOnCommandEnd) {
            return;
        }
        if(_loggerService.Equals(LogEventLevel.Debug)) {
            _loggerService.Debug("Notification of emulator pause from the UI to the GDB client.");
        }
        _resumeEmulatorOnCommandEnd = false;
        SendS05StringToGdb();
    }

    /// <summary>
    /// Adds a breakpoint to the _machineBreakpoints.
    /// </summary>
    /// <param name="commandContent">The breakpoint command string.</param>
    /// <returns>A response string to send back to GDB.</returns>
    public string AddBreakpoint(string commandContent) {
        BreakPoint? breakPoint = ParseBreakPoint(commandContent);
        if(breakPoint is not null) {
            _emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Breakpoint added!\n{@BreakPoint}", breakPoint);
            }
        }
        return _gdbIo.GenerateResponse("OK");
    }

    /// <summary>
    /// Sends a response to GDB and requests that the emulator be resumed after the breakpoint is hit.
    /// </summary>
    /// <returns>A response string to send back to GDB.</returns>
    public string ContinueCommand() {
        _resumeEmulatorOnCommandEnd = true;

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
        if (!_gdbIo.IsClientConnected()) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Breakpoint reached but client is not connected. Nothing to do.\n{@BreakPoint}", breakPoint);
            }
            return;
        }
        _pauseHandler.RequestPause($"Gdb breakpoint {breakPoint.BreakPointType} hit");
        _resumeEmulatorOnCommandEnd = false;
        try {
            SendS05StringToGdb();
        } catch (IOException e) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(e, "IOException while sending breakpoint info");
            }
        }
    }

    private void SendS05StringToGdb() {
        _gdbIo.SendResponse(_gdbIo.GenerateResponse("S05"));
    }

    /// <summary>
    /// Parses a breakpoint command string and returns a <see cref="BreakPoint"/> object.
    /// </summary>
    /// <param name="command">The breakpoint command string to parse.</param>
    /// <returns>A <see cref="BreakPoint"/> object if parsing succeeds, otherwise null.</returns>
    /// <remarks>
    /// Supports GDB remote protocol format: type,address,kind[;X:condition_expression]
    /// where X is a condition type (we support 'cond' or 'X' for condition expressions).
    /// Example: "0,1000,1;X:ax==0x100" sets an execution breakpoint at 0x1000 with condition "ax==0x100"
    /// </remarks>
    public BreakPoint? ParseBreakPoint(string command) {
        try {
            // Check for optional condition parameter (separated by semicolon)
            string? conditionExpression = null;
            string baseCommand = command;
            
            int semicolonIndex = command.IndexOf(';');
            if (semicolonIndex >= 0) {
                baseCommand = command.Substring(0, semicolonIndex);
                string conditionPart = command.Substring(semicolonIndex + 1);
                
                // Parse condition part - format is "X:expression" or "cond:expression"
                int colonIndex = conditionPart.IndexOf(':');
                if (colonIndex >= 0) {
                    string condType = conditionPart.Substring(0, colonIndex);
                    if (condType.Equals("X", StringComparison.OrdinalIgnoreCase) || 
                        condType.Equals("cond", StringComparison.OrdinalIgnoreCase)) {
                        conditionExpression = conditionPart.Substring(colonIndex + 1);
                        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                            _loggerService.Debug("Parsed condition expression: {Condition}", conditionExpression);
                        }
                    }
                }
            }
            
            string[] commandSplit = baseCommand.Split(",");
            int type = int.Parse(commandSplit[0]);
            BreakPointType? breakPointType = type switch {
                0 => BreakPointType.CPU_EXECUTION_ADDRESS,
                1 => BreakPointType.CPU_EXECUTION_ADDRESS,
                2 => BreakPointType.MEMORY_WRITE,
                3 => BreakPointType.MEMORY_READ,
                4 => BreakPointType.MEMORY_ACCESS,
                _ => null
            };
            if (breakPointType == null) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("Cannot parse breakpoint type {Type} for command {Command}", type, command);
                }
                return null;
            }
            long address = ConvertUtils.ParseHex32(commandSplit[1]);
            if (address > A20Gate.EndOfHighMemoryArea) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Cannot install breakpoint at address {Address} because it is higher than ram size {RamSize}", address, A20Gate.EndOfHighMemoryArea);
                }
                return null;
            }
            
            // Compile condition expression if present
            Func<long, bool>? condition = null;
            if (!string.IsNullOrWhiteSpace(conditionExpression) && _state != null && _memory != null) {
                try {
                    var parser = new Shared.Emulator.VM.Breakpoint.Expression.ExpressionParser();
                    var ast = parser.Parse(conditionExpression);
                    condition = (addr) => {
                        var context = new BreakpointExpressionContext(_state, _memory, addr);
                        return ast.Evaluate(context) != 0;
                    };
                    if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information("Compiled conditional breakpoint: {Expression}", conditionExpression);
                    }
                } catch (Exception ex) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning(ex, "Failed to parse condition expression: {Expression}", conditionExpression);
                    }
                    // Continue without condition if parsing fails
                    conditionExpression = null;
                }
            }
            
            return new AddressBreakPoint((BreakPointType)breakPointType, address, OnBreakPointReached, false, condition, conditionExpression);
        } catch (FormatException nfe) {
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
    /// <returns>Either an empty string if the breakpoint string could not be parsed, or "OK"</returns>
    public string RemoveBreakpoint(string commandContent) {
        BreakPoint? breakPoint = ParseBreakPoint(commandContent);
        if (breakPoint == null) {
            return _gdbIo.GenerateResponse("");
        }
        _emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, false);
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
        BreakPoint stepBreakPoint = new UnconditionalBreakPoint(BreakPointType.CPU_EXECUTION_ADDRESS, OnBreakPointReached, true);
        _emulatorBreakpointsManager.ToggleBreakPoint(stepBreakPoint, true);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Breakpoint added for st@ep!\n{@StepBreakPoint}", stepBreakPoint);
        }

        // Do not send anything to GDB, CPU thread will send something when breakpoint is reached
        return null;
    }
}