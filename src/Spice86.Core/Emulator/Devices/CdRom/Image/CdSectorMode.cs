namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Describes the sector encoding mode of a CD-ROM track.</summary>
public enum CdSectorMode {
    /// <summary>ISO 9660 cooked data, 2048 bytes per sector.</summary>
    CookedData2048 = 0,

    /// <summary>Raw CD-ROM sector, 2352 bytes per sector.</summary>
    Raw2352 = 1,

    /// <summary>CD-ROM XA Mode 2 Form 1, 2048 usable bytes inside a 2352-byte raw sector.</summary>
    Mode2Form1 = 2,

    /// <summary>CD-ROM XA Mode 2 Form 2, 2336 usable bytes inside a 2352-byte raw sector.</summary>
    Mode2Form2 = 3,

    /// <summary>Audio track stored as raw 2352-byte sectors.</summary>
    AudioRaw2352 = 4,
}
