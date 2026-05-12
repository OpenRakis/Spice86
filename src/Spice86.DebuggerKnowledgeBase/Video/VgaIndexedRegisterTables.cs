namespace Spice86.DebuggerKnowledgeBase.Video;

using System.Collections.Generic;

/// <summary>
/// Static lookup tables for the VGA indexed register groups (Sequencer, CRT Controller, Graphics
/// Controller, Attribute Controller). Names mirror the canonical EGA/VGA documentation as used by
/// dosbox-staging's <c>vga_seq.cpp</c>, <c>vga_crtc.cpp</c>, <c>vga_gfx.cpp</c> and
/// <c>vga_attr.cpp</c>.
/// </summary>
internal static class VgaIndexedRegisterTables {
    /// <summary>Sequencer registers selected through port 0x3C4 / read-write through 0x3C5.</summary>
    public static readonly IReadOnlyDictionary<byte, string> Sequencer = new Dictionary<byte, string> {
        [0x00] = "Reset",
        [0x01] = "Clocking Mode",
        [0x02] = "Map Mask (Plane Write Enable)",
        [0x03] = "Character Map Select",
        [0x04] = "Memory Mode",
        [0x07] = "Horizontal Character Counter Reset"
    };

    /// <summary>CRT Controller registers selected through port 0x3D4 (color) or 0x3B4 (mono).</summary>
    public static readonly IReadOnlyDictionary<byte, string> CrtController = new Dictionary<byte, string> {
        [0x00] = "Horizontal Total",
        [0x01] = "Horizontal Display End",
        [0x02] = "Start Horizontal Blanking",
        [0x03] = "End Horizontal Blanking",
        [0x04] = "Start Horizontal Retrace",
        [0x05] = "End Horizontal Retrace",
        [0x06] = "Vertical Total",
        [0x07] = "Overflow",
        [0x08] = "Preset Row Scan",
        [0x09] = "Maximum Scan Line",
        [0x0A] = "Cursor Start",
        [0x0B] = "Cursor End",
        [0x0C] = "Start Address High",
        [0x0D] = "Start Address Low",
        [0x0E] = "Cursor Location High",
        [0x0F] = "Cursor Location Low",
        [0x10] = "Vertical Retrace Start",
        [0x11] = "Vertical Retrace End",
        [0x12] = "Vertical Display End",
        [0x13] = "Offset (Logical Line Width)",
        [0x14] = "Underline Location",
        [0x15] = "Start Vertical Blanking",
        [0x16] = "End Vertical Blanking",
        [0x17] = "CRTC Mode Control",
        [0x18] = "Line Compare"
    };

    /// <summary>Graphics Controller registers selected through port 0x3CE / read-write through 0x3CF.</summary>
    public static readonly IReadOnlyDictionary<byte, string> GraphicsController = new Dictionary<byte, string> {
        [0x00] = "Set/Reset",
        [0x01] = "Enable Set/Reset",
        [0x02] = "Color Compare",
        [0x03] = "Data Rotate / Function Select",
        [0x04] = "Read Map Select",
        [0x05] = "Graphics Mode",
        [0x06] = "Miscellaneous",
        [0x07] = "Color Don't Care",
        [0x08] = "Bit Mask"
    };

    /// <summary>Attribute Controller registers selected through port 0x3C0.</summary>
    public static readonly IReadOnlyDictionary<byte, string> AttributeController = new Dictionary<byte, string> {
        [0x00] = "Palette Register 0",
        [0x01] = "Palette Register 1",
        [0x02] = "Palette Register 2",
        [0x03] = "Palette Register 3",
        [0x04] = "Palette Register 4",
        [0x05] = "Palette Register 5",
        [0x06] = "Palette Register 6",
        [0x07] = "Palette Register 7",
        [0x08] = "Palette Register 8",
        [0x09] = "Palette Register 9",
        [0x0A] = "Palette Register 10",
        [0x0B] = "Palette Register 11",
        [0x0C] = "Palette Register 12",
        [0x0D] = "Palette Register 13",
        [0x0E] = "Palette Register 14",
        [0x0F] = "Palette Register 15",
        [0x10] = "Mode Control",
        [0x11] = "Overscan (Border) Color",
        [0x12] = "Color Plane Enable",
        [0x13] = "Horizontal Pixel Panning",
        [0x14] = "Color Select"
    };

    /// <summary>Returns the register name for the index, or "Unknown" when not in the table.</summary>
    public static string Lookup(IReadOnlyDictionary<byte, string> table, byte index) {
        if (table.TryGetValue(index, out string? name)) {
            return name;
        }
        return "Unknown";
    }
}
