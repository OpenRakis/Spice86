namespace Spice86.ViewModels.DataModels;

/// <summary>
/// One block in the conventional memory bar: a single MCB sized by <see cref="SizeBytes"/>.
/// </summary>
/// <param name="HeaderSegment">MCB header segment as a hex string (e.g. <c>0x0800</c>).</param>
/// <param name="SizeBytes">Total size of the block (header excluded) in bytes.</param>
/// <param name="IsFree">True if the block is free.</param>
/// <param name="IsLast">True if the block is the final (Z) block of the chain.</param>
/// <param name="OwnerName">Owner program name (empty for free blocks).</param>
/// <param name="Tooltip">Human-readable tooltip shown when hovering this block.</param>
public sealed record DosMcbBarItem(
    string HeaderSegment,
    long SizeBytes,
    bool IsFree,
    bool IsLast,
    string OwnerName,
    string Tooltip);
