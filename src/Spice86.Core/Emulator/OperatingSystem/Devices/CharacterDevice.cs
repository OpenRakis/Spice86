namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

using System;
using System.IO;

/// <summary>
/// Character devices are things like the console, the printer, the clock, etc.
/// </summary>
public class CharacterDevice : VirtualDeviceBase {
    /// <summary>
    /// The logging service.
    /// </summary>
    protected readonly ILoggerService Logger;

    /// <summary>
    /// Create a new character device.
    /// </summary>
    /// <param name="attributes">The device attributes.</param>
    /// <param name="name">The name of the device.</param>
    /// <param name="loggerService">The logging service.</param>
    /// <param name="strategy">Optional entrypoint for the strategy routine.</param>
    /// <param name="interrupt">Optional entrypoint for the interrupt routine.</param>
    public CharacterDevice(DeviceAttributes attributes, string name,
        ILoggerService loggerService, ushort strategy = 0, ushort interrupt = 0)
        : base(attributes, strategy, interrupt) {
        Attributes |= DeviceAttributes.Character;
        Name = name.Length > 8 ? name[..8] : name;
        Logger = loggerService;
    }

    public override string Name { get; set; }
    public override ushort Information { get; }
    public override bool CanRead { get; }
    public override bool CanSeek { get; }
    public override bool CanWrite { get; }
    public override long Length { get; }
    public override long Position { get; set; }

    public override void Close() {
        throw new NotImplementedException();
    }

    public override void Flush() {
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

    public override void SetLength(long value) {
        throw new NotImplementedException();
    }

    public override bool TryReadFromControlChannel(uint address, ushort size, out ushort? returnCode) {
        throw new NotImplementedException();
    }

    public override bool TryWriteToControlChannel(uint address, ushort size, out ushort? returnCode) {
        throw new NotImplementedException();
    }

    public override void Write(ReadOnlySpan<byte> buffer) {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count) {
        throw new NotImplementedException();
    }
}