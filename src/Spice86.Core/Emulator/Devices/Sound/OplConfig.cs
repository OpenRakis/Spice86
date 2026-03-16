namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Configuration options for the OPL synthesizer.
/// </summary>
/// <param name="Mode">OPL synthesis mode to emulate.</param>
/// <param name="SbBase">Sound Blaster base I/O address used for OPL port registration.</param>
/// <param name="SbMixer">Whether Sound Blaster mixer control affects OPL output levels.</param>
public sealed record class OplConfig(OplMode Mode, ushort SbBase, bool SbMixer);
