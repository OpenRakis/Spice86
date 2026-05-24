namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents the VBE Info Block structure (VBE 1.0/1.2).
/// "The purpose of this function is to provide information to the calling program
/// about the general capabilities of the Super VGA environment. The function fills
/// an information block structure at the address specified by the caller.
/// The information block size is 256 bytes."
/// Returned by VBE Function 00h - Return VBE Controller Information.
/// Minimum size is 256 bytes, but callers should provide ~512 bytes for OEM string and mode list.
/// </summary>
public class VbeInfoBlock : MemoryBasedDataStructure {
    private const int SignatureLength = 4;
    private const int OemStringMaxLength = 256;

    /// <summary>
    /// Initializes a new instance of the <see cref="VbeInfoBlock"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    /// <param name="baseAddress">The base address of the structure in memory.</param>
    public VbeInfoBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// "The VESASignature field contains the characters 'VESA' if this is a valid block."
    /// Gets or sets the VBE signature (4 bytes: "VESA" for VBE 1.x, "VBE2" for VBE 2.0+).
    /// Offset: 0x00, Size: 4 bytes.
    /// </summary>
    public string Signature {
        get => GetZeroTerminatedString(0x00, SignatureLength);
        set {
            string truncated = value.Length >= SignatureLength
                ? value[..SignatureLength]
                : value;
            for (int i = 0; i < SignatureLength; i++) {
                UInt8[0x00 + i] = i < truncated.Length ? (byte)truncated[i] : (byte)0;
            }
        }
    }

    /// <summary>
    /// "The VESAVersion is a binary field which specifies what level of the VESA
    /// standard the Super VGA BIOS conforms to. The higher byte specifies the major
    /// version number. The lower byte specifies the minor version number. The current
    /// VESA version number is 1.2."
    /// Gets or sets the VBE version (BCD format: 0x0100 = 1.0, 0x0102 = 1.2, 0x0200 = 2.0).
    /// Offset: 0x04, Size: 2 bytes (word).
    /// </summary>
    public ushort Version {
        get => UInt16[0x04];
        set => UInt16[0x04] = value;
    }

    /// <summary>
    /// "The OEMStringPtr is a far pointer to a null terminated OEM-defined string."
    /// Gets or sets the pointer to OEM string (far pointer stored as offset:segment).
    /// Offset part at 0x06, Size: 2 bytes (word).
    /// </summary>
    public ushort OemStringOffset {
        get => UInt16[0x06];
        set => UInt16[0x06] = value;
    }

    /// <summary>
    /// "The OEMStringPtr is a far pointer to a null terminated OEM-defined string."
    /// Gets or sets the pointer to OEM string (far pointer stored as offset:segment).
    /// Segment part at 0x08, Size: 2 bytes (word).
    /// </summary>
    public ushort OemStringSegment {
        get => UInt16[0x08];
        set => UInt16[0x08] = value;
    }

    /// <summary>
    /// "The Capabilities field describes what general features are supported in the
    /// video environment. The bits are defined as follows:
    /// D0 = DAC is switchable (0 = DAC is fixed width, with 6-bits per primary color,
    /// 1 = DAC width is switchable)
    /// D1-31 = Reserved"
    /// Gets or sets the capabilities flags (4 bytes).
    /// Offset: 0x0A, Size: 4 bytes (dword).
    /// </summary>
    public uint Capabilities {
        get => UInt32[0x0A];
        set => UInt32[0x0A] = value;
    }

    /// <summary>
    /// "The VideoModePtr points to a list of supported Super VGA (VESA-defined as well
    /// as OEM-specific) mode numbers. Each mode number occupies one word (16 bits).
    /// The list of mode numbers is terminated by a -1 (0FFFFh)."
    /// Gets or sets the pointer to video mode list (far pointer stored as offset:segment).
    /// Offset part at 0x0E, Size: 2 bytes (word).
    /// Points to array of ushort mode numbers, terminated by 0xFFFF.
    /// </summary>
    public ushort VideoModeListOffset {
        get => UInt16[0x0E];
        set => UInt16[0x0E] = value;
    }

    /// <summary>
    /// "The VideoModePtr points to a list of supported Super VGA (VESA-defined as well
    /// as OEM-specific) mode numbers."
    /// Gets or sets the pointer to video mode list (far pointer stored as offset:segment).
    /// Segment part at 0x10, Size: 2 bytes (word).
    /// </summary>
    public ushort VideoModeListSegment {
        get => UInt16[0x10];
        set => UInt16[0x10] = value;
    }

    /// <summary>
    /// "The TotalMemory field indicates the amount of memory installed on the VGA
    /// board. Its value represents the number of 64kb blocks of memory currently
    /// installed."
    /// Gets or sets the total memory in 64KB blocks.
    /// Offset: 0x12, Size: 2 bytes (word).
    /// </summary>
    public ushort TotalMemory {
        get => UInt16[0x12];
        set => UInt16[0x12] = value;
    }

    /// <summary>
    /// Writes the OEM string at the specified address (typically beyond the main structure).
    /// </summary>
    /// <param name="oemString">The OEM string to write.</param>
    /// <param name="offsetFromBase">Offset from base address where to write the string.</param>
    public void WriteOemString(string oemString, uint offsetFromBase) {
        string truncated = oemString.Length >= OemStringMaxLength
            ? oemString[..(OemStringMaxLength - 1)]
            : oemString;
        SetZeroTerminatedString(offsetFromBase, truncated, OemStringMaxLength);
    }

    /// <summary>
    /// "The list of mode numbers is terminated by a -1 (0FFFFh)."
    /// Writes the video mode list at the specified address.
    /// </summary>
    /// <param name="modes">Array of mode numbers (will be terminated with 0xFFFF).</param>
    /// <param name="offsetFromBase">Offset from base address where to write the mode list.</param>
    public void WriteModeList(ushort[] modes, uint offsetFromBase) {
        for (int i = 0; i < modes.Length; i++) {
            UInt16[offsetFromBase + (uint)(i * 2)] = modes[i];
        }
        UInt16[offsetFromBase + (uint)(modes.Length * 2)] = 0xFFFF;
    }
}