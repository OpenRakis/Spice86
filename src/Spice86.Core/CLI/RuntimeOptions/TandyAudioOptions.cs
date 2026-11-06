namespace Spice86.Core.CLI.RuntimeOptions;

using Spice86.Core.Emulator.Devices.Sound.Tandy;
using Spice86.Core.Emulator.Platform;

/// <summary>Resolved machine, device, fade-out and filter preferences for Tandy audio.</summary>
public sealed record TandyAudioOptions(
    MachineType MachineType,
    TandySoundMode Mode,
    string Fadeout,
    string PsgFilter,
    string DacFilter);