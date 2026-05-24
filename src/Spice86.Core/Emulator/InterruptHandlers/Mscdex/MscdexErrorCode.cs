namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

/// <summary>Error codes returned in AX by MSCDEX INT 2Fh AH=15h subfunctions on failure.</summary>
public enum MscdexErrorCode : ushort {
    /// <summary>Operation completed successfully.</summary>
    Success = 0x0000,

    /// <summary>Write protect error (same numeric value as <see cref="Success"/>).</summary>
    WriteProtect = 0x0000,

    /// <summary>Drive is not ready.</summary>
    DriveNotReady = 0x0002,

    /// <summary>Bad command or parameter.</summary>
    BadCommand = 0x0003,

    /// <summary>Read fault.</summary>
    ReadFault = 0x000B,

    /// <summary>Invalid drive specified.</summary>
    InvalidDrive = 0x000F,

    /// <summary>Invalid function requested (0x22).</summary>
    InvalidFunction = 0x0016,

    /// <summary>Drive is locked.</summary>
    DriveLocked = 0x0021,
}
