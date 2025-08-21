﻿namespace Spice86.Core.Emulator.LoadableFile.Dos;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Representation of an EXE file as a MemoryBasedDataStructure
/// </summary>
public class DosExeFile : MemoryBasedDataStructure {
    /// <summary>
    /// Minimal size an exe file can have to be parseable.
    /// </summary>
    public const int MinExeSize = 0x1C;

    /// <summary>
    /// Creates a new instance of the ExeFile class.
    /// </summary>
    /// <param name="byteReaderWriter">The class that allows writing and reading at specific addresses in memory.</param>
    public DosExeFile(IByteReaderWriter byteReaderWriter) : base(byteReaderWriter, 0) {
    }

    /// <summary>
    /// Signature of the executable file.
    /// </summary>
    public string Signature => GetZeroTerminatedString(0, 2);

    /// <summary>
    /// Number of bytes in the final page (all previous pages have 512 Bytes).
    /// If 0, all the 512 Bytes of the final page are also filled.
    /// </summary>
    public ushort LenFinalPage => UInt16[0x02];

    /// <summary>
    /// Number of pages in the executable file.
    /// </summary>
    public ushort Pages => UInt16[0x04];

    /// <summary>
    /// Number of relocation table entries in the executable file.
    /// </summary>
    public ushort RelocItems => UInt16[0x06];

    /// <summary>
    /// Size of the header in paragraphs.
    /// </summary>
    public ushort HeaderSizeInParagraphs => UInt16[0x08];

    /// <summary>
    /// Minimum number of extra paragraphs needed by the program.
    /// </summary>
    public ushort MinAlloc => UInt16[0x0A];

    /// <summary>
    /// Maximum number of extra paragraphs needed by the program.
    /// </summary>
    public ushort MaxAlloc => UInt16[0x0C];

    /// <summary>
    /// Initial (relative) SS value of the program.
    /// </summary>
    public ushort InitSS => UInt16[0x0E];

    /// <summary>
    /// Initial SP value of the program.
    /// </summary>
    public ushort InitSP => UInt16[0x10];

    /// <summary>
    /// Checksum of the executable file.
    /// </summary>
    public ushort CheckSum => UInt16[0x12];

    /// <summary>
    /// Initial IP value of the program.
    /// </summary>
    public ushort InitIP => UInt16[0x14];

    /// <summary>
    /// Initial (relative) CS value of the program.
    /// </summary>
    public ushort InitCS => UInt16[0x16];

    /// <summary>
    /// Offset of the relocation table in the executable file.
    /// </summary>
    public ushort RelocTableOffset => UInt16[0x18];

    /// <summary>
    /// Overlay number of the program.
    /// </summary>
    public ushort Overlay => UInt16[0x1A];

    /// <summary>
    /// Enumeration of relocation table entries in the executable file.
    /// </summary>
    public IEnumerable<SegmentedAddress> RelocationTable => GetSegmentedAddressArray(RelocTableOffset, RelocItems);

    /// <summary>
    /// Program code in the executable file.
    /// </summary>
    public byte[] ProgramImage {
        get {
            byte[] res = new byte[ProgramSize];
            for (int i = 0; i < ProgramSize; i++) {
                res[i] = UInt8[(uint)(i + HeaderSizeInBytes)];
            }
            return res;
        }
    }

    /// <summary>
    /// Size of the header in bytes.
    /// </summary>
    public uint HeaderSizeInBytes => (uint)(HeaderSizeInParagraphs * 16);

    /// <summary>
    /// Size of the program code in the executable file in bytes.
    /// </summary>
    public uint ProgramSize {
        get {
            uint declaredTotalSize = LenFinalPage == 0
                ? Pages * 512U
                : (uint)((Pages == 0 ? 0 : Pages - 1) * 512) + LenFinalPage;

            uint headerSize = HeaderSizeInBytes;
            uint fileLength = ByteReaderWriter.Length;
            uint declaredImageSize = declaredTotalSize > headerSize ? declaredTotalSize - headerSize : 0;
            uint availableAfterHeader = fileLength > headerSize ? fileLength - headerSize : 0;
            return declaredImageSize <= availableAfterHeader ? declaredImageSize : availableAfterHeader;
        }
    }

    /// <summary>
    /// Number of paragraphs that are need to load the program code in the executable file.
    /// </summary>
    public ushort ProgramSizeInParagraphsPerHeader => (ushort)((Pages << 5) - HeaderSizeInParagraphs);

    /// <summary>
    /// True when represented EXE is valid.
    /// Valid means starts with "MZ" and declared header is at least as big as the whole exe.
    /// </summary>
    public bool IsValid => Signature is "MZ" or "ZM" && HeaderSizeInBytes <= ByteReaderWriter.Length;
}