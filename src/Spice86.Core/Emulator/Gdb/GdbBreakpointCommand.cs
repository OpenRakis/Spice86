namespace Spice86.Core.Emulator.Gdb;

using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// Represents a parsed GDB breakpoint command from the remote protocol.
/// </summary>
/// <param name="Type">The numeric type code from GDB (0=SW, 1=HW, 2=Write, 3=Read, 4=Access).</param>
/// <param name="BreakPointType">The mapped breakpoint type for the emulator.</param>
/// <param name="Address">The memory address where the breakpoint should be set.</param>
/// <param name="Kind">The kind parameter from GDB (typically indicates size).</param>
/// <param name="ConditionExpression">Optional condition expression string (from ;X:expr or ;cond:expr).</param>
public record GdbBreakpointCommand(
    int Type,
    BreakPointType BreakPointType,
    long Address,
    int Kind,
    string? ConditionExpression);
