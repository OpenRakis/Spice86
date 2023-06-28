namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using System;

/// <summary>
/// Represents a callback. Used by interrupt handlers.
/// </summary>
public class Callback : ICallback {
    private readonly Action _runnable;
    /// <inheritdoc/>
    public byte Index { get; }
    /// <inheritdoc />
    public uint InstructionPhysicalAddress { get; }

    /// <summary>
    /// Initializes a new instance of a <see cref="Callback"/>
    /// </summary>
    /// <param name="index">The callback number.</param>
    /// <param name="runnable">The code the callback will run.</param>
    /// <param name="instructionPhysicalAddress">Physical address of the callback instruction.</param>
    public Callback(byte index, Action runnable, uint instructionPhysicalAddress) {
        Index = index;
        _runnable = runnable;
        InstructionPhysicalAddress = instructionPhysicalAddress;
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