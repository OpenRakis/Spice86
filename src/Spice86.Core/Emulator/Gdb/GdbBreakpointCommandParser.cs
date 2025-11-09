namespace Spice86.Core.Emulator.Gdb;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Serilog.Events;

/// <summary>
/// Parses GDB remote protocol breakpoint commands (Z packets) into structured data.
/// </summary>
public class GdbBreakpointCommandParser {
    private readonly ILoggerService _loggerService;

    public GdbBreakpointCommandParser(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    /// <summary>
    /// Parses a GDB breakpoint command string into a structured GdbBreakpointCommand.
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
            long address = ConvertUtils.ParseHex32(parts[1]);
            int kind = int.Parse(parts[2]);

            // Map GDB type to emulator breakpoint type
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
                    _loggerService.Error("Unsupported GDB breakpoint type {Type} in command {Command}", type, command);
                }
                return null;
            }

            // Validate address
            if (address > A20Gate.EndOfHighMemoryArea) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("GDB breakpoint address {Address} exceeds memory size {MaxAddress}", 
                        address, A20Gate.EndOfHighMemoryArea);
                }
                return null;
            }

            return new GdbBreakpointCommand(type, breakPointType.Value, address, kind, conditionExpression);
        } catch (Exception ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(ex, "Failed to parse GDB breakpoint command: {Command}", command);
            }
            return null;
        }
    }
}
