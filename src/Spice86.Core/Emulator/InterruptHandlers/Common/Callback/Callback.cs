namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Shared.Emulator.Memory;

using System;

/// <summary>
/// Represents a callback. Used by interrupt handlers.
/// </summary>
public class Callback : ICallback {
    /// <inheritdoc/>
    public ushort Index { get; }
    /// <inheritdoc />
    public SegmentedAddress InstructionAddress { get; }

    /// <summary>
    /// Initializes a new instance of a <see cref="Callback"/>
    /// </summary>
    /// <param name="index">The callback number.</param>
    /// <param name="runnable">The code the callback will run.</param>
    /// <param name="instructionAddress">Physical address of the callback instruction.</param>
    public Callback(ushort index, Action runnable, SegmentedAddress instructionAddress) {
        Index = index;
        Runnable = runnable;
        InstructionAddress = instructionAddress;
    }

    /// <summary>The code the callback runs.</summary>
    public Action Runnable { get; }

    /// <inheritdoc/>
    public void Run() {
        Runnable.Invoke();
    }

    /// <inheritdoc/>
    public void RunFromOverriden() {
        Run();
    }
}