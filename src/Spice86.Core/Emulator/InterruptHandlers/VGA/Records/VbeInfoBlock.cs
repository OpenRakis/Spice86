namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Utils;

/// <summary>
/// VBE Controller Information Block (256 bytes).
/// This structure contains information about the VBE controller and available video modes.
/// Programs typically call Function 00h (ReturnControllerInfo) first to detect VBE presence.
/// </summary>
public class VbeInfoBlock : MemoryBasedDataStructure {
    /// <summary>
    /// Size of the VbeInfoBlock structure in bytes.
    /// </summary>
    public const int StructureSize = 256;

    /// <summary>
    /// Initializes a new instance of the VbeInfoBlock class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory interface to read/write the structure.</param>
    /// <param name="baseAddress">The physical address where the structure is located.</param>
    public VbeInfoBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) 
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// VBE Signature - should be "VESA" (56h 45h 53h 41h).
    /// Offset: 00h, Size: 4 bytes.
    /// </summary>
    public uint VbeSignature {
        get => UInt32[0x00];
        set => UInt32[0x00] = value;
    }

    /// <summary>
    /// VBE Version number. For VBE 1.0, this is 0100h (BCD format: major.minor).
    /// Offset: 04h, Size: 2 bytes.
    /// </summary>
    public ushort VbeVersion {
        get => UInt16[0x04];
        set => UInt16[0x04] = value;
    }

    /// <summary>
    /// Pointer to OEM String (segment:offset format).
    /// Offset: 06h, Size: 4 bytes.
    /// </summary>
    public uint OemStringPtr {
        get => UInt32[0x06];
        set => UInt32[0x06] = value;
    }

    /// <summary>
    /// Capabilities of the graphics controller.
    /// Offset: 0Ah, Size: 4 bytes.
    /// </summary>
    public VbeCapabilities Capabilities {
        get => (VbeCapabilities)UInt32[0x0A];
        set => UInt32[0x0A] = (uint)value;
    }

    /// <summary>
    /// Pointer to list of supported video modes (segment:offset format).
    /// The list is terminated with 0xFFFF.
    /// Offset: 0Eh, Size: 4 bytes.
    /// </summary>
    public uint VideoModePtr {
        get => UInt32[0x0E];
        set => UInt32[0x0E] = value;
    }

    /// <summary>
    /// Total memory in 64KB blocks.
    /// Offset: 12h, Size: 2 bytes.
    /// </summary>
    public ushort TotalMemory {
        get => UInt16[0x12];
        set => UInt16[0x12] = value;
    }

    /// <summary>
    /// Sets the OEM string at the specified address.
    /// </summary>
    /// <param name="oemString">The OEM string to set.</param>
    /// <param name="stringAddress">The physical address where to write the string.</param>
    public void SetOemString(string oemString, uint stringAddress) {
        this.SetZeroTerminatedString(stringAddress, oemString, oemString.Length + 1);
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
            UInt16[modeListAddress + (uint)(i * 2)] = modes[i];
        }
        UInt16[modeListAddress + (uint)(modes.Length * 2)] = 0xFFFF;
        ushort segment = MemoryUtils.ToSegment(modeListAddress);
        ushort offset = (ushort)(modeListAddress & 0xFFFF);
        VideoModePtr = (uint)((segment << 16) | offset);
    }
}
