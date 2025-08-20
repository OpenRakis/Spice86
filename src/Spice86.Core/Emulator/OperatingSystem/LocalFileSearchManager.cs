namespace Spice86.Core.Emulator.OperatingSystem;

using System.Runtime.CompilerServices;

public class LocalFileSearchManager {
    private const int DosMfnlength = 8;
    private const int DosExtlength = 3;
    private const int LfnNamelength = 255; // for the sanity check only
    
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

    public static bool WildFileCmp(string? filename, string? pattern) {
        if (filename is null || pattern is null) {
            return false;
        }

        return WildFileCmp(filename.AsSpan(), pattern.AsSpan());
    }

    private static bool WildFileCmp(ReadOnlySpan<char> sourceFilename, ReadOnlySpan<char> pattern) {
        if (sourceFilename.Length > 0 && pattern.Length == 0) {
            return false;
        }

        if (pattern.Length > LfnNamelength) {
            return false;
        }

        // Fast path: exact case-insensitive match (covers common no-wildcard cases)
        if (sourceFilename.Equals(pattern, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        // Skip “hidden” dot-files if the pattern uses wildcards (except "." / "..")
        if (WildcardMatchesHiddenFile(sourceFilename, pattern)) {
            return false;
        }
        
        // Uppercase once into fixed-size 8.3 stacks with space padding
        Span<char> fileName = stackalloc char[DosMfnlength];
        Span<char> fileExt = stackalloc char[DosExtlength];
        Span<char> wildName = stackalloc char[DosMfnlength];
        // wild ext needs an extra slot to check the 4th char like DOSBox
        Span<char> wildExt = stackalloc char[DosExtlength + 1];

        SplitTo83(sourceFilename, fileName, fileExt, out _);
        SplitTo83(pattern, wildName, wildExt, out int wildExtLength);

        // ---- NAME compare (no early '*' accept; '*' just ends name matching) ----
        if (CompareSegment(fileName, wildName, DosMfnlength, false) == false) {
            return false;
        }
        // the original code tolerated extra chars in pattern name beyond 8 unless it's a non-'*' check byte; our fixed-size truncation mirrors that behavior

        // ---- EXT compare (early '*' accept) ----
        return CompareSegment(fileExt, wildExt, DosExtlength, true) switch {
            true => true,
            false => false,
            // If wild ext has a 4th char, and it's not '*', reject (DOSBox-like behavior)
            _ => wildExtLength <= DosExtlength || wildExt[DosExtlength] == '*'
        };
    }

    // Common 8.3 segment compare:
    // - Compares up to 'length' characters.
    // - '?' matches any char (including space padding).
    // - If '*' is encountered:
    //     - when earlyStarAcceptsTrue == true (extension), return true immediately;
    //     - otherwise (name), treat as "stop comparing here" and return null (no decision).
    // Returns:
    //   true => definitively accept (only possible with earlyStarAcceptsTrue and '*' seen)
    //   false => mismatch
    //   null => matched the segment fully (or name-stopped at '*'), no final decision
    private static bool? CompareSegment(ReadOnlySpan<char> target, ReadOnlySpan<char> pattern, int length,
        bool earlyStarAcceptsTrue) {
        for (int i = 0; i < length; i++) {
            char wc = pattern[i];
            if (wc == '*') {
                return earlyStarAcceptsTrue ? true : null;
            }

            if (wc != '?' && wc != target[i]) {
                return false;
            }
        }

        return null;
    }

    private static void SplitTo83(ReadOnlySpan<char> file, Span<char> targetFileName, Span<char> targetFileExt,
        out int extLength) {
        targetFileName.Fill(' ');
        targetFileExt.Fill(' ');

        int dotPos = file.LastIndexOf('.');
        ReadOnlySpan<char> fileNameRaw = dotPos >= 0 ? file[..dotPos] : file;
        ReadOnlySpan<char> fileExtRaw =
            dotPos >= 0 && dotPos + 1 < file.Length ? file[(dotPos + 1)..] : ReadOnlySpan<char>.Empty;
        ToUpperInto(fileNameRaw[..Math.Min(fileNameRaw.Length, targetFileName.Length)], targetFileName);
        ToUpperInto(fileExtRaw[..Math.Min(fileExtRaw.Length, targetFileExt.Length)], targetFileExt);
        extLength = fileExtRaw.Length; // actual (untruncated) length for the DOSBox-style 4th-char check
    }

    private static bool WildcardMatchesHiddenFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> wildcard) {
        if (fileName.IsEmpty) {
            return false;
        }

        if (wildcard.IndexOfAny('?', '*') < 0) {
            return false;
        }

        return fileName.Length >= 5 && fileName[0] == '.' &&
               !fileName.Equals(".", StringComparison.Ordinal) &&
               !fileName.Equals("..", StringComparison.Ordinal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToUpperInto(ReadOnlySpan<char> src, Span<char> dst) {
        src.ToUpperInvariant(dst);
    }
}