namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

using System.IO;

/// <summary>
/// Block devices are things like hard drives, floppy drives, etc.
/// </summary>
public class BlockDevice : VirtualDeviceBase {
    /// <summary>
    /// The number of units (disks) that this device has.
    /// </summary>
    public byte UnitCount { get; }
    /// <summary>
    /// An optional 7-byte field with the signature of the device.
    /// </summary>
    public string Signature { get; }

    /// <summary>
    /// Device name, also serves for file-based device access.
    /// </summary>
    public override string Name { get; set; }
    
    /// <summary>
    /// Gets the DOS Device characteristics. Largely undocumented, and device-specific.
    /// </summary>
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
    /// <param name="loggerService">The logging implementation.</param>
    /// <param name="name">The name or label of the block device.</param>
    /// <param name="attributes">The device attributes.</param>
    /// <param name="unitCount">The amount of disks this device has.</param>
    /// <param name="signature">The string identifier.</param>
    /// <param name="strategy">Optional entrypoint for the strategy routine.</param>
    /// <param name="interrupt">Optional entrypoint for the interrupt routine.</param>
    public BlockDevice(ILoggerService loggerService, string name,
        DeviceAttributes attributes, byte unitCount,
        string signature = "", ushort strategy = 0, ushort interrupt = 0)
        : base(loggerService, attributes, strategy, interrupt) {
        Attributes &= ~DeviceAttributes.Character;
        UnitCount = unitCount;
        Name = name;
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