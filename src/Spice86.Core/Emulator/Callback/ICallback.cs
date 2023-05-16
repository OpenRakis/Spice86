namespace Spice86.Core.Emulator.Callback;

using Spice86.Core.Emulator.ReverseEngineer;

/// <summary>
/// The interface for classes that can be called back. Either by machine code, or by machine code overrides.
/// </summary>
public interface ICallback : IRunnable {
    /// <summary>
    /// Defines the callback number. For example, 0x2F for DOS Interrupt Handler 2F.
    /// </summary>
    public byte Index { get; }

    /// <summary>
    /// Optional segment where the interrupt handler is installed, if null the default interrupt location is used.
    /// This is useful for device drivers which are expected to have their Device Driver Header at the start of
    /// the segment where the interrupt handler is installed.
    /// </summary>
    ushort? InterruptHandlerSegment { get; }

    /// <summary>
    /// Invoked when interruptions are invoked by machine code overrides. See <see cref="CSharpOverrideHelper"/>
    /// </summary>
    public void RunFromOverriden();
}