namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Memory;

using System.Text;

/// <summary>
/// Represents the DOS device header structure stored in memory.
/// This structure is present at the beginning of every DOS device driver.
/// </summary>
/// <remarks>
/// DOS Device Header Layout (18 bytes total):
/// <code>
/// Offset  Size  Description
/// 0x00    4     Pointer to next device header (segment:offset, FFFF:FFFF if last)
/// 0x04    2     Device attributes word
/// 0x06    2     Strategy routine entry point (offset)
/// 0x08    2     Interrupt routine entry point (offset)
/// 0x0A    8     Device name (character devices) OR
///               1 byte unit count + 7 bytes (block devices)
/// </code>
/// References:
/// - MS-DOS 4.0 source code (DEVSYM.ASM, MSDOS.ASM)
/// - Adams - Writing DOS Device Drivers in C (1990), Chapter 4
/// </remarks>
public class DosDeviceHeader : MemoryBasedDataStructure {
    /// <summary>
    /// Total length of the DOS device header structure in bytes.
    /// </summary>
    public const int HeaderLength = 18;

    /// <summary>
    /// Maximum length of device name for character devices.
    /// </summary>
    public const int DeviceNameLength = 8;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosDeviceHeader"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    /// <param name="baseAddress">The base address of the structure in memory.</param>
    /// <remarks>
    /// The NextDevicePointer should be explicitly initialized by the caller when creating new headers.
    /// This avoids virtual calls in the constructor and allows proper initialization for existing headers.
    /// </remarks>
    public DosDeviceHeader(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the pointer to the next device header in the linked list (offset 0x00, 4 bytes).
    /// </summary>
    /// <remarks>
    /// Contains segment:offset of the next driver's header.
    /// Set to 0xFFFF:0xFFFF for the last device in the chain.
    /// </remarks>
    public SegmentedAddress NextDevicePointer {
        get => SegmentedAddress16[0x00];
        set => SegmentedAddress16[0x00] = value;
    }

    /// <summary>
    /// Optional reference to the next device header object.
    /// This is for internal bookkeeping and is not stored in memory.
    /// </summary>
    public DosDeviceHeader? NextDeviceHeader { get; set; }

    /// <summary>
    /// Gets or sets the device attributes word (offset 0x04, 2 bytes).
    /// </summary>
    /// <remarks>
    /// Defines device characteristics such as:
    /// - Bit 15 (0x8000): Character device (vs block device)
    /// - Bit 14 (0x4000): IOCTL supported
    /// - Bit 13 (0x2000): Non-IBM block format (block devices only)
    /// - Bit 11 (0x0800): Open/Close/Removable media supported
    /// - Bit 6  (0x0040): Generic IOCTL supported
    /// - Bits 0-5: For character devices, special device bits (stdin, stdout, clock, etc.)
    /// See <see cref="DeviceAttributes"/> enum for details.
    /// </remarks>
    public DeviceAttributes Attributes {
        get => (DeviceAttributes)UInt16[0x04];
        set => UInt16[0x04] = (ushort)value;
    }

    /// <summary>
    /// Gets or sets the offset of the strategy routine entry point (offset 0x06, 2 bytes).
    /// </summary>
    /// <remarks>
    /// DOS calls this routine first when it needs the device to perform an operation.
    /// The routine receives a pointer to a request packet in ES:BX.
    /// For internal emulated devices, this is typically set to 0.
    /// </remarks>
    public ushort StrategyEntryPoint {
        get => UInt16[0x06];
        set => UInt16[0x06] = value;
    }

    /// <summary>
    /// Gets or sets the offset of the interrupt routine entry point (offset 0x08, 2 bytes).
    /// </summary>
    /// <remarks>
    /// DOS calls this routine immediately after the strategy routine.
    /// This routine performs the actual device operation.
    /// For internal emulated devices, this is typically set to 0.
    /// </remarks>
    public ushort InterruptEntryPoint {
        get => UInt16[0x08];
        set => UInt16[0x08] = value;
    }

    /// <summary>
    /// Gets or sets the 8-character device name for character devices (offset 0x0A, 8 bytes).
    /// </summary>
    /// <remarks>
    /// For character devices: 8-byte ASCII name, padded with spaces (e.g., "CON     ").
    /// For block devices: first byte is unit count, remaining 7 bytes are unused/signature.
    /// Device names are case-insensitive and typically uppercase.
    /// </remarks>
    public string Name {
        get {
            UInt8Array array = GetUInt8Array(0x0A, DeviceNameLength);
            byte[] bytes = new byte[DeviceNameLength];
            for (int i = 0; i < DeviceNameLength; i++) {
                bytes[i] = array[i];
            }
            return Encoding.ASCII.GetString(bytes).TrimEnd();
        }
        set {
            string paddedName = (value ?? string.Empty).PadRight(DeviceNameLength);
            if (paddedName.Length > DeviceNameLength) {
                paddedName = paddedName[..DeviceNameLength];
            }
            byte[] bytes = Encoding.ASCII.GetBytes(paddedName);
            UInt8Array array = GetUInt8Array(0x0A, DeviceNameLength);
            for (int i = 0; i < DeviceNameLength; i++) {
                array[i] = bytes[i];
            }
        }
    }

    /// <summary>
    /// For block devices: Gets or sets the number of units (drives) this device handles (offset 0x0A, 1 byte).
    /// </summary>
    /// <remarks>
    /// Only valid for block devices (bit 15 of Attributes clear).
    /// For character devices, use the Name property instead.
    /// </remarks>
    public byte UnitCount {
        get => UInt8[0x0A];
        set => UInt8[0x0A] = value;
    }
}