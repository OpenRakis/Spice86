namespace Spice86.ViewModels;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// Utility class for shared memory breakpoint logic.
/// Provides common functionality for creating breakpoint condition functions.
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
            byte expectedValue = triggerValueCondition[index];

            if ((type is BreakPointType.MEMORY_READ or BreakPointType.MEMORY_ACCESS && memory.SneakilyRead((uint)address) == expectedValue) ||
                (type is BreakPointType.MEMORY_WRITE or BreakPointType.MEMORY_ACCESS && memory.CurrentlyWritingByte == expectedValue)) {
                return true;
            }

            return false;
        };
    }
}
