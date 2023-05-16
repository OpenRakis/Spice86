namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;
using System;

/// <summary>
/// Placeholder OPL channel that generates no output.
/// </summary>
internal sealed class NullChannel : Channel
{
    /// <summary>
    /// Initializes a new instance of the NullChannel class.
    /// </summary>
    /// <param name="opl">FmSynthesizer instance which owns the channel.</param>
    public NullChannel(FmSynthesizer opl)
        : base(0, opl)
    {
    }

    /// <summary>
    /// Returns an array containing the channel's output values.
    /// </summary>
    /// <returns>Array containing the channel's output values.</returns>
    public override void GetChannelOutput(Span<double> output) => output.Clear();
    
    /// <summary>
    /// Activates channel output.
    /// </summary>
    public override void KeyOn()
    {
    }
    
    /// <summary>
    /// Disables channel output.
    /// </summary>
    public override void KeyOff()
    {
    }
    
    /// <summary>
    /// Updates the state of all of the operators in the channel.
    /// </summary>
    public override void UpdateOperators()
    {
    }
}
