namespace Spice86.Core.Emulator.OperatingSystem;

using System;
using System.Text;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

/// <summary>
/// FCB (File Control Block) manager - rewritten to match FreeDOS kernel behavior.
/// Based on fcbfns.c from FreeDOS kernel.
/// </summary>
public class DosFcbManager {
    public const byte FcbSuccess = 0x00;
    public const byte FcbError = 0xFF;
    public const byte FcbErrorNoData = 0x01;
    public const byte FcbErrorSegmentWrap = 0x02;
    public const byte FcbErrorEof = 0x03;

    // FreeDOS parse control constants
    private const byte PARSE_SKIP_LEAD_SEP = 0x01;
    private const byte PARSE_DFLT_DRIVE = 0x02;
    private const byte PARSE_BLNK_FNAME = 0x04;
    private const byte PARSE_BLNK_FEXT = 0x08;

    private const byte PARSE_RET_NOWILD = 0;
    private const byte PARSE_RET_WILD = 1;
    private const byte PARSE_RET_BADDRIVE = 0xFF;

    // FreeDOS separators
    private const string COMMON_SEPS = ":;,=+ \t";
    private const string FIELD_SEPS = "/\\\"[]<>|.:;,=+\t";

    private readonly IMemory _memory;
    private readonly DosFileManager _dosFileManager;
    private readonly DosDriveManager _dosDriveManager;
    private readonly ILoggerService _loggerService;

    public DosFcbManager(IMemory memory, DosFileManager dosFileManager, DosDriveManager dosDriveManager, ILoggerService loggerService) {
        _memory = memory;
        _dosFileManager = dosFileManager;
        _dosDriveManager = dosDriveManager;
        _loggerService = loggerService;
    }

    /// <summary>
    /// INT 21h, AH=29h - Parse Filename into FCB.
    /// Based on FreeDOS FcbParseFname (fcbfns.c lines 89-177).
    /// </summary>
    /// <param name="stringAddress">The address of the filename string to parse.</param>
    /// <param name="fcbAddress">The address of the FCB to fill.</param>
    /// <param name="parseControl">Parsing control byte (PARSE_* flags).</param>
    /// <param name="bytesAdvanced">Output: offset of next byte in input string.</param>
    /// <returns>0 if no wildcards, 1 if wildcards, 0xFF if invalid drive.</returns>
    public byte ParseFilename(uint stringAddress, uint fcbAddress, byte parseControl, out uint bytesAdvanced) {
        string filename = _memory.GetZeroTerminatedString(stringAddress, 128);
        DosFileControlBlock fcb = GetFcb(fcbAddress, out _);
        
        int pos = 0;
        bool retCodeDrive = false;
        bool retCodeName = false;
        bool retCodeExt = false;

        // Skip leading separators if requested
        // FreeDOS: "if (*wTestMode & PARSE_SKIP_LEAD_SEP)"
        if ((parseControl & PARSE_SKIP_LEAD_SEP) != 0) {
            while (pos < filename.Length && TestCmnSeps(filename[pos])) {
                pos++;
            }
        }

        // Skip whitespace (undocumented: "Undocumented 'feature,' we skip white space anyway")
        pos = ParseSkipWh(filename, pos);

        // Check for drive specification
        // FreeDOS: "if (!TestFieldSeps(lpFileName) && *(lpFileName + 1) == ':'"
        if (pos + 1 < filename.Length && filename[pos + 1] == ':' && !TestFieldSeps(filename[pos])) {
            char driveChar = char.ToUpper(filename[pos]);
            if (driveChar >= 'A' && driveChar <= 'Z') {
                byte driveNum = (byte)(driveChar - 'A');
                
                // FreeDOS: "Undocumented behavior: should keep parsing even if drive is invalid"
                if (!_dosDriveManager.HasDriveAtIndex(driveNum)) {
                    retCodeDrive = true;
                }
                
                fcb.DriveNumber = (byte)(driveNum + 1);
                pos += 2;
            }
        } else if ((parseControl & PARSE_DFLT_DRIVE) == 0) {
            // FreeDOS: "} else if (!(*wTestMode & PARSE_DFLT_DRIVE)) {"
            // If flag NOT set, set to default drive (0)
            fcb.DriveNumber = 0;
        }

        /* Undocumented behavior, set record number & record size to 0  */
        /* per MS-DOS Encyclopedia pp269 no other FCB fields modified   */
        /* except zeroing current block and record size fields          */
        // FreeDOS: "lpFcb->fcb_cublock = lpFcb->fcb_recsiz = 0;"
        fcb.CurrentBlock = 0;
        fcb.RecordSize = 0;

        // Blank filename field if requested
        if ((parseControl & PARSE_BLNK_FNAME) == 0) {
            fcb.FileName = "        ";
        }
        
        // Blank extension field if requested
        if ((parseControl & PARSE_BLNK_FEXT) == 0) {
            fcb.FileExtension = "   ";
        }

        // Special cases: '.' and '..'
        // FreeDOS: "if (*lpFileName == '.')"
        if (pos < filename.Length && filename[pos] == '.') {
            StringBuilder nameBuilder = new("        ");
            nameBuilder[0] = '.';
            pos++;
            
            if (pos < filename.Length && filename[pos] == '.') {
                nameBuilder[1] = '.';
                pos++;
            }
            
            fcb.FileName = nameBuilder.ToString();
            bytesAdvanced = (uint)pos;
            return retCodeDrive ? PARSE_RET_BADDRIVE : PARSE_RET_NOWILD;
        }

        // Parse filename field
        int nameStart = pos;
        (pos, retCodeName) = GetNameField(filename, pos, 8);
        fcb.FileName = ExtractAndPadField(filename, nameStart, pos, 8, retCodeName);

        // Parse extension if present
        if (pos < filename.Length && filename[pos] == '.') {
            pos++;
            int extStart = pos;
            (pos, retCodeExt) = GetNameField(filename, pos, 3);
            fcb.FileExtension = ExtractAndPadField(filename, extStart, pos, 3, retCodeExt);
        }

        bytesAdvanced = (uint)pos;

        // Return appropriate code
        if (retCodeDrive) {
            return PARSE_RET_BADDRIVE;
        } else if (retCodeName || retCodeExt) {
            return PARSE_RET_WILD;
        } else {
            return PARSE_RET_NOWILD;
        }
    }

