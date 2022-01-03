namespace Spice86.Emulator.Memory;

/// <summary>
/// Informations about memory mapping of an IBM PC
/// </summary>
public static class MemoryMap
{
    public static readonly int INTERRUPT_VECTOR_SEGMENT = 0x0;
    public static readonly int INTERRUPT_VECTOR_LENGTH = 1024;
    public static readonly int BIOS_DATA_AREA_SEGMENT = 0x40;
    public static readonly int BIOS_DATA_AREA_LENGTH = 256;
    public static readonly int FREE_MEMORY_START_SEGMENT = 0x50;
    // This is where the port to get VGA CRT status is stored
    public static readonly int BIOS_DATA_AREA_OFFSET_CRT_IO_PORT = 0x63;
    // Counter incremented 18.2 times per second
    public static readonly int BIOS_DATA_AREA_OFFSET_TICK_COUNTER = 0x6C;
    public static readonly int BOOT_SECTOR_CODE_SEGMENT = 0x07C0;
    public static readonly int BOOT_SECTOR_CODE_LENGTH = 512;
    public static readonly int GRAPHIC_VIDEO_MEMORY_SEGMENT = 0xA000;
    public static readonly int GRAPHIC_VIDEO_MEMORY_LENGTH = 65535;
    public static readonly int MONOCHROME_TEXT_VIDEO_MEMORY_SEGMENT = 0xB000;
    public static readonly int MONOCHROME_TEXT_VIDEO_MEMORY_LENGTH = 32767;
    public static readonly int COLOR_TEXT_VIDEO_MEMORY_SEGMENT = 0xB800;
    public static readonly int COLOR_TEXT_VIDEO_MEMORY_LENGTH = 32767;
}
