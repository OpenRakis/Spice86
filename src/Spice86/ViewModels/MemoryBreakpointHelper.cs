namespace Spice86.ViewModels;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

/// <summary>
/// Utility class for shared memory breakpoint logic.
/// Provides common functionality for creating breakpoint condition functions and creating memory breakpoints.
/// </summary>
public static class MemoryBreakpointHelper {
    /// <summary>
    /// Creates a condition function that checks if memory values match the specified trigger condition.
    /// </summary>
    /// <param name="triggerValueCondition">The hex bytes to match against.</param>
    /// <param name="startAddress">The starting address of the breakpoint range.</param>
    /// <param name="type">The type of memory breakpoint (READ, WRITE, or ACCESS).</param>
    /// <param name="memory">Memory interface for reading memory values.</param>
    /// <returns>A function that returns true if the memory value matches the condition, or null if no condition specified.</returns>
    public static Func<long, bool>? CreateCheckForBreakpointMemoryValue(
        byte[]? triggerValueCondition,
        long startAddress,
        BreakPointType type,
        IMemory memory) {
        
        if (triggerValueCondition is null || triggerValueCondition.Length == 0) {
            return null;
        }

        return (long address) => {
            long index = address - startAddress;
            
            // Bounds checking to prevent IndexOutOfRangeException
            if (index < 0 || index >= triggerValueCondition.Length) {
                return false;
            }
            
            byte expectedValue = triggerValueCondition[index];

            // Add explicit parentheses to clarify operator precedence
            if (((type is BreakPointType.MEMORY_READ or BreakPointType.MEMORY_ACCESS) && memory.SneakilyRead((uint)address) == expectedValue) ||
                ((type is BreakPointType.MEMORY_WRITE or BreakPointType.MEMORY_ACCESS) && memory.CurrentlyWritingByte == expectedValue)) {
                return true;
            }

            return false;
        };
    }

    /// <summary>
    /// Creates a memory breakpoint with the specified parameters.
    /// This method consolidates the common logic for creating memory breakpoints from both BreakpointsViewModel and MemoryViewModel.
    /// </summary>
    /// <param name="startAddressString">The start address as a string.</param>
    /// <param name="endAddressString">The end address as a string (optional).</param>
    /// <param name="valueConditionString">The hex value condition as a string (optional).</param>
    /// <param name="breakpointType">The type of memory breakpoint.</param>
    /// <param name="state">CPU state for address parsing.</param>
    /// <param name="memory">Memory interface for value condition checking.</param>
    /// <param name="createBreakpointAction">Action to create the actual breakpoint with start, end addresses, type, and condition.</param>
    /// <returns>True if the breakpoint was created successfully, false otherwise.</returns>
    public static bool TryCreateMemoryBreakpoint(
        string? startAddressString,
        string? endAddressString,
        string? valueConditionString,
        BreakPointType breakpointType,
        State state,
        IMemory memory,
        Action<uint, uint, BreakPointType, Func<long, bool>?> createBreakpointAction) {
        
        if (!AddressAndValueParser.TryParseAddressString(startAddressString, state, out uint? startAddress) ||
            !startAddress.HasValue) {
            return false;
        }

        byte[]? triggerValueCondition = AddressAndValueParser.ParseHexAsArray(valueConditionString);
        Func<long, bool>? condition = CreateCheckForBreakpointMemoryValue(
            triggerValueCondition,
            startAddress.Value,
            breakpointType,
            memory);

        if (AddressAndValueParser.TryParseAddressString(endAddressString, state, out uint? endAddress) &&
            endAddress.HasValue) {
            createBreakpointAction(startAddress.Value, endAddress.Value, breakpointType, condition);
        } else {
            createBreakpointAction(startAddress.Value, startAddress.Value, breakpointType, condition);
        }

        return true;
    }
}
