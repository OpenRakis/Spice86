namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Aeon.Emulator.Video;
using Spice86.Aeon.Emulator.Video.Registers;
using Spice86.Core.Emulator.InterruptHandlers.VGA;

public class VideoState : IVideoState {
    public VideoState() {
        DacRegisters = new DacRegisters();
        GeneralRegisters = new GeneralRegisters();
        SequencerRegisters = new SequencerRegisters();
        CrtControllerRegisters = new CrtControllerRegisters();
        GraphicsControllerRegisters = new GraphicsControllerRegisters();
        AttributeControllerRegisters = new AttributeControllerRegisters();
    }

    // Registers
    public DacRegisters DacRegisters { get; }
    public GeneralRegisters GeneralRegisters { get; }
    public SequencerRegisters SequencerRegisters { get; }
    public CrtControllerRegisters CrtControllerRegisters { get; }
    public GraphicsControllerRegisters GraphicsControllerRegisters { get; }
    public AttributeControllerRegisters AttributeControllerRegisters { get; }
    
    // Other state
    public VgaMode VideoMode { get; } = default;
}