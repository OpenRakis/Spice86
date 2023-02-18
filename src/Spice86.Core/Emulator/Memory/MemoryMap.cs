using Spice86.Core.Emulator.OperatingSystem;

namespace Spice86.Core.Emulator.Memory;

/// <summary> Informations about memory mapping of an IBM PC </summary>
public static class MemoryMap {
    /// <summary>
    /// Segment that contains a list of addresses of interrupt handlers.
    /// </summary>
    public const int InterruptVectorSegment = 0x0000;
    
    /// <summary>
    /// Segment containing the BIOS data area.
    /// </summary>
    public const ushort BiosDataSegment = 0x0040;
    
    public const int BootSectorCodeLength = 512;

    public const int BootSectorCodeSegment = 0x07C0;

    public const int ColorTextVideoMemoryLength = 32767;

    public const int ColorTextVideoMemorySegment = 0xB800;

    public const int FreeMemoryStartSegment = 0x50;

    public const int GraphicVideoMemorylength = 65535;

    public const int GraphicVideoMemorySegment = 0xA000;

    public const int InterruptVectorLength = 1024;

    public const int MonochromeTextVideoMemoryLength = 32767;

    public const int MonochromeTextVideoMemorySegment = 0xB000;

    /// <summary>
    /// Segment where VGA BIOS is stored.
    /// </summary>
    public const ushort VideoBiosSegment = 0xC000;

    /// <summary>
    /// Segment where our interrupt handler routines are stored.
    /// These are filled with special opcode FE 38 XX CF which will make the CPU pass control to the registered
    /// callback with index 0xXX.
    /// The interrupt vector table at 0x0000:0x0000 will contain pointers to these routines.
    /// </summary>
    public const ushort InterruptHandlersSegment = 0xF000;

    /// <summary>
    /// Segment where DOS device driver headers are stored.
    /// <see cref="VirtualDeviceBase"/>
    /// </summary>
    public const ushort DeviceDriverSegment = 0xF800;
}