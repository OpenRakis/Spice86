namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// Represents the memory model.
/// </summary>
public enum MemoryModel {
    /// <summary>
    /// Text memory layout.
    /// </summary>
    Text,
    /// <summary>
    /// CGA memory layout.
    /// </summary>
    Cga,
    /// <summary>
    /// Planar memory layout.
    /// </summary>
    Planar,
    /// <summary>
    /// Packed memory layout.
    /// </summary>
    Packed,
}