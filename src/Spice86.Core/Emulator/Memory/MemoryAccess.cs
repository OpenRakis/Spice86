namespace Spice86.Core.Emulator.Memory;

using System;

[Flags]
public enum MemoryAccess {
    /// <summary>
    /// No memory access specified.
    /// </summary>
    None = 0,

    /// <summary>
    /// Read memory access.
    /// </summary>
    Read = 1 << 0,

    /// <summary>
    /// Write memory access.
    /// </summary>
    Write = 1 << 1,

    /// <summary>
    /// Read and write memory access.
    /// </summary>
    ReadWrite = Read | Write,
}
