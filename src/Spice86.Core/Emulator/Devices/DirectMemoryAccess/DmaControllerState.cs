namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

/// <summary>
///     State of one Intel 8237 DMA controller, as captured by <see cref="DmaBus.CaptureState" />.
/// </summary>
/// <param name="Channel0">First channel of the controller (channel 0 or 4).</param>
/// <param name="Channel1">Second channel of the controller (channel 1 or 5).</param>
/// <param name="Channel2">Third channel of the controller (channel 2 or 6).</param>
/// <param name="Channel3">Fourth channel of the controller (channel 3 or 7).</param>
/// <param name="FlipFlop">
///     Byte pointer flip-flop. <see langword="true" /> when the next address or count access is to the high byte.
/// </param>
public readonly record struct DmaControllerState(
    DmaChannelState Channel0,
    DmaChannelState Channel1,
    DmaChannelState Channel2,
    DmaChannelState Channel3,
    bool FlipFlop);
