using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Parses CUE sheet files into a structured <see cref="CueSheet"/>.</summary>
public sealed class CueSheetParser {
    /// <summary>Reads and parses the CUE sheet at <paramref name="cueFilePath"/>.</summary>
    /// <param name="cueFilePath">Path to the .cue file.</param>
    /// <returns>A fully populated <see cref="CueSheet"/>.</returns>
    /// <exception cref="IOException">Thrown when the file cannot be read.</exception>
    /// <exception cref="InvalidDataException">Thrown when a time field is malformed.</exception>
    public CueSheet Parse(string cueFilePath) {
        string[] lines = File.ReadAllLines(cueFilePath);

        string cueDir = Path.GetDirectoryName(cueFilePath) ?? string.Empty;
        List<CueEntry> entries = new List<CueEntry>();
        string? catalog = null;
        string currentFile = string.Empty;
        string currentTrackMode = string.Empty;
        int currentTrackNumber = 0;
        int currentPregap = 0;
        int currentPostgap = 0;

        foreach (string line in lines.Select(l => l.Trim())) {
            if (line.StartsWith("CATALOG ", StringComparison.OrdinalIgnoreCase)) {
                catalog = line.Substring("CATALOG ".Length).Trim();
            } else if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase)) {
                currentFile = ParseFileName(line, cueDir);
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
                ParseIndexLine(line, currentFile, currentTrackMode, currentTrackNumber, currentPregap, currentPostgap, entries);
            }
        }

        return new CueSheet(entries, catalog);
    }

    private static string ParseFileName(string line, string cueDir) {
        Match match = Regex.Match(line, @"FILE\s+(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase);
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
            TrackMode = currentTrackMode,
            TrackNumber = currentTrackNumber,
            IndexNumber = indexNumber,
            IndexMsf = indexFrames,
            Pregap = currentPregap,
            Postgap = currentPostgap,
        };
        entries.Add(entry);
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