    /// <summary>
    /// Checks if character is a common separator.
    /// FreeDOS: TestCmnSeps - ":;,=+ \t"
    /// </summary>
    private static bool TestCmnSeps(char c) {
        return COMMON_SEPS.Contains(c);
    }

    /// <summary>
    /// Checks if character is a field separator.
    /// FreeDOS: TestFieldSeps - (unsigned char)*lpFileName &lt;= ' ' || "/\\\"[]&lt;&gt;|.:;,=+\t"
    /// </summary>
    private static bool TestFieldSeps(char c) {
        return c <= ' ' || FIELD_SEPS.Contains(c);
    }

    /// <summary>
    /// Skip whitespace.
    /// FreeDOS: ParseSkipWh
    /// </summary>
    private static int ParseSkipWh(string filename, int pos) {
        while (pos < filename.Length && (filename[pos] == ' ' || filename[pos] == '\t')) {
            pos++;
        }
        return pos;
    }

    /// <summary>
    /// Parse a name field (filename or extension).
    /// FreeDOS: GetNameField.
    /// Returns a tuple of (newPos, hasWildcard).
    /// </summary>
    private (int newPos, bool hasWildcard) GetNameField(string filename, int pos, int fieldSize) {
        bool hasWildcard = false;
        int index = 0;

        while (pos < filename.Length && !TestFieldSeps(filename[pos]) && index < fieldSize) {
            char c = filename[pos];
            
            if (c == '*') {
                hasWildcard = true;
                pos++;
                break;
            }
            
            if (c == '?') {
                hasWildcard = true;
            }
            
            index++;
            pos++;
        }

        return (pos, hasWildcard);
    }

    /// <summary>
    /// Extract field from string and pad/convert to proper form.
    /// Handles asterisk conversion to question marks.
    /// </summary>
    private string ExtractAndPadField(string filename, int startPos, int endPos, int fieldSize, bool hasWildcard) {
        StringBuilder result = new();
        int pos = startPos;
        int index = 0;
        bool hitAsterisk = false;

        // Extract actual characters
        while (pos < endPos && !TestFieldSeps(filename[pos]) && index < fieldSize) {
            if (filename[pos] == '*') {
                hitAsterisk = true;
                pos++;
                break;
            }
            
            result.Append(char.ToUpper(filename[pos]));
            index++;
            pos++;
        }

        // If we hit asterisk, fill rest with ?
        if (hitAsterisk) {
            while (index < fieldSize) {
                result.Append('?');
                index++;
            }
        } else {
            // Pad with spaces
            while (index < fieldSize) {
                result.Append(' ');
                index++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Get file control block from memory address.
    /// </summary>
    public DosFileControlBlock GetFcb(uint fcbAddress, out byte attribute) {
        attribute = 0;
        return new DosFileControlBlock(_memory, fcbAddress);
    }

    public byte OpenFile(uint fcbAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte CloseFile(uint fcbAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte CreateFile(uint fcbAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte SequentialRead(uint fcbAddress, uint dtaAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte SequentialWrite(uint fcbAddress, uint dtaAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte RandomRead(uint fcbAddress, uint dtaAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte RandomWrite(uint fcbAddress, uint dtaAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte RandomBlockRead(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte RandomBlockWrite(uint fcbAddress, uint dtaAddress, ref ushort recordCount) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte GetFileSize(uint fcbAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte DeleteFile(uint fcbAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte RenameFile(uint fcbAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public void SetRandomRecordNumber(uint fcbAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte FindFirst(uint fcbAddress, uint dtaAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public byte FindNext(uint fcbAddress, uint dtaAddress) {
        throw new NotImplementedException("To be implemented TDD style");
    }

    public void ClearAllSearchState() {
        throw new NotImplementedException("To be implemented TDD style");
    }
}

