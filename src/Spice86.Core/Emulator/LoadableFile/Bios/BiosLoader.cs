namespace Spice86.Core.Emulator.LoadableFile.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Interfaces;

using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Loader for BIOS files.<br/>
/// Bios entry point is at physical address 0xFFFF0 (F000:FFF0).
/// </summary>
public class BiosLoader : ExecutableFileLoader {
    
    /// <summary>
    /// Offset of the BIOS code within the segment.
    /// </summary>
    private const ushort CodeOffset = 0xFFF0;
    
    /// <summary>
    /// Segment where the BIOS code is loaded.
    /// </summary>
    private const ushort CodeSegment = 0xF000;
    
    /// <summary>
    /// Indicates whether DOS initialization is needed for the loaded file (always false for BIOS).
    /// </summary>
    public override bool DosInitializationNeeded => false;

    /// <summary>
    /// Initializes a new instance of the <see cref="BiosLoader"/> class with the specified <paramref name="machine"/> and <paramref name="loggerService"/>.
    /// </summary>
    /// <param name="machine">The machine instance to load the BIOS on.</param>
    /// <param name="loggerService">The logger service to log messages to.</param>
    public BiosLoader(IMemory memory, State state, ILoggerService loggerService) : base(memory, state, loggerService) {
    }

    /// <summary>
    /// Loads the specified BIOS <paramref name="file"/> into memory and sets the entry point to the BIOS address.
    /// </summary>
    /// <param name="file">The path to the BIOS file to load.</param>
    /// <param name="arguments">Ignored for BIOS files.</param>
    /// <returns>The loaded BIOS file as a byte array.</returns>
    public override byte[] LoadFile(string file, string? arguments) {
        byte[] bios = ReadFile(file);
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(CodeSegment, 0);
        _memory.LoadData(physicalStartAddress, bios);
        SetEntryPoint(CodeSegment, CodeOffset);
        return bios;
    }
}
