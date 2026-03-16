namespace Spice86.Core.Emulator.Errors;

using Spice86.Shared.Emulator.Memory;

public class UnsupportedBitWidthException(BitWidth bitWidth) : InvalidOperationException($"Unsupported bit width {bitWidth}");