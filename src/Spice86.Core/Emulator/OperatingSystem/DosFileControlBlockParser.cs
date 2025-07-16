namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Utils;

using System;
using System.Text;

/// <summary>
/// Parses filenames into FCB structures.
/// </summary>
public class DosFileControlBlockParser {
    private readonly IMemory _memory;
    private readonly DosDriveManager _dosDriveManager;
    private const string FCB_SEPARATORS = ":;,=+";
    private const string ILLEGAL_CHARS = ":.;,=+ \t/\"[]<>|";

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory to use for reading and writing FCBs.</param>
    /// <param name="dosDriveManager">The drive manager to access drive information.</param>
    public DosFileControlBlockParser(IMemory memory, DosDriveManager dosDriveManager) {
        _memory = memory;
        _dosDriveManager = dosDriveManager;
    }

    /// <summary>
    /// Parses a filename into an FCB.
    /// </summary>
    /// <param name="fcbSegment">Segment of the FCB.</param>
    /// <param name="fcbOffset">Offset of the FCB.</param>
    /// <param name="parser">Parsing flags.</param>
    /// <param name="filename">Filename to parse.</param>
    /// <param name="changeOffset">Out parameter that will contain how many bytes were processed.</param>
    /// <returns>A parse result indicating success or failure.</returns>
    public FcbParseResult ParseName(ushort fcbSegment, ushort fcbOffset, byte parser, string filename, out byte changeOffset) {
        // Create FCB instance based on the segment:offset provided
        DosFileControlBlock fcb = new(_memory, MemoryUtils.ToPhysicalAddress(fcbSegment, fcbOffset));
        
        // Check if the FCB is an extended FCB (has 0xFF as first byte)
        if (fcb.Drive == 0xFF) {
            // This is an extended FCB, adjust address and create a new instance
            DosExtendedFileControlBlock extFcb = new(fcb);
            fcb = extFcb;
            
            // If parser flag doesn't force default drive, invalidate the extended FCB
            if ((parser & 0x02) == 0) {
                fcb.Drive = 0;
            }
        }

        bool hasDrive = false;
        bool hasName = false;
        bool hasExt = false;
        byte fillChar = (byte)' ';
        FcbParseResult result = FcbParseResult.NoWildcards;
        
        // Copy the original FCB data
        string fileName = fcb.FileName.TrimEnd();
        string extension = fcb.Extension.TrimEnd();
        byte drive = fcb.Drive;
        
        // Skip leading spaces
        int index = 0;
        while (index < filename.Length && (filename[index] == ' ' || filename[index] == '\t')) {
            index++;
        }
        
        // Strip leading separator if parser has PARSE_SEP_STOP flag
        if ((parser & 0x01) != 0 && index < filename.Length) {
            if (FCB_SEPARATORS.Contains(filename[index])) {
                index++;
            }
        }
        
        // Skip more spaces
        while (index < filename.Length && (filename[index] == ' ' || filename[index] == '\t')) {
            index++;
        }
        
        // Check for drive letter
        if (index + 1 < filename.Length && filename[index + 1] == ':') {
            char driveLetter = char.ToUpper(filename[index]);
            if (!IsValidChar(driveLetter)) {
                index += 2;
                changeOffset = (byte)index;
                return FcbParseResult.InvalidDrive;
            }
            
            drive = 0;
            hasDrive = true;
            
            // Check if drive exists
            if (char.IsLetter(driveLetter) && 
                _dosDriveManager.ContainsKey(driveLetter)) {
                drive = (byte)(char.ToUpper(driveLetter) - 'A' + 1);
            } else {
                result = FcbParseResult.InvalidDrive;
            }
            
            index += 2; // Skip drive letter and colon
        }
        
        // Check for extension only file (starting with .)
        if (index < filename.Length && filename[index] == '.') {
            index++;
            goto check_extension;
        }
        
        // Process the filename part
        if (index < filename.Length && IsValidChar(filename[index])) {
            hasName = true;
            int nameIndex = 0;
            
            // Copy filename (up to 8 characters)
            StringBuilder nameBuilder = new();
            while (index < filename.Length) {
                char c = filename[index];
                if (!IsValidChar(c)) break;
                
                char upperC = char.ToUpper(c);
                if (upperC == '*') {
                    fillChar = (byte)'?';
                    upperC = '?';
                }
                
                if (upperC == '?' && result == FcbParseResult.NoWildcards && nameIndex < 8) {
                    result = FcbParseResult.Wildcards;
                }
                
                if (nameIndex < 8) {
                    nameBuilder.Append(fillChar == '?' ? '?' : upperC);
                    nameIndex++;
                }
                
                index++;
            }
            
            // Pad the name with fill character
            while (nameIndex < 8) {
                nameBuilder.Append(fillChar);
                nameIndex++;
            }
            
            fileName = nameBuilder.ToString();
        }
        
        // If no extension, we're done
        if (index >= filename.Length || filename[index] != '.') {
            goto save_fcb;
        }
        
        // Skip the dot
        index++;
        
check_extension:
        // Process the extension part
        hasExt = true;
        fillChar = (byte)' ';
        int extIndex = 0;
        
        StringBuilder extBuilder = new();
        while (index < filename.Length) {
            char c = filename[index];
            if (!IsValidChar(c)) break;
            
            char upperC = char.ToUpper(c);
            if (upperC == '*') {
                fillChar = (byte)'?';
                upperC = '?';
            }
            
            if (upperC == '?' && result == FcbParseResult.NoWildcards && extIndex < 3) {
                result = FcbParseResult.Wildcards;
            }
            
            if (extIndex < 3) {
                extBuilder.Append(fillChar == '?' ? '?' : upperC);
                extIndex++;
            }
            
            index++;
        }
        
        // Pad the extension with fill character
        while (extIndex < 3) {
            extBuilder.Append(fillChar);
            extIndex++;
        }
        
        extension = extBuilder.ToString();
        
save_fcb:
        // Apply parser flags
        if (!hasDrive && (parser & 0x02) == 0) drive = 0;
        if (!hasName && (parser & 0x04) == 0) fileName = "        ";
        if (!hasExt && (parser & 0x08) == 0) extension = "   ";
        
        // Save back to FCB
        fcb.Drive = drive;
        fcb.FileName = fileName;
        fcb.Extension = extension;
        
        fcb.CurrentBlock = 0;
        fcb.LogicalRecordSize = 0;
        
        // Set the change offset (how many characters were processed)
        changeOffset = (byte)index;
        
        return result;
    }
    
