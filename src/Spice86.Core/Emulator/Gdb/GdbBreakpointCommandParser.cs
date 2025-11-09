namespace Spice86.Core.Emulator.Gdb;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Serilog.Events;

/// <summary>
/// Parses GDB remote protocol breakpoint commands (Z packets) into structured data
/// and creates Spice86 breakpoints from them.
/// </summary>
public class GdbBreakpointCommandParser {
    private readonly ILoggerService _loggerService;
    private readonly BreakpointConditionCompiler? _conditionCompiler;

    public GdbBreakpointCommandParser(ILoggerService loggerService, BreakpointConditionCompiler? conditionCompiler = null) {
        _loggerService = loggerService;
        _conditionCompiler = conditionCompiler;
    }

    /// <summary>
    /// Parses a GDB breakpoint command string into a structured GdbBreakpointCommand.
    /// This method only parses the string format without interpretation or validation.
    /// </summary>
    /// <param name="command">The GDB Z command string (e.g., "0,1000,1" or "0,1000,1;X:ax==0x100").</param>
    /// <returns>A parsed GdbBreakpointCommand, or null if parsing fails.</returns>
    public GdbBreakpointCommand? Parse(string command) {
        try {
            // Split on semicolon to separate base command from condition
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
                            _loggerService.Debug("Parsed GDB condition expression: {Condition}", conditionExpression);
                        }
                    }
                }
            }
            
            // Parse base command: type,address,kind
            string[] parts = baseCommand.Split(',');
            if (parts.Length < 3) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("Invalid GDB breakpoint command format: {Command}", command);
                }
                return null;
            }

            int type = int.Parse(parts[0]);
            string address = parts[1];
            int kind = int.Parse(parts[2]);

            return new GdbBreakpointCommand(type, address, kind, conditionExpression);
        } catch (Exception ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(ex, "Failed to parse GDB breakpoint command: {Command}", command);
            }
            return null;
        }
    }

    /// <summary>
    /// Converts a parsed GDB breakpoint command into a Spice86 BreakPoint object.
    /// </summary>
    /// <param name="gdbCommand">The parsed GDB command.</param>
    /// <param name="onBreakPointReached">Callback when breakpoint is reached.</param>
    /// <returns>A BreakPoint object, or null if conversion fails.</returns>
    public BreakPoint? CreateBreakPoint(GdbBreakpointCommand gdbCommand, Action<BreakPoint> onBreakPointReached) {
        // Map GDB type to emulator breakpoint type
        BreakPointType? breakPointType = gdbCommand.Type switch {
            0 => BreakPointType.CPU_EXECUTION_ADDRESS,
            1 => BreakPointType.CPU_EXECUTION_ADDRESS,
            2 => BreakPointType.MEMORY_WRITE,
            3 => BreakPointType.MEMORY_READ,
            4 => BreakPointType.MEMORY_ACCESS,
            _ => null
        };

        if (breakPointType == null) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Unsupported GDB breakpoint type {Type}", gdbCommand.Type);
            }
            return null;
        }

        // Parse and validate address
        long address;
        try {
            address = ConvertUtils.ParseHex32(gdbCommand.Address);
        } catch (Exception ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(ex, "Failed to parse address: {Address}", gdbCommand.Address);
            }
            return null;
        }

        if (address > A20Gate.EndOfHighMemoryArea) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("GDB breakpoint address {Address} exceeds memory size {MaxAddress}", 
                    address, A20Gate.EndOfHighMemoryArea);
            }
            return null;
        }

        // Compile condition if present and compiler is available
        Func<long, bool>? condition = null;
        string? conditionExpression = gdbCommand.ConditionExpression;

        if (!string.IsNullOrWhiteSpace(conditionExpression) && _conditionCompiler != null) {
            try {
                condition = _conditionCompiler.Compile(conditionExpression);
                if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                    _loggerService.Information("Compiled conditional breakpoint: {Expression}", conditionExpression);
                }
            } catch (ArgumentException ex) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning(ex, "Failed to compile condition expression: {Expression}", conditionExpression);
                }
                // Continue without condition if compilation fails
                conditionExpression = null;
            }
        }

        return new AddressBreakPoint(
            breakPointType.Value,
            address,
            onBreakPointReached,
            false,
            condition,
            conditionExpression);
    }
}
