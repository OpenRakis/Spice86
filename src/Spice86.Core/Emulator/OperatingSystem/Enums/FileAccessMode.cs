namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Represents file access modes, sharing modes, and inheritance attributes.
/// Based on DOS file open mode byte layout:
/// - Bits 0-2: Access mode (0=read, 1=write, 2=read/write)
/// - Bits 4-6: Sharing mode (0=compat, 1=deny all, 2=deny write, 3=deny read, 4=deny none)
/// - Bit 7: Inheritance flag (0=inherited, 1=private/not inherited)
/// </summary>
public enum FileAccessMode {
    /// <summary>
    /// File can only be read.
    /// </summary>
    ReadOnly = 0x00,

    /// <summary>
    /// File can only be written.
    /// </summary>
    WriteOnly = 0x01,

    /// <summary>
    /// File can be both read and written.
    /// </summary>
    ReadWrite = 0x02,

    /// <summary>
    /// Reserved
    /// </summary>
    Reserved = 0x03,

    /// <summary>
    /// Prohibits nothing to other processes.
    /// </summary>
    DenyNone = 0x40,

    /// <summary>
    /// Prohibits read access by other processes.
    /// </summary>
    DenyRead = 0x30,

    /// <summary>
    /// Prohibits write access by other processes.
    /// </summary>
    DenyWrite = 0x20,

    /// <summary>
    /// File is private to the current process and will not be inherited by child processes.
    /// This is bit 7 of the open mode byte (DOS_NOT_INHERIT in DOSBox).
    /// </summary>
    Private = 0x80,
}