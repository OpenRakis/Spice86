namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Text;

/// <summary>
/// Handles parsing of filenames into DOS File Control Blocks (FCBs).
/// Implements DOS INT 21H function 29H functionality.
/// </summary>
public class DosFileControlBlockParser {
    private readonly IMemory _memory;
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosFileControlBlockParser(IMemory memory, ILoggerService loggerService) {
        _memory = memory;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Parses a filename string into an FCB structure.
    /// </summary>
    /// <param name="fcbAddress">The physical address of the FCB to fill.</param>
    /// <param name="parseControl">Parse control flags:
    /// Bit 0: Skip leading separators
    /// Bit 1: Change drive only if drive letter is specified
    /// Bit 2: Change filename only if filename is specified  
    /// Bit 3: Change extension only if extension is specified</param>
    /// <param name="filename">The filename string to parse.</param>
    /// <param name="bytesProcessed">Returns the number of bytes processed from the filename string.</param>
    /// <returns>Parse result code:
    /// 0 = No wildcards encountered
    /// 1 = Wildcards encountered  
    /// 0xFF = Drive letter invalid</returns>
    public byte ParseFilename(uint fcbAddress, byte parseControl, string filename, out int bytesProcessed) {
        bytesProcessed = 0;

        if (string.IsNullOrEmpty(filename)) {
            return 0;
        }

        DosFileControlBlock fcb = new DosFileControlBlock(_memory, fcbAddress);

        // Initialize FCB if not preserving existing values
        if ((parseControl & 0x0E) == 0) {
            fcb.Initialize();
        }

        int index = 0;
        bool wildcardsFound = false;

        // Skip leading separators if requested
        if ((parseControl & 0x01) != 0) {
            while (index < filename.Length && IsSeparator(filename[index])) {
                index++;
            }
        }

        // Parse drive letter
        if (index + 1 < filename.Length && filename[index + 1] == ':') {
            char driveLetter = char.ToUpperInvariant(filename[index]);
            if (driveLetter >= 'A' && driveLetter <= 'Z') {
                if ((parseControl & 0x02) == 0) { // Change drive only if flag not set
                    fcb.DriveNumber = (byte)(driveLetter - 'A' + 1);
                }
                index += 2; // Skip drive letter and colon
            } else {
                bytesProcessed = index + 2;
                return 0xFF; // Invalid drive letter
            }
        }

        // Skip any separators after drive
        while (index < filename.Length && IsSeparator(filename[index])) {
            index++;
        }

        // Parse filename (up to 8 characters)
        string parsedName = "";
        int nameStart = index;
        while (index < filename.Length && !IsSeparator(filename[index]) && filename[index] != '.') {
            if (parsedName.Length < 8) {
                char c = char.ToUpperInvariant(filename[index]);
                if (c == '?' || c == '*') {
                    wildcardsFound = true;
                    if (c == '*') {
                        // '*' fills remaining positions with '?'
                        while (parsedName.Length < 8) {
                            parsedName += '?';
                        }
                        break;
                    } else {
                        parsedName += '?';
                    }
                } else if (IsValidFilenameChar(c)) {
                    parsedName += c;
                } else {
                    break; // Invalid character ends parsing
                }
            }
            index++;
        }

        if ((parseControl & 0x04) == 0 && index > nameStart) { // Change filename only if flag not set
            fcb.FileName = parsedName.PadRight(8);
        }

        // Parse extension if present
        if (index < filename.Length && filename[index] == '.') {
            index++; // Skip the dot
            string parsedExt = "";
            int extStart = index;
            while (index < filename.Length && !IsSeparator(filename[index])) {
                if (parsedExt.Length < 3) {
                    char c = char.ToUpperInvariant(filename[index]);
                    if (c == '?' || c == '*') {
                        wildcardsFound = true;
                        if (c == '*') {
                            // '*' fills remaining positions with '?'
                            while (parsedExt.Length < 3) {
                                parsedExt += '?';
                            }
                            break;
                        } else {
                            parsedExt += '?';
                        }
                    } else if (IsValidFilenameChar(c)) {
                        parsedExt += c;
                    } else {
                        break; // Invalid character ends parsing
                    }
                }
                index++;
            }

            if ((parseControl & 0x08) == 0 && index > extStart) { // Change extension only if flag not set
                fcb.FileExtension = parsedExt.PadRight(3);
            }
        }

        bytesProcessed = index;

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("FCB Parse: Drive={Drive}, Name={Name}, Ext={Ext}, Wildcards={Wildcards}, Processed={Processed}",
                fcb.DriveNumber, fcb.FileName.TrimEnd(), fcb.FileExtension.TrimEnd(), wildcardsFound, bytesProcessed);
        }

        return wildcardsFound ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Checks if a character is a path separator or other separator character.
    /// </summary>
    private static bool IsSeparator(char c) {
        return c == ' ' || c == '\t' || c == '\\' || c == '/' || c == ':' || c == ';' || c == ',' || c == '=';
    }

    /// <summary>
    /// Checks if a character is valid in a DOS filename.
    /// </summary>
    private static bool IsValidFilenameChar(char c) {
        // DOS filename characters: A-Z, 0-9, and some special characters
        return char.IsLetterOrDigit(c) ||
               "!#$%&'()-@^_`{}~".Contains(c);
    }
}