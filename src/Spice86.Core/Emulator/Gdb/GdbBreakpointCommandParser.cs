namespace Spice86.Core.Emulator.Gdb;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using System.Globalization;

/// <summary>
/// Parses GDB remote protocol breakpoint commands (Z packets) into structured data
/// and creates Spice86 breakpoints from them.
/// </summary>
public class GdbBreakpointCommandParser {
    private const char ConditionSeparator = ';';
    private const char ConditionTypeSeparator = ':';
    private const char FieldSeparator = ',';
    private const string GdbConditionTypeX = "X";
    private const string GdbConditionTypeCond = "cond";
    private const int MinimumCommandParts = 3;

    private readonly ILoggerService _loggerService;
    private readonly BreakpointConditionCompiler? _conditionCompiler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GdbBreakpointCommandParser"/> class.
    /// </summary>
    /// <param name="loggerService">The logger service for diagnostic output.</param>
    /// <param name="conditionCompiler">Optional compiler for breakpoint condition expressions.</param>
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
        (string baseCommand, string? conditionExpression) = SeparateConditionFromCommand(command);
        return ParseBaseCommand(baseCommand, conditionExpression, command);
    }

    /// <summary>
    /// Converts a parsed GDB breakpoint command into a Spice86 BreakPoint object.
    /// </summary>
    /// <param name="gdbCommand">The parsed GDB command.</param>
    /// <param name="onBreakPointReached">Callback when breakpoint is reached.</param>
    /// <returns>A BreakPoint object, or null if conversion fails.</returns>
    public BreakPoint? CreateBreakPoint(GdbBreakpointCommand gdbCommand, Action<BreakPoint> onBreakPointReached) {
        BreakPointType? breakPointType = MapGdbTypeToBreakPointType(gdbCommand.Type);
        if (breakPointType is null) {
            return null;
        }

        long? address = ParseAndValidateAddress(gdbCommand.Address);
        if (address is null) {
            return null;
        }

        (Func<long, bool>? condition, string? conditionExpression) = CompileConditionIfPresent(gdbCommand.ConditionExpression);

        return new AddressBreakPoint(
            breakPointType.Value,
            address.Value,
            onBreakPointReached,
            isRemovedOnTrigger: false,
            condition,
            conditionExpression);
    }

    private (string baseCommand, string? conditionExpression) SeparateConditionFromCommand(string command) {
        int semicolonIndex = command.IndexOf(ConditionSeparator);
        if (semicolonIndex < 0) {
            return (command, null);
        }

        string baseCommand = command[..semicolonIndex];
        string conditionPart = command[(semicolonIndex + 1)..];
        string? conditionExpression = ExtractConditionExpression(conditionPart);

        return (baseCommand, conditionExpression);
    }

    private string? ExtractConditionExpression(string conditionPart) {
        int colonIndex = conditionPart.IndexOf(ConditionTypeSeparator);
        if (colonIndex < 0) {
            return null;
        }

        string conditionType = conditionPart[..colonIndex];
        if (!IsValidConditionType(conditionType)) {
            return null;
        }

        string expression = conditionPart[(colonIndex + 1)..];
        LogDebugConditionParsed(expression);
        return expression;
    }

    private static bool IsValidConditionType(string conditionType) {
        return conditionType.Equals(GdbConditionTypeX, StringComparison.Ordinal) ||
               conditionType.Equals(GdbConditionTypeCond, StringComparison.Ordinal);
    }

    private GdbBreakpointCommand? ParseBaseCommand(string baseCommand, string? conditionExpression, string originalCommand) {
        string[] parts = baseCommand.Split(FieldSeparator);
        if (parts.Length < MinimumCommandParts) {
            LogErrorInvalidCommandFormat(originalCommand);
            return null;
        }

        int? type = ParseIntField(parts[0], "type");
        if (type is null) {
            return null;
        }

        string address = parts[1];

        int? kind = ParseIntField(parts[2], "kind");
        if (kind is null) {
            return null;
        }

        return new GdbBreakpointCommand(type.Value, address, kind.Value, conditionExpression);
    }

    private int? ParseIntField(string value, string fieldName) {
        if (int.TryParse(value, out int result)) {
            return result;
        }

        LogErrorInvalidField(fieldName, value);
        return null;
    }

    private BreakPointType? MapGdbTypeToBreakPointType(int gdbType) {
        BreakPointType? breakPointType = gdbType switch {
            0 or 1 => BreakPointType.CPU_EXECUTION_ADDRESS,
            2 => BreakPointType.MEMORY_WRITE,
            3 => BreakPointType.MEMORY_READ,
            4 => BreakPointType.MEMORY_ACCESS,
            _ => null
        };

        if (breakPointType is null) {
            LogErrorUnsupportedBreakpointType(gdbType);
        }

        return breakPointType;
    }

    private long? ParseAndValidateAddress(string addressString) {
        if (!uint.TryParse(addressString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsedAddress)) {
            LogErrorInvalidAddressFormat(addressString);
            return null;
        }

        long address = parsedAddress;
        if (address > A20Gate.EndOfHighMemoryArea) {
            LogWarningAddressExceedsMemory(address);
            return null;
        }

        return address;
    }

    private (Func<long, bool>? condition, string? expression) CompileConditionIfPresent(string? conditionExpression) {
        if (string.IsNullOrWhiteSpace(conditionExpression) || _conditionCompiler is null) {
            return (null, conditionExpression);
        }

        try {
            Func<long, bool> condition = _conditionCompiler.Compile(conditionExpression);
            LogInfoCompiledCondition(conditionExpression);
            return (condition, conditionExpression);
        } catch (ExpressionParseException ex) {
            LogWarningConditionCompilationFailed(ex, conditionExpression);
            return (null, null);
        }
    }

    private void LogDebugConditionParsed(string expression) {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Parsed GDB condition expression: {Condition}", expression);
        }
    }

    private void LogErrorInvalidCommandFormat(string command) {
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Invalid GDB breakpoint command format: {Command}", command);
        }
    }

    private void LogErrorInvalidField(string fieldName, string value) {
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Invalid {FieldName} value in GDB command: {Value}", fieldName, value);
        }
    }

    private void LogErrorUnsupportedBreakpointType(int gdbType) {
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Unsupported GDB breakpoint type {Type}", gdbType);
        }
    }

    private void LogErrorInvalidAddressFormat(string address) {
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("Invalid address format: {Address}", address);
        }
    }

    private void LogWarningAddressExceedsMemory(long address) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("GDB breakpoint address {Address} exceeds memory size {MaxAddress}",
                address, A20Gate.EndOfHighMemoryArea);
        }
    }

    private void LogInfoCompiledCondition(string expression) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Compiled conditional breakpoint: {Expression}", expression);
        }
    }

    private void LogWarningConditionCompilationFailed(ExpressionParseException ex, string expression) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning(ex, "Failed to compile condition expression at position {Position}: {Expression}",
                ex.Position, expression);
        }
    }
}