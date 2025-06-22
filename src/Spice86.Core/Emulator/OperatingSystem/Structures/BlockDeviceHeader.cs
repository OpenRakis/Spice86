namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;

using System;
using System.ComponentModel.DataAnnotations;

public class BlockDeviceHeader : DosDeviceHeader {
    public BlockDeviceHeader(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// The number of units (disks) that this block device manages.
    /// </summary>
    /// <remarks>
    /// A single block device driver can manage more than one disk or floppy drive.
    /// </remarks>
    public byte UnitCount {
        get => UInt8[0x19];
        set => UInt8[0x19] = value;
    }

    /// <summary>
    /// An optional 7-byte field with the signature of the block device.
    /// </summary>
    [Range(0, 7)]
    public string Signature {
        get => GetZeroTerminatedString(0x20, 7);
        set => SetZeroTerminatedString(0x20, value, 7);
    }
}
