namespace Spice86.Core.Emulator.LoadableFile;

using Spice86.Core.Emulator.StateSerialization.CfgReload;

using System.Globalization;
using System.Linq;

/// <summary>
/// Immutable copy of the changed-bytes map for the code generator: the merged
/// <c>[start, end)</c> areas the loaders wrote and the merged <c>[start, end)</c> byte ranges the
/// program changed afterwards. A range is physical-address based.
/// </summary>
internal sealed record ChangedMemorySnapshot {
    /// <summary>Merged watched areas, sorted by start.</summary>
    public required IReadOnlyList<(uint Start, uint End)> WatchedAreas { get; init; }

    /// <summary>Merged changed ranges, sorted by start.</summary>
    public required IReadOnlyList<(uint Start, uint End)> ChangedRanges { get; init; }

    /// <summary>
    /// True only when every byte of <c>[start, start + length)</c> sits inside a watched area and no
    /// byte changed. Bytes outside the watched areas are never "unchanged".
    /// </summary>
    public bool IsUnchanged(uint start, int length) {
        if (length <= 0) {
            return true;
        }
        uint end = start + (uint)length;
        uint covered = start;
        foreach ((uint areaStart, uint areaEnd) in WatchedAreas) {
            if (areaEnd <= covered) {
                continue;
            }
            if (areaStart > covered) {
                return false;
            }
            covered = areaEnd;
            if (covered >= end) {
                break;
            }
        }
        if (covered < end) {
            return false;
        }
        foreach ((uint rangeStart, uint rangeEnd) in ChangedRanges) {
            if (rangeEnd <= start) {
                continue;
            }
            if (rangeStart >= end) {
                break;
            }
            return false;
        }
        return true;
    }

    /// <summary>Serializes merged ranges as <c>"0xSTART-0xEND"</c> pairs for the reload dump;
    /// <see langword="null"/> when the list is empty so the field is omitted.</summary>
    internal static string[]? SerializeRanges(IReadOnlyList<(uint Start, uint End)> ranges) {
        if (ranges.Count == 0) {
            return null;
        }
        return ranges.Select(range => $"0x{range.Start:X8}-0x{range.End:X8}").ToArray();
    }

    /// <summary>Rebuilds a snapshot from the watched and changed fields of a reload dump.</summary>
    internal static ChangedMemorySnapshot FromReloadDump(CfgReloadDump dump) {
        return new ChangedMemorySnapshot {
            WatchedAreas = ParseRanges(dump.WatchedAreas),
            ChangedRanges = ParseRanges(dump.ChangedRanges)
        };
    }

    private static IReadOnlyList<(uint Start, uint End)> ParseRanges(string[]? ranges) {
        return ranges?.Select(ParseRange).ToArray() ?? [];
    }

    private static (uint Start, uint End) ParseRange(string range) {
        string[] parts = range.Split('-');
        if (parts.Length != 2 || !parts[0].StartsWith("0x") || !parts[1].StartsWith("0x")
            || !uint.TryParse(parts[0][2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint start)
            || !uint.TryParse(parts[1][2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint end)) {
            throw new InvalidOperationException($"Cannot parse changed-memory range '{range}'");
        }
        return (start, end);
    }
}
