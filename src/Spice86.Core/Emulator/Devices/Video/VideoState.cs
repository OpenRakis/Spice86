namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.InternalDebugger;

/// <inheritdoc cref="IVideoState" />
public class VideoState : IVideoState {
    /// <summary>
    ///     Creates a new instance of the <see cref="VideoState" /> class.
    /// </summary>
    public VideoState(DacRegisters dacRegisters, GeneralRegisters generalRegisters, SequencerRegisters sequencerRegisters, CrtControllerRegisters crtControllerRegisters, GraphicsControllerRegisters graphicsControllerRegisters, AttributeControllerRegisters attributeControllerRegisters) {
        DacRegisters = dacRegisters;
        GeneralRegisters = generalRegisters;
        SequencerRegisters = sequencerRegisters;
        CrtControllerRegisters = crtControllerRegisters;
        GraphicsControllerRegisters = graphicsControllerRegisters;
        AttributeControllerRegisters = attributeControllerRegisters;
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

    /// <inheritdoc/>
    public void Accept<T>(T emulatorDebugger) where T : IInternalDebugger {
        emulatorDebugger.Visit(this);
        DacRegisters.Accept(emulatorDebugger);
    }
}