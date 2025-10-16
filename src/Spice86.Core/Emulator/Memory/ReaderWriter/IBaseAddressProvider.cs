﻿namespace Spice86.Core.Emulator.Memory.ReaderWriter;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Interface for classes that provide a BaseAddress uint field.
/// </summary>
public interface IBaseAddressProvider {
    /// <summary>
    /// Base address to add to any address used to access the underlying IByteReaderWriter instance.
    /// </summary>
    public SegmentedAddress BaseAddress { get; }
}