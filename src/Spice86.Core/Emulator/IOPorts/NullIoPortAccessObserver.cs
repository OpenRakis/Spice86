namespace Spice86.Core.Emulator.IOPorts;

/// <summary><see cref="IIoPortAccessObserver"/> that records nothing, the dispatcher default.</summary>
public sealed class NullIoPortAccessObserver : IIoPortAccessObserver {
    /// <inheritdoc />
    public void OnWrite(ushort port, uint value, int width) {
        //NOP
    }
}
