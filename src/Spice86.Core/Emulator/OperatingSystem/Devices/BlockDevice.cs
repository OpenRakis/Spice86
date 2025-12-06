namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System.ComponentModel.DataAnnotations;
using System.IO;

/// <summary>
/// Block devices are things like hard drives, floppy drives, etc.
/// </summary>
public class BlockDevice : VirtualDeviceBase {
    /// <summary>
    /// The number of units (disks) that this block device manages.
    /// </summary>
    /// <remarks>
    /// A block device driver can manage more than one disk or floppy drive.
    /// </remarks>
    public byte UnitCount { get; }

    /// <summary>
    /// An optional 7-byte field with the signature of the block device.
    /// </summary>
    [Range(0, 7)]
    public string Signature { get; }

    public override ushort Information { get; }
    /// <inheritdoc/>
    public override bool CanRead { get; }

    /// <inheritdoc/>
    public override bool CanSeek { get; }

    /// <inheritdoc/>
    public override bool CanWrite { get; }

    /// <inheritdoc/>
    public override long Length { get; }

    /// <inheritdoc/>
    public override long Position { get; set; }

    /// <summary>
    /// Create a new virtual device.
    /// </summary>
    /// <param name="memory">The memory bus, to store the DOS device driver header..</param>
    /// <param name="baseAddress">The absolute address to the DOS device driver header.</param>
    /// <param name="attributes">The block device attributes. Not all block devices are FAT devices.</param>
    /// <param name="unitCount">The amount of disks this device has.</param>
    /// <param name="signature">An optional 7-byte field with the signature of the block device</param>
    public BlockDevice(IMemory memory, uint baseAddress, DeviceAttributes attributes,
        byte unitCount, string signature = "")
        : base(new DosDeviceHeader(memory, baseAddress) {
            Attributes = attributes,
            NextDevicePointer = new Spice86.Shared.Emulator.Memory.SegmentedAddress(0xFFFF, 0xFFFF)
        }) {
        UnitCount = unitCount;
        Signature = signature.Length > 7 ? signature[..7] : signature;
    }

    /// <inheritdoc/>
    public override void Close() {
        //NOP
    }

    /// <inheritdoc/>
    public override byte GetStatus(bool inputFlag) {
        //NOP
        return 0;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) {
        //NOP
        return 0;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) {
        //NOP
        return 0;
    }

    /// <inheritdoc/>
    public override void Flush() {
        //NOP
    }

    /// <inheritdoc/>
    public override void SetLength(long value) {
        //NOP
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) {
        //NOP
    }
}