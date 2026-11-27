namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using System.Collections.Immutable;

/// <summary>
///     Complete state of the XMS driver, as captured by <see cref="ExtendedMemoryManager.CaptureState" />.
/// </summary>
/// <remarks>
///     The A20 line itself belongs to <see cref="Spice86.Core.Emulator.Memory.A20Gate" /> and is not part of this
///     state. The HMA is in main memory and is not part of it either.
/// </remarks>
/// <param name="Blocks">The extended memory blocks, free and allocated, in the driver's order.</param>
/// <param name="HandleLockCounts">The lock count of each allocated handle.</param>
/// <param name="IsHighMemoryAreaClaimed">Whether a program owns the HMA.</param>
/// <param name="IsA20GloballyEnabled">Whether the global A20 enable of the driver is on.</param>
/// <param name="A20LocalEnableCount">Number of local A20 enables not yet matched by a local disable.</param>
/// <param name="Memory">
///     The extended memory (<see cref="ExtendedMemoryManager.XmsRam" />) up to its last non-zero byte. The rest of
///     it is zero.
/// </param>
public sealed record XmsState(
    ImmutableArray<XmsBlock> Blocks,
    ImmutableDictionary<int, byte> HandleLockCounts,
    bool IsHighMemoryAreaClaimed,
    bool IsA20GloballyEnabled,
    uint A20LocalEnableCount,
    ReadOnlyMemory<byte> Memory);
