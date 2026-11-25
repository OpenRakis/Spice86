namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Records every I/O port write of a run, with its port, value and width, in write order. It is
/// installed on the machine's <see cref="IOPortDispatcher"/> before the program starts, so
/// program and emulator writes are recorded alike; the run restores the null observer when it
/// ends.
/// </summary>
internal sealed class PortWriteRecorder : IIoPortAccessObserver, IDisposable {
    private readonly WeakReference<Machine> _machine;

    public PortWriteRecorder(Machine machine) {
        _machine = new WeakReference<Machine>(machine);
        machine.IoPortDispatcher.AccessObserver = this;
    }

    /// <summary>The port, value and width of every write, in write order.</summary>
    public List<(ushort Port, uint Value, int Width)> Writes { get; } = [];

    /// <inheritdoc />
    public void OnWrite(ushort port, uint value, int width) {
        Writes.Add((port, value, width));
    }

    public void Dispose() {
        if (_machine.TryGetTarget(out Machine? machine)) {
            machine.IoPortDispatcher.AccessObserver = new NullIoPortAccessObserver();
        }
    }
}
