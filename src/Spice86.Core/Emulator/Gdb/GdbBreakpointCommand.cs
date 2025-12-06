namespace Spice86.Core.Emulator.Gdb;

/// <summary>
/// Represents a parsed GDB breakpoint command from the remote protocol.
/// This is a simple data structure containing the raw parsed values without interpretation.
/// </summary>
/// <param name="Type">The numeric type code from GDB (0=SW, 1=HW, 2=Write, 3=Read, 4=Access).</param>
/// <param name="Address">The memory address string from the command (unparsed hex).</param>
/// <param name="Kind">The kind parameter from GDB (typically indicates size).</param>
/// <param name="ConditionExpression">Optional condition expression string (from ;X:expr or ;cond:expr).</param>
public record GdbBreakpointCommand(
    int Type,
    string Address,
    int Kind,
    string? ConditionExpression);