namespace Spice86.ViewModels.DataModels;

/// <summary>
/// Single segment of the conventional memory bar.
/// </summary>
public sealed class ConventionalMemorySegment {
    /// <summary>Starting segment address.</summary>
    public ushort StartSegment { get; init; }

    /// <summary>Size of the block, in bytes (used for relative bar width).</summary>
    public long SizeBytes { get; init; }

    /// <summary>True when the block is free, false when allocated.</summary>
    public bool IsFree { get; init; }

    /// <summary>Owner display label (PSP segment or owner name).</summary>
    public string Owner { get; init; } = string.Empty;

    /// <summary>Whether this block is the last MCB in the chain.</summary>
    public bool IsLast { get; init; }
}