    private bool IsValidChar(char c) {
        // Valid characters are:
        // - Above ASCII 0x1F
        // - Not in the ILLEGAL_CHARS list
        return c > 0x1F && !ILLEGAL_CHARS.Contains(c);
    }

    /// <summary>
    /// Converts an FCB to a full DOS filename.
    /// </summary>
    /// <param name="fcb">The FCB to get the name from.</param>
    /// <returns>The full DOS filename represented by the FCB.</returns>
    public string GetFcbName(DosFileControlBlock fcb) {
        char driveLetter = fcb.Drive == 0 ? (char)('A' + _dosDriveManager.CurrentDriveIndex) : (char)('A' + fcb.Drive - 1);
        string filename = fcb.FileName.TrimEnd();
        string extension = fcb.Extension.TrimEnd();
        
        if (extension.Length > 0) {
            return $"{driveLetter}:{filename}.{extension}";
        } else {
            return $"{driveLetter}:{filename}";
        }
    }

    /// <summary>
    /// Splits a full DOS filename into name and extension parts for FCB use.
    /// </summary>
    /// <param name="fullname">The full filename to split.</param>
    /// <returns>A tuple containing the name and extension.</returns>
    public static (string Name, string Ext) SplitFcbName(string fullname) {
        // This function splits the filename like DOS_FCB does in DOSBox
        string[] parts = fullname.Split('.');
        
        string name = parts[0];
        string ext = parts.Length > 1 ? parts[1] : "";
        
        // Pad or truncate name to 8 characters
        if (name.Length < 8) {
            name = name.PadRight(8, ' ');
        } else if (name.Length > 8) {
            name = name.Substring(0, 8);
        }
        
        // Pad or truncate extension to 3 characters
        if (ext.Length < 3) {
            ext = ext.PadRight(3, ' ');
        } else if (ext.Length > 3) {
            ext = ext.Substring(0, 3);
        }
        
        return (name, ext);
    }
}