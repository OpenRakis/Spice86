namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// VBE Controller Information Block (256 bytes).
/// This structure contains information about the VBE controller and available video modes.
/// Programs typically call Function 00h (ReturnControllerInfo) first to detect VBE presence.
/// </summary>
public class VbeInfoBlock {
    private readonly IIndexable _memory;
    private readonly uint _address;

    /// <summary>
    /// Size of the VbeInfoBlock structure in bytes.
    /// </summary>
    public const int StructureSize = 256;

    /// <summary>
    /// Initializes a new instance of the VbeInfoBlock class.
    /// </summary>
    /// <param name="memory">The memory interface to read/write the structure.</param>
    /// <param name="address">The physical address where the structure is located.</param>
    public VbeInfoBlock(IIndexable memory, uint address) {
        _memory = memory;
        _address = address;
    }

    /// <summary>
    /// VBE Signature - should be "VESA" (56h 45h 53h 41h).
    /// Offset: 00h, Size: 4 bytes.
    /// </summary>
    public uint VbeSignature {
        get => _memory.UInt32[_address];
        set => _memory.UInt32[_address] = value;
    }

    /// <summary>
    /// VBE Version number. For VBE 1.0, this is 0100h (BCD format: major.minor).
    /// Offset: 04h, Size: 2 bytes.
    /// </summary>
    public ushort VbeVersion {
        get => _memory.UInt16[_address + 0x04];
        set => _memory.UInt16[_address + 0x04] = value;
    }

    /// <summary>
    /// Pointer to OEM String (segment:offset format).
    /// Offset: 06h, Size: 4 bytes.
    /// </summary>
    public uint OemStringPtr {
        get => _memory.UInt32[_address + 0x06];
        set => _memory.UInt32[_address + 0x06] = value;
    }

    /// <summary>
    /// Capabilities of the graphics controller.
    /// Offset: 0Ah, Size: 4 bytes.
    /// </summary>
    public VbeCapabilities Capabilities {
        get => (VbeCapabilities)_memory.UInt32[_address + 0x0A];
        set => _memory.UInt32[_address + 0x0A] = (uint)value;
    }

    /// <summary>
    /// Pointer to list of supported video modes (segment:offset format).
    /// The list is terminated with 0xFFFF.
    /// Offset: 0Eh, Size: 4 bytes.
    /// </summary>
    public uint VideoModePtr {
        get => _memory.UInt32[_address + 0x0E];
        set => _memory.UInt32[_address + 0x0E] = value;
    }

    /// <summary>
    /// Total memory in 64KB blocks.
    /// Offset: 12h, Size: 2 bytes.
    /// </summary>
    public ushort TotalMemory {
        get => _memory.UInt16[_address + 0x12];
        set => _memory.UInt16[_address + 0x12] = value;
    }

    /// <summary>
    /// Sets the OEM string at the specified address.
    /// </summary>
    /// <param name="oemString">The OEM string to set.</param>
    /// <param name="stringAddress">The physical address where to write the string.</param>
    public void SetOemString(string oemString, uint stringAddress) {
        _memory.SetZeroTerminatedString(stringAddress, oemString, oemString.Length + 1);
        // Convert physical address to segment:offset format (far pointer)
        ushort segment = MemoryUtils.ToSegment(stringAddress);
        ushort offset = (ushort)(stringAddress & 0xFFFF);
        OemStringPtr = (uint)((segment << 16) | offset);
    }

    /// <summary>
    /// Sets the video mode list at the specified address.
    /// </summary>
    /// <param name="modes">The list of supported mode numbers.</param>
    /// <param name="modeListAddress">The physical address where to write the mode list.</param>
    public void SetVideoModeList(ushort[] modes, uint modeListAddress) {
        for (int i = 0; i < modes.Length; i++) {
            _memory.UInt16[modeListAddress + (uint)(i * 2)] = modes[i];
        }
        // Terminate list with 0xFFFF
        _memory.UInt16[modeListAddress + (uint)(modes.Length * 2)] = 0xFFFF;
        // Convert physical address to segment:offset format (far pointer)
        ushort segment = MemoryUtils.ToSegment(modeListAddress);
        ushort offset = (ushort)(modeListAddress & 0xFFFF);
        VideoModePtr = (uint)((segment << 16) | offset);
    }
}
