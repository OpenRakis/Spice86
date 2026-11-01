namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Effect an interrupt service has on emulated memory.
/// </summary>
public enum ServiceMemoryEffect {
    /// <summary>The service does not touch emulated memory.</summary>
    None,
    /// <summary>The service reads emulated memory and writes none.</summary>
    Read,
    /// <summary>The service writes emulated memory and reads none.</summary>
    Write,
    /// <summary>The service reads and writes emulated memory.</summary>
    ReadWrite
}
