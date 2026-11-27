namespace Spice86.Core.Emulator.OperatingSystem;

/// <summary>
///     A find first / find next search that has not ended, part of <see cref="DosState" />.
/// </summary>
/// <param name="SearchId">Key of the search, which find first wrote to the disk transfer area.</param>
/// <param name="FileSpec">File specification the search matches.</param>
/// <param name="Index">Index of the match the next find next returns.</param>
/// <param name="SearchAttributes">Attributes given to find first.</param>
/// <param name="IsFcbSearch">Whether the search was started through the FCB functions.</param>
public readonly record struct DosFileSearchState(
    uint SearchId,
    string FileSpec,
    int Index,
    ushort SearchAttributes,
    bool IsFcbSearch);
