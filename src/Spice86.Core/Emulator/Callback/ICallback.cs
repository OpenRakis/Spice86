namespace Spice86.Core.Emulator.Callback;
public interface ICallback : IRunnable {
    public byte Index { get; }
    /// <summary>
    /// Optional segment where the interrupt handler is installed, if null the default interrupt location is used.
    /// This is useful for device drivers which are expected to have their Device Driver Header at the start of
    /// the segment where the interrupt handler is installed.
    /// </summary>
    ushort? InterruptHandlerSegment { get; }
    public void RunFromOverriden();
}