namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Shared IRQ state for all GUS voices.
/// Wave and volume IRQs accumulate here as bitmasks, one bit per voice.
/// Fields (not auto-properties) so voice code can pass them by reference.
/// </summary>
/// <remarks>
/// 2022-2025 The DOSBox Staging Team
/// </remarks>
internal sealed class GusVoiceIrq {
    /// <summary>Volume-ramp IRQ bitmask: bit N is set when voice N requests a volume IRQ.</summary>
    public uint VolState;

    /// <summary>Wave-position IRQ bitmask: bit N is set when voice N requests a wave IRQ.</summary>
    public uint WaveState;

    /// <summary>Index of the next voice whose IRQ should be reported via the IRQ-status register.</summary>
    public byte Status;
}
