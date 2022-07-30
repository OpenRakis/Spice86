namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Devices;

using System;

/// <summary>
/// Defines a device which supports 16-bit DMA transfers.
/// </summary>
public interface IDmaDevice16 {
    /// <summary>
    /// Gets the DMA channel of the device.
    /// </summary>
    int Channel { get; }

    /// <summary>
    /// Writes 16-bit words of data to the DMA device.
    /// </summary>
    /// <param name="source">Address of first 16-bit word to write to the device.</param>
    /// <param name="count">Number of 16-bit words to write.</param>
    /// <returns>Number of 16-bit words actually written to the device.</returns>
    int WriteWords(IntPtr source, int count);
    /// <summary>
    /// Invoked when a transfer is completed in single-cycle mode.
    /// </summary>
    void SingleCycleComplete();
}
