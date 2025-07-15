namespace Spice86.Core.Emulator.OperatingSystem.Enums;
using System;
using System.Diagnostics;

/// <summary>
/// Represents file access modes, sharing modes, and inheritance attributes.
/// </summary>
[Flags]
public enum FileAccessMode {
    /// <summary>
    /// File can only be read.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// File can only be written.
    /// </summary>
    WriteOnly,

    /// <summary>
    /// File can be both read and written.
    /// </summary>
    ReadWrite,

    /// <summary>
    /// Reserved
    /// </summary>
    Reserved,

    /// <summary>
    /// Prohibits nothing to other processes.
    /// </summary>
    DenyNone,

    /// <summary>
    /// Prohibits read access by other processes.
    /// </summary>
    DenyRead,

    /// <summary>
    /// Prohibits write access by other processes.
    /// </summary>
    DenyWrite,

    /// <summary>
    /// File is private to the current process and will not be inherited by child processes.
    /// </summary>
    Private,
}