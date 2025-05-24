namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

/// <summary>
/// Block devices are things like hard drives, floppy drives, etc.
/// </summary>
internal class BlockDevice : VirtualDeviceBase {
    /// <summary>
    /// The number of units (disks) that this device has.
    /// </summary>
    public byte UnitCount { get; }
    /// <summary>
    /// An optional 7-byte field with the signature of the device.
    /// </summary>
    public string Signature { get; }
    public override string Name { get; set; }
    public override ushort Information { get; }
    public override bool CanRead { get; }
    public override bool CanSeek { get; }
    public override bool CanWrite { get; }
    public override long Length { get; }
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

    public override void Close() {
        throw new NotImplementedException();
    }

    public override byte GetStatus(bool inputFlag) {
        throw new NotImplementedException();
    }

    public override int Read(byte[] buffer, int offset, int count) {
        throw new NotImplementedException();
    }

    public override long Seek(long offset, SeekOrigin origin) {
        throw new NotImplementedException();
    }

    public override bool TryReadFromControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        throw new NotImplementedException();
    }

    public override bool TryWriteToControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        throw new NotImplementedException();
    }

    public override void Flush() {
        throw new NotImplementedException();
    }

    public override void SetLength(long value) {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count) {
        throw new NotImplementedException();
    }
}