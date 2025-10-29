namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// VBE window attribute flags that describe the capabilities of a memory window.
/// </summary>
[Flags]
public enum VbeWindowAttribute : byte {
    /// <summary>
    /// Window exists.
    /// </summary>
    WindowExists = 0x01,

    /// <summary>
    /// Window is readable.
    /// </summary>
    WindowReadable = 0x02,

    /// <summary>
    /// Window is writable.
    /// </summary>
    WindowWritable = 0x04
}
