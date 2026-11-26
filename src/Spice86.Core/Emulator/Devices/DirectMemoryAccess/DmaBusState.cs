namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

/// <summary>
///     Complete state of the two DMA controllers, as captured by <see cref="DmaBus.CaptureState" />.
/// </summary>
/// <param name="Primary">8-bit controller of channels 0 to 3.</param>
/// <param name="Secondary">16-bit controller of channels 4 to 7.</param>
public readonly record struct DmaBusState(DmaControllerState Primary, DmaControllerState Secondary);
