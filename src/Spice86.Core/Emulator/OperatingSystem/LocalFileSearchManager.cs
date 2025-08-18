namespace Spice86.Core.Emulator.OperatingSystem;

using System.Runtime.CompilerServices;

public class LocalFileSearchManager {
    public string[] FindFilesUsingWildCmp(string searchFolder, string searchPattern,
        EnumerationOptions enumerationOptions) {
        var matches = new List<string>();
        ReadOnlySpan<char> patternSpan = searchPattern.AsSpan();
        foreach (string path in Directory.EnumerateFileSystemEntries(searchFolder, "*", enumerationOptions)) {
            ReadOnlySpan<char> nameSpan = Path.GetFileName(path.AsSpan());
            if (WildFileCmp(nameSpan, patternSpan)) {
                matches.Add(path);
            }
        }

        return matches.ToArray();
    }

    private const int DosMfnlength = 8;
    private const int DosExtlength = 3;
    private const int LfnNamelength = 255; // for the sanity check only

    public static bool WildFileCmp(string? file, string? wild) {
        if (file is null || wild is null) {
            return false;
        }

        return WildFileCmp(file.AsSpan(), wild.AsSpan());
    }

    // ... existing code ...
    private static bool WildFileCmp(ReadOnlySpan<char> file, ReadOnlySpan<char> wild) {
        if (file.Length > 0 && wild.Length == 0) {
            return false;
        }

        if (wild.Length > LfnNamelength) {
            return false;
        }

        // Fast path: exact case-insensitive match (covers common no-wildcard cases)
        if (file.Equals(wild, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        // Skip “hidden” dot-files if pattern uses wildcards (except "." / "..")
        if (WildcardMatchesHiddenFile(file, wild)) {
            return false;
        }

        // Split file
        int dot = file.LastIndexOf('.');
        ReadOnlySpan<char> fileNameRaw = dot >= 0 ? file[..dot] : file;
        ReadOnlySpan<char> fileExtRaw =
            dot >= 0 && dot + 1 < file.Length ? file[(dot + 1)..] : ReadOnlySpan<char>.Empty;

        // Split pattern
        int wdot = wild.LastIndexOf('.');
        ReadOnlySpan<char> wildNameRaw = wdot >= 0 ? wild[..wdot] : wild;
        ReadOnlySpan<char> wildExtRaw =
            wdot >= 0 && wdot + 1 < wild.Length ? wild[(wdot + 1)..] : ReadOnlySpan<char>.Empty;

        // Uppercase once into fixed-size 8.3 stacks with space padding
        Span<char> fn = stackalloc char[DosMfnlength];
        Span<char> fe = stackalloc char[DosExtlength];
        Span<char> wn = stackalloc char[DosMfnlength];
        // wild ext needs an extra slot to check the 4th char like DOSBox
        Span<char> we = stackalloc char[DosExtlength + 1];

        FillWithSpaces(fn);
        FillWithSpaces(fe);
        FillWithSpaces(wn);
        FillWithSpaces(we);

        ToUpperInto(fileNameRaw[..Math.Min(fileNameRaw.Length, DosMfnlength)], fn);
        ToUpperInto(fileExtRaw[..Math.Min(fileExtRaw.Length, DosExtlength)], fe);
        // DOSBox clamps wild name to 8 (+1 in original buffer is only for internal check), wild ext to 3 (+1 extra for trailing check)
        ToUpperInto(wildNameRaw[..Math.Min(wildNameRaw.Length, DosMfnlength)], wn);
        ToUpperInto(wildExtRaw[..Math.Min(wildExtRaw.Length, DosExtlength + 1)], we);

        // ---- NAME compare (no early '*' accept; '*' just ends name matching) ----
        for (int i = 0; i < DosMfnlength; i++) {
            char wc = wn[i];
            if (wc == '*') {
                break;
            }

            if (wc != '?' && wc != fn[i])
                return false;
        }
        // original code tolerated extra chars in pattern name beyond 8 unless it's a non-'*' check byte; our fixed-size truncation mirrors that behavior

        // ---- EXT compare (early '*' accept) ----
        for (int i = 0; i < DosExtlength; i++) {
            char wc = we[i];
            if (wc == '*') {
                return true;
            }

            if (wc != '?' && wc != fe[i]) {
                return false;
            }
        }

        // If wild ext has a 4th char and it's not '*', reject (DOSBox-like behavior)
        if (wildExtRaw.Length <= DosExtlength) {
            return true;
        }

        char c4 = we[DosExtlength];
        return c4 == '*';
    }

    // ---------- helpers ----------

    private static bool WildcardMatchesHiddenFile(ReadOnlySpan<char> filename, ReadOnlySpan<char> wildcard) {
        if (filename.IsEmpty) {
            return false;
        }

        bool hasWildcard = wildcard.IndexOfAny('?', '*') >= 0;

        bool isHidden = filename.Length >= 5 &&
                        filename[0] == '.' &&
                        !filename.Equals(".", StringComparison.Ordinal) &&
                        !filename.Equals("..", StringComparison.Ordinal);

        return hasWildcard && isHidden;
    }

    private static void FillWithSpaces(Span<char> buf) {
        for (int i = 0; i < buf.Length; i++) {
            buf[i] = ' ';
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToUpperInto(ReadOnlySpan<char> src, Span<char> dst) {
        // Write uppercase without allocations
        src.ToUpperInvariant(dst);
    }
}