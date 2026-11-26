namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

/// <summary>
///     State of one DMA channel, as captured by <see cref="DmaBus.CaptureState" />.
/// </summary>
/// <remarks>
///     The page base address is not stored: it is derived from <paramref name="PageRegisterValue" /> on restore.
/// </remarks>
/// <param name="BaseAddress">Base address register, reloaded into the current address on auto-init.</param>
/// <param name="BaseCount">Base count register, reloaded into the current count on auto-init.</param>
/// <param name="CurrentAddress">Current address register.</param>
/// <param name="CurrentCount">Current count register.</param>
/// <param name="PageRegisterValue">Value last written to the channel's page register.</param>
/// <param name="IsAutoiniting">Whether the mode register selected auto-init.</param>
/// <param name="IsIncremented">Whether the mode register selected address increment.</param>
/// <param name="IsMasked">Whether the channel is masked.</param>
/// <param name="HasReachedTerminalCount">
///     Whether the channel reached its terminal count since the status register was last read.
/// </param>
/// <param name="HasRaisedRequest">
///     Whether the device on the channel asserts its request line, shown in the status register.
/// </param>
public readonly record struct DmaChannelState(
    ushort BaseAddress,
    ushort BaseCount,
    uint CurrentAddress,
    ushort CurrentCount,
    byte PageRegisterValue,
    bool IsAutoiniting,
    bool IsIncremented,
    bool IsMasked,
    bool HasReachedTerminalCount,
    bool HasRaisedRequest);
