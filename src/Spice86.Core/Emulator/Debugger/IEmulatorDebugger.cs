namespace Spice86.Core.Emulator.Debugger;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// A class that visits several classes that live inside the Emulator
/// </summary>
public interface IEmulatorDebugger {
    void VisitMainMemory(IMemory memory);

    void VisitCpuState(State state);

    void VisitVgaRenderer(IVgaRenderer vgaRenderer);

    void VisitVideoState(IVideoState videoState);

    void VisitDacPalette(ArgbPalette argbPalette);
    
    void VisitDacRegisters(DacRegisters dacRegisters);
    
    void VisitVgaCard(VgaCard vgaCard);
    
    void VisitCpu(Cpu cpu);
    void VisitExternalMidiDevice(Midi midi);
}