namespace Spice86.Core.Emulator.LoadableFile;

/// <summary>
/// Which loader produced a <see cref="LoadedImage"/>.
/// </summary>
public enum LoadedImageKind {
    /// <summary>An EXE or COM program DOS loaded through the EXEC path.</summary>
    DosProgram,

    /// <summary>The in-memory COM image the batch engine runs for internal commands.</summary>
    InternalBatchProgram,

    /// <summary>An EXE overlay DOS loaded at an explicit segment.</summary>
    DosOverlay,

    /// <summary>A BIOS binary loaded at the BIOS segment.</summary>
    BiosBinary,

    /// <summary>A floppy boot sector loaded at the boot address.</summary>
    BootSector
}
