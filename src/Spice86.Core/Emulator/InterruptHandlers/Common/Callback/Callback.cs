namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Shared.Emulator.Memory;

using System;

/// <summary>
/// Represents a callback. Used by interrupt handlers.
/// </summary>
public class Callback : ICallback {
    private readonly Action _runnable;
    /// <inheritdoc/>
    public byte Index { get; }
    /// <inheritdoc />
    public SegmentedAddress InstructionAddress { get; }

    /// <summary>
    /// Initializes a new instance of a <see cref="Callback"/>
    /// </summary>
    /// <param name="index">The callback number.</param>
    /// <param name="runnable">The code the callback will run.</param>
    /// <param name="instructionAddress">Physical address of the callback instruction.</param>
    public Callback(byte index, Action runnable, SegmentedAddress instructionAddress) {
        Index = index;
        _runnable = runnable;
        InstructionAddress = instructionAddress;
    }

    /// <inheritdoc/>
    public void Run() {
        _runnable.Invoke();
    }

    /// <inheritdoc/>
    public void RunFromOverriden() {
        Run();
    }
}