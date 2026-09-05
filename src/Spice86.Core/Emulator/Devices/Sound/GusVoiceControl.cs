namespace Spice86.Core.Emulator.Devices.Sound;

using System;

/// <summary>
/// Bit flags shared by the GF1 voice wave-control (register 0x00/0x80) and
/// volume-control (register 0x0D/0x8D) bytes.
/// </summary>
[Flags]
public enum GusVoiceControl : byte {
    /// <summary>No flag set: the control is running forward, 8-bit, non-looping.</summary>
    None = 0x00,

    /// <summary>The control is held in reset and produces no output.</summary>
    Reset = 0x01,

    /// <summary>The control has reached its end point and stopped.</summary>
    Stopped = 0x02,

    /// <summary>Samples are 16-bit rather than 8-bit (wave control only).</summary>
    Bit16 = 0x04,

    /// <summary>Playback loops between the start and end addresses.</summary>
    Loop = 0x08,

    /// <summary>Looping reverses direction at each end point instead of wrapping.</summary>
    Bidirectional = 0x10,

    /// <summary>An IRQ is raised when the end point is reached.</summary>
    RaiseIrq = 0x20,

    /// <summary>The position decrements instead of incrementing.</summary>
    Decreasing = 0x40
}
