namespace Spice86.Core.Emulator.Debugger;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// Defines the interface for an Emulator Debugger.
/// </summary>
public interface IEmulatorDebugger
{
    /// <summary>
    /// Visits the main memory of the emulator.
    /// </summary>
    /// <param name="memory">The memory to visit.</param>
    void VisitMainMemory(IMemory memory);

    /// <summary>
    /// Visits the CPU state of the emulator.
    /// </summary>
    /// <param name="state">The state to visit.</param>
    void VisitCpuState(State state);

    /// <summary>
    /// Visits the VGA renderer of the emulator.
    /// </summary>
    /// <param name="vgaRenderer">The VGA renderer to visit.</param>
    void VisitVgaRenderer(IVgaRenderer vgaRenderer);

    /// <summary>
    /// Visits the video state of the emulator.
    /// </summary>
    /// <param name="videoState">The video state to visit.</param>
    void VisitVideoState(IVideoState videoState);

    /// <summary>
    /// Visits the DAC palette of the emulator.
    /// </summary>
    /// <param name="argbPalette">The ARGB palette to visit.</param>
    void VisitDacPalette(ArgbPalette argbPalette);

    /// <summary>
    /// Visits the DAC registers of the emulator.
    /// </summary>
    /// <param name="dacRegisters">The DAC registers to visit.</param>
    void VisitDacRegisters(DacRegisters dacRegisters);

    /// <summary>
    /// Visits the VGA card of the emulator.
    /// </summary>
    /// <param name="vgaCard">The VGA card to visit.</param>
    void VisitVgaCard(VgaCard vgaCard);

    /// <summary>
    /// Visits the CPU of the emulator.
    /// </summary>
    /// <param name="cpu">The CPU to visit.</param>
    void VisitCpu(Cpu cpu);

    /// <summary>
    /// Visits the external MIDI device of the emulator.
    /// </summary>
    /// <param name="midi">The MIDI device to visit.</param>
    void VisitExternalMidiDevice(Midi midi);
}
