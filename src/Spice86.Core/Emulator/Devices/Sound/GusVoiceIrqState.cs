namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Read-only snapshot of the IRQ state shared by all 32 GF1 voices.
/// </summary>
/// <param name="VolStateMask">Volume-ramp IRQ bitmask: bit N is set when voice N requests a volume IRQ.</param>
/// <param name="WaveStateMask">Wave-position IRQ bitmask: bit N is set when voice N requests a wave IRQ.</param>
/// <param name="NextVoiceToReport">Index of the next voice whose IRQ is reported via the voice IRQ status register.</param>
public sealed record GusVoiceIrqState(
    uint VolStateMask,
    uint WaveStateMask,
    byte NextVoiceToReport);
