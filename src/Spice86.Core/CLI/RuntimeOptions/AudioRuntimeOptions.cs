namespace Spice86.Core.CLI.RuntimeOptions;

using Spice86.Audio.Filters;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// Runtime audio options projected from command-line configuration.
/// </summary>
/// <param name="AudioEngine">Audio engine backend used by the software mixer.</param>
/// <param name="Mt32RomsPath">Optional path to MT-32 ROM assets.</param>
/// <param name="OplMode">OPL synthesis mode to emulate.</param>
/// <param name="SbBase">Sound Blaster base I/O address.</param>
/// <param name="SbMixer">Whether Sound Blaster mixer control affects OPL output levels.</param>
/// <param name="SbIrq">IRQ line used by the emulated Sound Blaster card.</param>
/// <param name="SbDma">8-bit DMA channel used by the emulated Sound Blaster card.</param>
/// <param name="SbHdma">16-bit DMA channel used by the emulated Sound Blaster card.</param>
/// <param name="SbType">Sound Blaster model to emulate.</param>
/// <param name="GusBase">Gravis UltraSound I/O base address.</param>
/// <param name="GusIrq">Gravis UltraSound IRQ number.</param>
/// <param name="GusDma">Gravis UltraSound DMA channel.</param>
/// <param name="GusEnable">Whether Gravis UltraSound emulation is enabled.</param>
/// <param name="GusUltradir">Path exported to DOS as the ULTRADIR environment variable.</param>
/// <param name="GusFilter">Whether the Gravis UltraSound output low-pass filter is enabled.</param>
public sealed record class AudioRuntimeOptions(
    AudioEngine AudioEngine,
    string? Mt32RomsPath,
    OplMode OplMode,
    ushort SbBase,
    bool SbMixer,
    byte SbIrq,
    byte SbDma,
    byte SbHdma,
    SbType SbType,
    ushort GusBase,
    byte GusIrq,
    byte GusDma,
    bool GusEnable,
    string GusUltradir,
    bool GusFilter) {
    /// <summary>
    /// Sound Blaster base I/O address.
    /// </summary>
    public ushort BaseAddress => SbBase;

    /// <summary>
    /// Sound Blaster mixer control.
    /// </summary>
    public bool Mixer => SbMixer;

    /// <summary>
    /// Sound Blaster IRQ.
    /// </summary>
    public byte Irq => SbIrq;

    /// <summary>
    /// Sound Blaster 8-bit DMA.
    /// </summary>
    public byte LowDma => SbDma;

    /// <summary>
    /// Sound Blaster 16-bit DMA.
    /// </summary>
    public byte HighDma => SbHdma;

    /// <summary>
    /// OPL mode.
    /// </summary>
    public OplMode Mode => OplMode;
}