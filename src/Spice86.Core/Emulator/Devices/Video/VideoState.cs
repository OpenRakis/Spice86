namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;

/// <inheritdoc cref="IVideoState" />
public class VideoState : IVideoState {
    /// <summary>
    ///     Creates a new instance of the <see cref="VideoState" /> class.
    /// </summary>
    public VideoState() {
        BeamTiming = new(this);
    }

    /// <summary>Creates video state with native Tandy registers and beam timing.</summary>
    public VideoState(TandyVideoState tandy) {
        Tandy = tandy;
        BeamTiming = new TandyBeamTiming(this, tandy);
    }

    /// <inheritdoc />
    public DacRegisters DacRegisters { get; } = new();

    /// <inheritdoc />
    public GeneralRegisters GeneralRegisters { get; } = new();

    /// <inheritdoc />
    public SequencerRegisters SequencerRegisters { get; } = new();

    /// <inheritdoc />
    public CrtControllerRegisters CrtControllerRegisters { get; } = new();

    /// <inheritdoc />
    public GraphicsControllerRegisters GraphicsControllerRegisters { get; } = new();

    /// <inheritdoc />
    public AttributeControllerRegisters AttributeControllerRegisters { get; } = new();

    /// <inheritdoc />
    public bool IsRenderingDirty { get; set; }

    /// <inheritdoc />
    public VgaBeamTiming BeamTiming { get; }

    /// <summary>Native gate array state, absent on VGA machines.</summary>
    public TandyVideoState? Tandy { get; }
}