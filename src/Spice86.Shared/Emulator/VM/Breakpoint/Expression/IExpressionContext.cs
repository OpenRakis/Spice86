namespace Spice86.Shared.Emulator.VM.Breakpoint.Expression;

/// <summary>
/// Context for evaluating expressions, providing access to variables and memory.
/// </summary>
public interface IExpressionContext {
    /// <summary>
    /// Gets the value of a variable by name.
    /// </summary>
    /// <param name="variableName">The name of the variable.</param>
    /// <returns>The value of the variable.</returns>
    long GetVariable(string variableName);
    
    /// <summary>
    /// Reads a byte from memory at the specified address.
    /// </summary>
    /// <param name="address">The memory address to read from.</param>
    /// <returns>The byte value at the address.</returns>
    byte ReadMemoryByte(long address);
    
    /// <summary>
    /// Reads a word (16-bit) from memory at the specified address.
    /// </summary>
    /// <param name="address">The memory address to read from.</param>
    /// <returns>The word value at the address.</returns>
    ushort ReadMemoryWord(long address);
    
    /// <summary>
    /// Reads a dword (32-bit) from memory at the specified address.
    /// </summary>
    /// <param name="address">The memory address to read from.</param>
    /// <returns>The dword value at the address.</returns>
    uint ReadMemoryDword(long address);
}
