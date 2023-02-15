namespace Spice86.Core.Emulator.Callback;

using System;

public class Callback : ICallback {
    private readonly Action _runnable;

    public Callback(byte index, Action runnable) {
        Index = index;
        _runnable = runnable;
    }

    public byte Index { get; private set; }

    /// <inheritdoc />
    public ushort? InterruptHandlerSegment => null;

    public void Run() {
        _runnable.Invoke();
    }

    public void RunFromOverriden() {
        Run();
    }
}