namespace Spice86.Core.Emulator.OperatingSystem.Enums;

using System;

/// <summary>
/// Control flags for INT 21h AH=29h filename parsing into FCB format.
/// </summary>
/// <remarks>
/// <para>
/// These flags control how the DOS filename parser interprets the input string when converting
/// it to FCB (File Control Block) format. The FCB format requires: 8-char filename + 3-char extension,
/// space-padded, uppercase, with no directory separator or dot.
/// </para>
/// <para>
/// <b>DOS Parsing Behavior:</b>
/// <list type="bullet">
///   <item><b>Separators:</b> Common separators like space, tab, colon, semicolon, etc. can be skipped at the start</item>
///   <item><b>Drive:</b> Optional "X:" prefix where X is A-Z. If missing, can default to current drive or leave as 0</item>
///   <item><b>Filename:</b> Up to 8 characters. Shorter names are space-padded. Can be left unchanged if missing.</item>
///   <item><b>Extension:</b> Up to 3 characters after optional dot. Space-padded if shorter. Can be left unchanged if missing.</item>
///   <item><b>Wildcards:</b> '?' matches single character, '*' fills rest of field with '?'</item>
/// </list>
/// </para>
/// <para>
/// <b>Flag Combinations:</b>
/// Flags can be OR'd together. For example, <c>SkipLeadingSeparators | LeaveDriveUnchanged</c>
/// skips leading spaces/tabs and leaves the drive unchanged if no "X:" prefix is found.
/// </para>
/// <para>
/// <b>References:</b>
/// <list type="bullet">
///   <item>DOSBox Staging: dos_files.cpp FCB_Parsename</item>
///   <item>FreeDOS kernel: fcbfns.c FcbParseFname (lines 91-188)</item>
///   <item>Ralf Brown's Interrupt List: INT 21h AH=29h</item>
/// </list>
/// </para>
/// </remarks>
[Flags]
public enum FcbParseControl : byte {
    /// <summary>
    /// No special parsing behavior. Use defaults for everything.
    /// </summary>
    None = 0x00,

    /// <summary>
    /// Skip leading separator characters before parsing filename.
    /// Separator characters are: : ; , = + space tab
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set, the parser consumes all separator characters at the start of the input string
    /// before looking for the drive letter or filename.
    /// </para>
    /// <para>
    /// Example: "  : TEST.TXT" → with flag: "TEST.TXT", without flag: parse fails
    /// </para>
    /// <para>
    /// FreeDOS: if (*wTestMode &amp; PARSE_SKIP_LEAD_SEP) (fcbfns.c:98-102)
    /// </para>
    /// </remarks>
    SkipLeadingSeparators = 0x01,

    /// <summary>
    /// Leave FCB drive field unchanged if no drive letter is specified in the input string.
    /// When NOT set, sets drive to default (0) if no "X:" prefix found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Inverted bit semantics:</b> The bit logic is inverted from DOS's flag name:
    /// <list type="bullet">
    ///   <item>When bit is CLEAR (0): Sets FCB drive to 0 (default drive) if no "X:" found</item>
    ///   <item>When bit is SET (1): Leaves FCB drive field unchanged if no "X:" found</item>
    /// </list>
    /// </para>
    /// <para>
    /// This matches FreeDOS behavior: "if (!(*wTestMode &amp; PARSE_DFLT_DRIVE))" (fcbfns.c:130)
    /// The FreeDOS flag name "PARSE_DFLT_DRIVE" suggests "use default drive", but the code checks
    /// for the bit being CLEAR to set default. This enum name reflects the actual behavior when SET.
    /// </para>
    /// <para>
    /// Example: Input "TEST.TXT" (no drive letter)
    /// <list type="bullet">
    ///   <item>Flag CLEAR: FCB.Drive = 0 (default)</item>
    ///   <item>Flag SET: FCB.Drive = unchanged (whatever it was before)</item>
    /// </list>
    /// </para>
    /// </remarks>
    LeaveDriveUnchanged = 0x02,

    /// <summary>
    /// Blank filename field if no filename is found in the input string.
    /// When NOT set, leaves the FCB filename field unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like LeaveDriveUnchanged, this has inverted semantics:
    /// <list type="bullet">
    ///   <item>When bit is CLEAR (0): Fills FCB filename with spaces if input has no filename</item>
    ///   <item>When bit is SET (1): Leaves FCB filename field unchanged</item>
    /// </list>
    /// </para>
    /// <para>
    /// This allows applications to pre-populate an FCB with a default filename, then parse
    /// a partial filename string that only updates specific fields.
    /// </para>
    /// <para>
    /// Example: FCB.FileName = "DEFAULT ", input ".TXT" (extension only)
    /// <list type="bullet">
    ///   <item>Flag CLEAR: FCB.FileName = "        " (8 spaces)</item>
    ///   <item>Flag SET: FCB.FileName = "DEFAULT " (unchanged)</item>
    /// </list>
    /// </para>
    /// </remarks>
    BlankFilename = 0x04,

    /// <summary>
    /// Blank extension field if no extension is found in the input string.
    /// When NOT set, leaves the FCB extension field unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inverted semantics like BlankFilename:
    /// <list type="bullet">
    ///   <item>When bit is CLEAR (0): Fills FCB extension with spaces if input has no extension</item>
    ///   <item>When bit is SET (1): Leaves FCB extension field unchanged</item>
    /// </list>
    /// </para>
    /// <para>
    /// Useful for building FCB templates where you want to preserve a default extension.
    /// </para>
    /// <para>
    /// Example: FCB.Extension = "BAK", input "TEST" (no extension)
    /// <list type="bullet">
    ///   <item>Flag CLEAR: FCB.Extension = "   " (3 spaces)</item>
    ///   <item>Flag SET: FCB.Extension = "BAK" (unchanged)</item>
    /// </list>
    /// </para>
    /// </remarks>
    BlankExtension = 0x08
}
