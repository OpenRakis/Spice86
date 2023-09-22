namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Video.Registers;

/// <summary>
///     Represents the state of the video card.
/// </summary>
public interface IVideoState : IDebuggableComponent {
    /// <summary>
    ///     Contains the DAC registers.
    /// </summary>
    public DacRegisters DacRegisters { get; }

    /// <summary>
    ///     Contains the general registers.
    /// </summary>
    public GeneralRegisters GeneralRegisters { get; }

    /// <summary>
    ///     Contains the sequencer registers.
    /// </summary>
    public SequencerRegisters SequencerRegisters { get; }

    /// <summary>
    ///     Contains the CRT controller registers.
    /// </summary>
    public CrtControllerRegisters CrtControllerRegisters { get; }

    /// <summary>
    ///     Contains the graphics controller registers.
    /// </summary>
    public GraphicsControllerRegisters GraphicsControllerRegisters { get; }

    /// <summary>
    ///     Contains the attribute controller registers.
    /// </summary>
    public AttributeControllerRegisters AttributeControllerRegisters { get; }
}