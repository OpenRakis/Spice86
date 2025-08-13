namespace Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Utils;

using System.Diagnostics;

/// <summary>
/// Provides access to Global Descriptor Table (GDT) memory structure used in BIOS function <see cref="SystemBiosInt15Handler.CopyExtendedMemory(bool)"/>. <br/>
/// The GDT is a BIOS structure to copy data into extended memory. <br/>
/// Based on SeaBIOS handle_1587 implementation. <br/>
/// See <see href="https://fd.lod.bz/rbil/interrup/bios/1587.html#table-00498"/>
/// </summary>
/// <remarks>
/// The GDT structure for INT 15h, AH=87h as defined by SeaBIOS:
/// <code>
/// offset   use     initially  comments
/// ==============================================
/// 00..07   Unused  zeros      Null descriptor
/// 08..0f   GDT     zeros      filled in by BIOS
/// 10..17   source  ssssssss   source of data
/// 18..1f   dest    dddddddd   destination of data
/// 20..27   CS      zeros      filled in by BIOS
/// 28..2f   SS      zeros      filled in by BIOS
/// </code>
/// 
/// The source and destination are provided by the calling program as:
/// - For conventional memory (handle 0): segment:offset format in offset 06h and 0Ch
/// - For extended memory (handle != 0): linear address in offset 06h and 0Ch
/// </remarks>
[DebuggerDisplay("BaseAddress = {BaseAddress}, SourceHandle = {SourceHandle}, DestHandle = {DestinationHandle}, LinearSource = {LinearSourceAddress}, LinearDest = {LinearDestAddress}")]
public sealed class GlobalDescriptorTable : MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    /// <param name="baseAddress">The physical memory address where the GDT is located.</param>
    public GlobalDescriptorTable(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets the null descriptor (first 8 bytes, offset 0x00-0x07).
    /// This is always unused and should be zeros.
    /// </summary>
    public UInt8Array NullDescriptor => GetUInt8Array(0x00, 8);

    /// <summary>
    /// Gets the GDT descriptor area (offset 0x08-0x0F).
    /// Used by BIOS for the GDT descriptor itself.
    /// </summary>
    public UInt8Array GdtDescriptor => GetUInt8Array(0x08, 8);

    /// <summary>
    /// Gets the source descriptor area (offset 0x10-0x17).
    /// Contains the source segment descriptor for the copy operation.
    /// </summary>
    public UInt8Array SourceDescriptor => GetUInt8Array(0x10, 8);

    /// <summary>
    /// Gets the destination descriptor area (offset 0x18-0x1F).
    /// Contains the destination segment descriptor for the copy operation.
    /// </summary>
    public UInt8Array DestinationDescriptor => GetUInt8Array(0x18, 8);

    /// <summary>
    /// Gets the CS descriptor area (offset 0x20-0x27).
    /// Used by BIOS for Code Segment descriptor.
    /// </summary>
    public UInt8Array CodeSegmentDescriptor => GetUInt8Array(0x20, 8);

    /// <summary>
    /// Gets the SS descriptor area (offset 0x28-0x2F).
    /// Used by BIOS for Stack Segment descriptor.
    /// </summary>
    public UInt8Array StackSegmentDescriptor => GetUInt8Array(0x28, 8);

    /// <summary>
    /// Gets the source handle (0 for conventional memory, non-zero for extended memory).
    /// Offset 0x14 in the source descriptor.
    /// </summary>
    public ushort SourceHandle => UInt16[0x14];

    /// <summary>
    /// Gets the destination handle (0 for conventional memory, non-zero for extended memory).
    /// Offset 0x1A in the destination descriptor.
    /// </summary>
    public ushort DestinationHandle => UInt16[0x1A];

    /// <summary>
    /// Gets the source offset/address from the source descriptor.
    /// If SourceHandle is 0, this is a segmented address (segment:offset).
    /// If SourceHandle is non-zero, this is a linear address.
    /// Offset 0x16 in the source descriptor.
    /// </summary>
    public uint SourceOffsetOrAddress => UInt32[0x16];

    /// <summary>
    /// Gets the destination offset/address from the destination descriptor.
    /// If DestinationHandle is 0, this is a segmented address (segment:offset).
    /// If DestinationHandle is non-zero, this is a linear address.
    /// Offset 0x1C in the destination descriptor.
    /// </summary>
    public uint DestinationOffsetOrAddress => UInt32[0x1C];

    /// <summary>
    /// Retrieves the linear source address based on SeaBIOS logic.
    /// If the source handle is 0 (conventional memory), converts segment:offset to linear address.
    /// If the source handle is non-zero (extended memory), uses the address directly.
    /// </summary>
    /// <returns>The linear source address as an unsigned 32-bit integer.</returns>
    public uint LinearSourceAddress {
        get {
            ushort handle = SourceHandle;
            uint offsetOrAddress = SourceOffsetOrAddress;

            return DecodeAddressInternal(handle, offsetOrAddress);

        }
    }

    /// <summary>
    /// Retrieves the linear destination address based on SeaBIOS logic.
    /// If the destination handle is 0 (conventional memory), converts segment:offset to linear address.
    /// If the destination handle is non-zero (extended memory), uses the address directly.
    /// </summary>
    /// <returns>The linear destination address as an unsigned 32-bit integer.</returns>
    public uint LinearDestAddress {
        get {
            ushort handle = DestinationHandle;
            uint offsetOrAddress = DestinationOffsetOrAddress;

            return DecodeAddressInternal(handle, offsetOrAddress);
        }
    }

    private static uint DecodeAddressInternal(ushort handle, uint offsetOrAddress) {
        if (handle == 0) {
            // Conventional memory: convert segment:offset to linear address
            ushort segment = (ushort)(offsetOrAddress >> 16);
            ushort offset = (ushort)(offsetOrAddress & 0xFFFF);
            return MemoryUtils.ToPhysicalAddress(segment, offset);
        } else {
            // Extended memory: use linear address directly
            return offsetOrAddress;
        }
    }

    /// <summary>
    /// Gets the source segment limit from the source descriptor.
    /// Limit is stored in bytes 0-1 (bits 15:0) and bits 0-3 of byte 6 (bits 19:16).
    /// </summary>
    /// <returns>The source segment limit</returns>
    public uint SourceSegmentLimit => GetLimitFromDescriptor(SourceDescriptor);

    /// <summary>
    /// Gets the destination segment limit from the destination descriptor.
    /// Limit is stored in bytes 0-1 (bits 15:0) and bits 0-3 of byte 6 (bits 19:16).
    /// </summary>
    /// <returns>The destination segment limit</returns>
    public uint DestinationSegmentLimit => GetLimitFromDescriptor(DestinationDescriptor);

    /// <summary>
    /// Gets the limit from a GDT descriptor.
    /// Limit is stored in bytes 0-1 (bits 15:0) and bits 0-3 of byte 6 (bits 19:16).
    /// </summary>
    /// <param name="descriptor">The 8-byte descriptor array</param>
    /// <returns>The 20-bit limit value</returns>
    private static uint GetLimitFromDescriptor(UInt8Array descriptor) {
        uint limit = (uint)(descriptor[0] |                    // Limit 7:0
                           descriptor[1] << 8 |               // Limit 15:8
                           (descriptor[6] & 0x0F) << 16);     // Limit 19:16
        return limit;
    }

    /// <summary>
    /// Gets the access rights byte from a descriptor.
    /// </summary>
    /// <param name="descriptor">The 8-byte descriptor array</param>
    /// <returns>The access rights byte</returns>
    private static byte GetAccessRightsFromDescriptor(UInt8Array descriptor) {
        return descriptor[5];
    }

    /// <summary>
    /// Gets the source segment access rights.
    /// Typically set to 93h for read/write data segment access.
    /// </summary>
    /// <returns>The source access rights byte</returns>
    public byte SourceAccessRights => GetAccessRightsFromDescriptor(SourceDescriptor);

    /// <summary>
    /// Gets the destination segment access rights.
    /// Typically set to 93h for read/write data segment access.
    /// </summary>
    /// <returns>The destination access rights byte</returns>
    public byte DestinationAccessRights => GetAccessRightsFromDescriptor(DestinationDescriptor);
}
