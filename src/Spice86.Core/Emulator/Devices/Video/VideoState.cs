namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;

/// <inheritdoc cref="IVideoState" />
public class VideoState : IVideoState {
    /// <summary>
    ///     Creates a new instance of the <see cref="VideoState" /> class.
    /// </summary>
    public VideoState() {
        DacRegisters = new DacRegisters();
        GeneralRegisters = new GeneralRegisters();
        SequencerRegisters = new SequencerRegisters();
        CrtControllerRegisters = new CrtControllerRegisters();
        GraphicsControllerRegisters = new GraphicsControllerRegisters();
        AttributeControllerRegisters = new AttributeControllerRegisters();
    }

    /// <inheritdoc />
    public DacRegisters DacRegisters { get; }

    /// <inheritdoc />
    public GeneralRegisters GeneralRegisters { get; }

    /// <inheritdoc />
    public SequencerRegisters SequencerRegisters { get; }

    /// <inheritdoc />
    public CrtControllerRegisters CrtControllerRegisters { get; }

    /// <inheritdoc />
    public GraphicsControllerRegisters GraphicsControllerRegisters { get; }

    /// <inheritdoc />
    public AttributeControllerRegisters AttributeControllerRegisters { get; }
}