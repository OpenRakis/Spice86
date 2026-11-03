namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Platform;

/// <inheritdoc cref="IVideoState" />
public class VideoState : IVideoState {
    /// <summary>
    ///     Creates a new instance of the <see cref="VideoState" /> class.
    /// </summary>
    public VideoState() : this(MachineProfile.For(MachineType.Pc)) {
    }

    /// <summary>Creates video state for the selected machine.</summary>
    public VideoState(MachineProfile profile) {
        if (profile.IsTandy) {
            Tandy = new TandyVideoState();
        }
        DacRegisters = new DacRegisters();
        GeneralRegisters = new GeneralRegisters();
        SequencerRegisters = new SequencerRegisters();
        CrtControllerRegisters = new CrtControllerRegisters();
        GraphicsControllerRegisters = new GraphicsControllerRegisters();
        AttributeControllerRegisters = new AttributeControllerRegisters();
        BeamTiming = new(this);
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

    /// <inheritdoc />
    public bool IsRenderingDirty { get; set; }

    /// <inheritdoc />
    public VgaBeamTiming BeamTiming { get; }

    /// <summary>Native gate array state, absent on VGA machines.</summary>
    public TandyVideoState? Tandy { get; }
}