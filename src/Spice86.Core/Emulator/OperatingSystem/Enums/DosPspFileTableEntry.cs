namespace Spice86.Core.Emulator.OperatingSystem.Enums;

using System;

/// <summary>
/// Flags and sentinel values for PSP Job File Table (JFT) entries.
/// </summary>
/// <remarks>
/// Mirrors DOS/FreeDOS semantics:
/// - 0xFF means the JFT slot is unused
/// - Bit 7 (0x80) set means the handle must not be inherited by a child process
/// The remaining low 7 bits contain the DOS handle index.
/// </remarks>
[Flags]
public enum DosPspFileTableEntry : byte {
    /// <summary>
    /// The JFT entry is unused.
    /// </summary>
    Unused = 0xFF,
    /// <summary>
    /// Entry is marked as non-inheritable. When set, child PSP must not inherit this handle.
    /// </summary>
    DoNotInherit = 0x80,
}
