using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>Parses CUE sheet files into a structured <see cref="CueSheet"/>.</summary>
public sealed class CueSheetParser {
    /// <summary>Reads and parses the CUE sheet at <paramref name="cueFilePath"/>.</summary>
    /// <param name="cueFilePath">Path to the .cue file.</param>
    /// <returns>A fully populated <see cref="CueSheet"/>.</returns>
    /// <exception cref="IOException">Thrown when the file cannot be read.</exception>
    /// <exception cref="InvalidDataException">Thrown when a time field is malformed.</exception>
    public static CueSheet Parse(string cueFilePath) {
        string[] lines = File.ReadAllLines(cueFilePath);

        string cueDir = Path.GetDirectoryName(cueFilePath) ?? string.Empty;
        List<CueEntry> entries = new List<CueEntry>();
        string catalog = string.Empty;
        string currentFile = string.Empty;
        CueFileType currentFileType = CueFileType.Binary;
        string currentTrackMode = string.Empty;
        int currentTrackNumber = 0;
        int currentPregap = 0;
        int currentPostgap = 0;

        foreach (string line in lines.Select(l => l.Trim())) {
            if (line.StartsWith("CATALOG ", StringComparison.OrdinalIgnoreCase)) {
                catalog = line.Substring("CATALOG ".Length).Trim();
            } else if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase)) {
                currentFile = ParseFileName(line, cueDir);
                currentFileType = ParseFileType(line);
            } else if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase)) {
                currentTrackNumber = ParseTrackNumber(line);
                currentTrackMode = ParseTrackMode(line);
                currentPregap = 0;
                currentPostgap = 0;
            } else if (line.StartsWith("PREGAP ", StringComparison.OrdinalIgnoreCase)) {
                string msfPart = line.Substring("PREGAP ".Length).Trim();
                currentPregap = MsfToFrames(msfPart);
            } else if (line.StartsWith("POSTGAP ", StringComparison.OrdinalIgnoreCase)) {
                string msfPart = line.Substring("POSTGAP ".Length).Trim();
                currentPostgap = MsfToFrames(msfPart);
            } else if (line.StartsWith("INDEX ", StringComparison.OrdinalIgnoreCase)) {
                ParseIndexLine(line, currentFile, currentFileType, currentTrackMode, currentTrackNumber, currentPregap, currentPostgap, entries);
            }
        }

        return new CueSheet(entries, catalog);
    }

    private static string ParseFileName(string line, string cueDir) {
        Match match = Regex.Match(line, @"FILE\s+(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase);
        if (!match.Success) {
            throw new InvalidDataException($"Malformed FILE directive in CUE sheet: '{line}'.");
        }

        string raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        if (Path.IsPathRooted(raw)) {
            return raw;
        }
        if (string.IsNullOrEmpty(cueDir)) {
            return raw;
        }
        return Path.GetFullPath(raw, cueDir);
    }

    private static int ParseTrackNumber(string line) {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return int.Parse(parts[1]);
    }

    private static string ParseTrackMode(string line) {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[2];
    }

    private static void ParseIndexLine(
        string line,
        string currentFile,
        CueFileType currentFileType,
        string currentTrackMode,
        int currentTrackNumber,
        int currentPregap,
        int currentPostgap,
        List<CueEntry> entries) {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int indexNumber = int.Parse(parts[1]);
        int indexFrames = MsfToFrames(parts[2]);

        CueEntry entry = new CueEntry {
            FileName = currentFile,
            FileType = currentFileType,
            TrackMode = currentTrackMode,
            TrackNumber = currentTrackNumber,
            IndexNumber = indexNumber,
            IndexMsf = indexFrames,
            Pregap = currentPregap,
            Postgap = currentPostgap,
        };
        entries.Add(entry);
    }

    private static CueFileType ParseFileType(string line) {
        Match match = Regex.Match(line, @"FILE\s+(?:""[^""]+""|\S+)\s+(\S+)\s*$", RegexOptions.IgnoreCase);
        if (!match.Success) {
            throw new InvalidDataException($"Malformed FILE directive in CUE sheet: '{line}'.");
        }
        string token = match.Groups[1].Value;
        if (token.Equals("BINARY", StringComparison.OrdinalIgnoreCase)) {
            return CueFileType.Binary;
        }
        if (token.Equals("MOTOROLA", StringComparison.OrdinalIgnoreCase)) {
            return CueFileType.Motorola;
        }
        if (token.Equals("WAVE", StringComparison.OrdinalIgnoreCase) || token.Equals("WAV", StringComparison.OrdinalIgnoreCase)) {
            return CueFileType.Wave;
        }
        if (token.Equals("AIFF", StringComparison.OrdinalIgnoreCase)) {
            return CueFileType.Aiff;
        }
        if (token.Equals("MP3", StringComparison.OrdinalIgnoreCase)) {
            return CueFileType.Mp3;
        }
        if (token.Equals("FLAC", StringComparison.OrdinalIgnoreCase)) {
            return CueFileType.Flac;
        }
        if (token.Equals("OGG", StringComparison.OrdinalIgnoreCase)) {
            return CueFileType.Ogg;
        }
        if (token.Equals("OPUS", StringComparison.OrdinalIgnoreCase)) {
            return CueFileType.Opus;
        }
        return CueFileType.Binary;
    }

    private static int MsfToFrames(string msf) {
        string[] parts = msf.Split(':');
        if (parts.Length != 3) {
            throw new InvalidDataException($"Malformed MSF time value: '{msf}'. Expected MM:SS:FF format.");
        }
        if (!int.TryParse(parts[0], out int minutes)
            || !int.TryParse(parts[1], out int seconds)
            || !int.TryParse(parts[2], out int frames)) {
            throw new InvalidDataException($"Malformed MSF time value: '{msf}'. Expected MM:SS:FF format.");
        }
        return minutes * 60 * 75 + seconds * 75 + frames;
    }
}
