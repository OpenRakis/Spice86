namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

/// <summary>Error codes returned in AX by MSCDEX INT 2Fh AH=15h subfunctions on failure.</summary>
public enum MscdexErrorCode : ushort {
    /// <summary>Operation completed successfully.</summary>
    Success = 0x0000,

    /// <summary>Invalid MSCDEX function.</summary>
    InvalidFunction = 0x0001,

    /// <summary>Directory entry was not found.</summary>
    DirectoryEntryNotFound = 0x0002,

    /// <summary>Directory entry or file was not found.</summary>
    FileNotFound = DirectoryEntryNotFound,

    /// <summary>Invalid CD volume format.</summary>
    BadFormat = 0x000B,

    /// <summary>Unknown CD-ROM drive.</summary>
    UnknownDrive = 0x000F,

    /// <summary>Drive is not ready.</summary>
    DriveNotReady = 0x0015,

    /// <summary>Compatibility alias for the DOSBox unknown-drive result.</summary>
    InvalidDrive = UnknownDrive,
}
