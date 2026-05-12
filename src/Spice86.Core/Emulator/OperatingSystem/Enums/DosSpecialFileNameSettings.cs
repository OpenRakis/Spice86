namespace Spice86.Core.Emulator.OperatingSystem.Enums;

using System;

/// <summary>Settings for controlling how DOS special file names are parsed and handled.</summary>
[Flags]
public enum DosSpecialFileNameSettings {
    /// <summary>Default settings.</summary>
    None = 0,

    /// <summary>Do not allow superscript '1', '2', or '3' in builtin device names.</summary>
    /// <remarks>
    /// Windows automatically parses device names containing superscript digits the same as regular device names
    /// containing ASCII digits. This will indicate to the parser that those device names should be treated as invalid
    /// file names.
    /// </remarks>
    NoDeviceSuperscriptDigits = 1 << 0,
}
