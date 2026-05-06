namespace Spice86.DebuggerKnowledgeBase.Dos;

using System.Collections.Generic;

/// <summary>
/// Knowledge tables for DOS INT 21h: maps AH (and a few AX values) to a human-readable name and
/// short description. Mirrors the dispatch table in <c>DosInt21Handler</c>; new entries should be
/// added here whenever the handler grows.
/// </summary>
internal static class DosInt21DecodingTables {
    /// <summary>
    /// Subsystem label used in <c>DecodedCall.Subsystem</c>.
    /// </summary>
    public const string Subsystem = "DOS INT 21h";

    private static readonly IReadOnlyDictionary<byte, FunctionEntry> ByAh = new Dictionary<byte, FunctionEntry> {
        [0x00] = new FunctionEntry("Terminate Program (legacy)", "Exit to parent process via PSP terminate vector."),
        [0x01] = new FunctionEntry("Read Character with Echo", "Read a character from STDIN and echo it to STDOUT."),
        [0x02] = new FunctionEntry("Display Output", "Write the character in DL to STDOUT."),
        [0x03] = new FunctionEntry("Read Character from STDAUX", "Read a character from the auxiliary device."),
        [0x04] = new FunctionEntry("Write Character to STDAUX", "Write the character in DL to the auxiliary device."),
        [0x05] = new FunctionEntry("Printer Output", "Write the character in DL to LPT1."),
        [0x06] = new FunctionEntry("Direct Console I/O", "If DL=0xFF read a key, otherwise display DL."),
        [0x07] = new FunctionEntry("Direct STDIN Input (no echo)", "Read a character from STDIN without echo or break check."),
        [0x08] = new FunctionEntry("STDIN Input (no echo)", "Read a character from STDIN without echo, with Ctrl-Break check."),
        [0x09] = new FunctionEntry("Print String", "Write the '$'-terminated string at DS:DX to STDOUT."),
        [0x0A] = new FunctionEntry("Buffered Keyboard Input", "Read a line into the buffer at DS:DX."),
        [0x0B] = new FunctionEntry("Check STDIN Status", "Return 0xFF in AL if a character is available, else 0x00."),
        [0x0C] = new FunctionEntry("Flush Buffer + Keyboard Function", "Flush input buffer then call function in AL (1, 6, 7, 8 or A)."),
        [0x0D] = new FunctionEntry("Disk Reset", "Flush all file buffers to disk."),
        [0x0E] = new FunctionEntry("Select Default Drive", "Set the default drive to DL (0 = A:)."),
        [0x0F] = new FunctionEntry("FCB: Open File", "Open the FCB at DS:DX."),
        [0x10] = new FunctionEntry("FCB: Close File", "Close the FCB at DS:DX."),
        [0x11] = new FunctionEntry("FCB: Find First", "Find first matching file using the FCB at DS:DX."),
        [0x12] = new FunctionEntry("FCB: Find Next", "Find next matching file using the FCB at DS:DX."),
        [0x13] = new FunctionEntry("FCB: Delete File", "Delete the file matching the FCB at DS:DX."),
        [0x14] = new FunctionEntry("FCB: Sequential Read", "Read one record from the FCB at DS:DX."),
        [0x15] = new FunctionEntry("FCB: Sequential Write", "Write one record to the FCB at DS:DX."),
        [0x16] = new FunctionEntry("FCB: Create File", "Create the file described by the FCB at DS:DX."),
        [0x17] = new FunctionEntry("FCB: Rename File", "Rename the file described by the FCB at DS:DX."),
        [0x19] = new FunctionEntry("Get Default Drive", "Return the current default drive in AL (0 = A:)."),
        [0x1A] = new FunctionEntry("Set Disk Transfer Address", "Set DTA to DS:DX."),
        [0x1B] = new FunctionEntry("Get Default Drive Allocation Info", "Return cluster info for the default drive."),
        [0x1C] = new FunctionEntry("Get Drive Allocation Info", "Return cluster info for the drive in DL."),
        [0x21] = new FunctionEntry("FCB: Random Read", "Read one record at the random record number."),
        [0x22] = new FunctionEntry("FCB: Random Write", "Write one record at the random record number."),
        [0x23] = new FunctionEntry("FCB: Get File Size", "Return the file size in records."),
        [0x24] = new FunctionEntry("FCB: Set Random Record Number", "Set the FCB random record number."),
        [0x25] = new FunctionEntry("Set Interrupt Vector", "Install handler DS:DX for vector AL."),
        [0x26] = new FunctionEntry("Create New PSP", "Copy the current PSP to segment DX."),
        [0x27] = new FunctionEntry("FCB: Random Block Read", "Read CX records from the FCB at DS:DX."),
        [0x28] = new FunctionEntry("FCB: Random Block Write", "Write CX records to the FCB at DS:DX."),
        [0x29] = new FunctionEntry("FCB: Parse Filename", "Parse filename at DS:SI into FCB at ES:DI."),
        [0x2A] = new FunctionEntry("Get Date", "Return the system date."),
        [0x2B] = new FunctionEntry("Set Date", "Set the system date."),
        [0x2C] = new FunctionEntry("Get Time", "Return the system time."),
        [0x2D] = new FunctionEntry("Set Time", "Set the system time."),
        [0x2F] = new FunctionEntry("Get DTA Address", "Return the disk transfer address in ES:BX."),
        [0x30] = new FunctionEntry("Get DOS Version", "Return the DOS version in AX."),
        [0x31] = new FunctionEntry("Terminate and Stay Resident", "Exit but keep the program in memory."),
        [0x33] = new FunctionEntry("Get/Set Ctrl-Break", "Get or set the BREAK flag."),
        [0x34] = new FunctionEntry("Get InDOS Flag Address", "Return ES:BX pointing at the InDOS flag."),
        [0x35] = new FunctionEntry("Get Interrupt Vector", "Return the handler for vector AL in ES:BX."),
        [0x36] = new FunctionEntry("Get Free Disk Space", "Return free space for the drive in DL."),
        [0x38] = new FunctionEntry("Get/Set Country Info", "Get or set country-specific formatting."),
        [0x39] = new FunctionEntry("Create Directory", "Create the directory at DS:DX."),
        [0x3A] = new FunctionEntry("Remove Directory", "Remove the directory at DS:DX."),
        [0x3B] = new FunctionEntry("Change Current Directory", "Change CWD to DS:DX."),
        [0x3C] = new FunctionEntry("Create File", "Create or truncate file at DS:DX with attributes CX."),
        [0x3D] = new FunctionEntry("Open File", "Open file at DS:DX with access mode AL."),
        [0x3E] = new FunctionEntry("Close File", "Close the handle in BX."),
        [0x3F] = new FunctionEntry("Read From File or Device", "Read CX bytes from BX into DS:DX."),
        [0x40] = new FunctionEntry("Write To File or Device", "Write CX bytes from DS:DX to BX."),
        [0x41] = new FunctionEntry("Delete File", "Delete the file at DS:DX."),
        [0x42] = new FunctionEntry("Move File Pointer", "Seek handle BX by (CX:DX) using mode AL."),
        [0x43] = new FunctionEntry("Get/Set File Attributes", "Read or set attributes of file at DS:DX."),
        [0x44] = new FunctionEntry("I/O Control (IOCTL)", "Device I/O control sub-function in AL."),
        [0x45] = new FunctionEntry("Duplicate File Handle", "Allocate a new handle pointing at BX."),
        [0x46] = new FunctionEntry("Force Duplicate File Handle", "Force handle CX to point at BX."),
        [0x47] = new FunctionEntry("Get Current Directory", "Write CWD of drive DL into DS:SI."),
        [0x48] = new FunctionEntry("Allocate Memory Block", "Allocate BX paragraphs."),
        [0x49] = new FunctionEntry("Free Memory Block", "Free the block whose segment is in ES."),
        [0x4A] = new FunctionEntry("Modify Memory Block", "Resize block ES to BX paragraphs."),
        [0x4B] = new FunctionEntry("EXEC: Load and/or Execute", "Load/exec child program at DS:DX, mode AL."),
        [0x4C] = new FunctionEntry("Terminate with Exit Code", "Exit to parent. AL = exit code."),
        [0x4D] = new FunctionEntry("Get Return Code", "Return exit code/cause of the last child."),
        [0x4E] = new FunctionEntry("Find First Matching File", "Find first match for DS:DX with attributes CX."),
        [0x4F] = new FunctionEntry("Find Next Matching File", "Continue search from the previous Find First."),
        [0x50] = new FunctionEntry("Set Current PSP", "Set the active PSP to BX."),
        [0x51] = new FunctionEntry("Get Current PSP", "Return the active PSP segment in BX."),
        [0x52] = new FunctionEntry("Get List Of Lists", "Return ES:BX pointing at SYSVARS."),
        [0x55] = new FunctionEntry("Create Child PSP", "Create a child PSP at segment DX."),
        [0x58] = new FunctionEntry("Allocation Strategy / UMB Link", "Get/set allocation strategy or UMB link."),
        [0x62] = new FunctionEntry("Get Current PSP", "Return the active PSP segment in BX."),
        [0x63] = new FunctionEntry("Get Lead Byte Table", "Return the DBCS lead byte table."),
        [0x66] = new FunctionEntry("Get/Set Code Page", "Get or set the active code page.")
    };

    /// <summary>
    /// Returns the function entry for the given AH value, or a generic "unknown" entry.
    /// </summary>
    public static FunctionEntry GetEntry(byte ah) {
        if (ByAh.TryGetValue(ah, out FunctionEntry? entry)) {
            return entry;
        }
        return new FunctionEntry($"AH={ah:X2}h (unknown)", "Unknown DOS INT 21h sub-function.");
    }

    /// <summary>
    /// One row of the table: human-readable name and a short description.
    /// </summary>
    public sealed record FunctionEntry(string Name, string Description);
}
