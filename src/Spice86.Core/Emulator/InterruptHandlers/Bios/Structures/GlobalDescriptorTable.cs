namespace Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

/// <summary>
/// Provides access to Global Descriptor Table (GDT) memory structure used in BIOS function <see cref="SystemBiosInt15Handler.CopyExtendedMemory(bool)"/>. <br/>
/// The GDT is a BIOS structure to copy data into extended memory. <br/>
/// See <see href="https://fd.lod.bz/rbil/interrup/bios/1587.html#table-00498"/>
/// </summary>
public sealed class GlobalDescriptorTable : MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    /// <param name="baseAddress">The physical memory address where the GDT is located.</param>
    public GlobalDescriptorTable(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets the BIOS reserved area (first 16 bytes).
    /// Used by BIOS for internal operations.
    /// </summary>
    public UInt8Array BiosReserved => GetUInt8Array(0x0, 16);

    /// <summary>
    /// Gets or sets the source segment length in bytes.
    /// Must be at least (2 * CX - 1) where CX is the number of words to copy.
    /// </summary>
    public ushort SourceSegmentLength { get => UInt16[0x10]; set => UInt16[0x10] = value; }

    /// <summary>
    /// Retrieves the linear source address associated as an uint.
    /// </summary>
    /// <returns>The linear source address as an unsigned 32-bit integer.</returns>
    public uint GetLinearSourceAddress() {
        return GetLinearAddressInternal(SourceLinearAddress);
    }

    /// <summary>
    /// Retrieves the linear destination address as an uint.
    /// </summary>
    /// <returns>The linear destination address as an unsigned 32-bit integer.</returns>
    public uint GetLinearDestAddress() {
        return GetLinearAddressInternal(DestinationLinearAddress);
    }

    private uint GetLinearAddressInternal(UInt8Array addrBytes) {
        // Little-endian: low, mid, high
        return (uint)(addrBytes[0] | addrBytes[1] << 8 | addrBytes[2] << 16);
    }

    /// <summary>
    /// Gets or sets the 24-bit linear source address.
    /// Stored in little-endian format (low byte first).
    /// </summary>
    public UInt8Array SourceLinearAddress => GetUInt8Array(0x12, 3);

    /// <summary>
    /// Gets or sets the source segment access rights.
    /// Typically set to 93h for read/write data segment access.
    /// </summary>
    public byte SourceAccessRights { get => UInt8[0x15]; set => UInt8[0x15] = value; }

    /// <summary>
    /// Gets or sets the extended source attributes.
    /// For 286: Should be zero
    /// For 386+: Contains extended access rights and high byte of source address.
    /// </summary>
    public ushort SourceExtendedAttributes { get => UInt16[0x16]; set => UInt16[0x16] = value; }

    /// <summary>
    /// Gets or sets the destination segment length in bytes.
    /// Must be at least (2 * CX - 1) where CX is the number of words to copy.
    /// </summary>
    public ushort DestinationSegmentLength { get => UInt16[0x18]; set => UInt16[0x18] = value; }

    /// <summary>
    /// Gets or sets the 24-bit linear destination address.
    /// Stored in little-endian format (low byte first).
    /// </summary>
    public UInt8Array DestinationLinearAddress => GetUInt8Array(0x1A, 3);

    /// <summary>
    /// Gets or sets the destination segment access rights.
    /// Typically set to 93h for read/write data segment access.
    /// </summary>
    public byte DestinationAccessRights { get => UInt8[0x1D]; set => UInt8[0x1D] = value; }

    /// <summary>
    /// Gets or sets the extended destination attributes.
    /// For 286: Should be zero
    /// For 386+: Contains extended access rights and high byte of destination address.
    /// </summary>
    public ushort DestinationExtendedAttributes { get => UInt16[0x1E]; set => UInt16[0x1E] = value; }

    /// <summary>
    /// Gets the BIOS CS/SS descriptor area.
    /// Used by BIOS to build Code Segment and Stack Segment descriptors.
    /// </summary>
    public UInt8Array BiosDescriptorArea => GetUInt8Array(0x20, 16);
}
