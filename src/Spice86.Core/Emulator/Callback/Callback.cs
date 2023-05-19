namespace Spice86.Core.Emulator.Callback;

using System;

/// <summary>
/// Represents a callback. Used by interrupt handlers.
/// </summary>
public class Callback : ICallback {
    private readonly Action _runnable;

    /// <summary>
    /// Initializes a new instance of a <see cref="Callback"/>
    /// </summary>
    /// <param name="index">The callback number.</param>
    /// <param name="runnable">The code the callback will run.</param>
    public Callback(byte index, Action runnable) {
        Index = index;
        _runnable = runnable;
    }

    /// <inheritdoc/>
    public byte Index { get; private set; }

    /// <inheritdoc />
    public ushort? InterruptHandlerSegment => null;

    /// <inheritdoc/>
    public void Run() {
        _runnable.Invoke();
    }

    /// <inheritdoc/>
    public void RunFromOverriden() {
        Run();
    }
}