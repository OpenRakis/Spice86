namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// Represents a memory action.
/// </summary>
internal enum MemoryAction {
    /// <summary>
    /// Reads a byte from memory.
    /// </summary>
    ReadByte,
    /// <summary>
    /// Writes a byte to memory.
    /// </summary>
    WriteByte,
    /// <summary>
    /// Sets a block of memory to a value.
    /// </summary>
    MemSet,
    /// <summary>
    /// Moves a block of memory.
    /// </summary>
    MemMove
}