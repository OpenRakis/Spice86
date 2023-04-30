namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.InterruptHandlers.VGA;

public interface IVideoState {
    public DacRegisters DacRegisters { get; }
    public GeneralRegisters GeneralRegisters { get; }
    public SequencerRegisters SequencerRegisters { get; }
    public CrtControllerRegisters CrtControllerRegisters { get; }
    public GraphicsControllerRegisters GraphicsControllerRegisters { get; }
    public AttributeControllerRegisters AttributeControllerRegisters { get; }

    public VgaMode CurrentMode { get; }
}