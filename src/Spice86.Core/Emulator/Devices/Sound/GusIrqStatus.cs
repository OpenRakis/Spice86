namespace Spice86.Core.Emulator.Devices.Sound;

using System;

/// <summary>
/// Bit flags of the GF1 IRQ status byte read from port 0x246.
/// </summary>
[Flags]
public enum GusIrqStatus : byte {
    /// <summary>No IRQ pending.</summary>
    None = 0x00,

    /// <summary>Hardware timer 1 expired.</summary>
    Timer1 = 0x04,

    /// <summary>Hardware timer 2 expired.</summary>
    Timer2 = 0x08,

    /// <summary>At least one voice reached its wave end point.</summary>
    WaveTable = 0x20,

    /// <summary>At least one voice completed its volume ramp.</summary>
    VolumeRamp = 0x40,

    /// <summary>The DMA channel reached terminal count.</summary>
    DmaTerminalCount = 0x80
}
