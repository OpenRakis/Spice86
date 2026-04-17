namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Structures;

/// <summary>
/// Abstract base class for virtual drive device implementations supporting IOCTL operations.
/// This provides common structure for block devices (hard drives, CD-ROM, floppy).
/// Future subclasses will specialize for CD-ROM (MSCDEX) or floppy media.
/// </summary>
internal abstract class VirtualDriveDeviceBase : VirtualDeviceBase, IVirtualDriveDevice {
    private readonly VirtualDriveInfo _virtualDriveInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualDriveDeviceBase"/> class.
    /// </summary>
    /// <param name="header">DOS device header for this device.</param>
    /// <param name="virtualDriveInfo">Virtual drive descriptor with identity and properties.</param>
    protected VirtualDriveDeviceBase(DosDeviceHeader header, VirtualDriveInfo virtualDriveInfo)
        : base(header) {
        _virtualDriveInfo = virtualDriveInfo;
    }

    /// <summary>
    /// Gets the virtual drive descriptor associated with this device.
    /// </summary>
    public VirtualDriveInfo VirtualDriveInfo => _virtualDriveInfo;

    /// <inheritdoc />
    public bool IsRemovable => _virtualDriveInfo.IsRemovable;

    /// <inheritdoc />
    public byte PhysicalDriveNumber => _virtualDriveInfo.PhysicalDriveNumber;

    /// <inheritdoc />
    public VirtualDriveMediaType MediaType => _virtualDriveInfo.MediaType;

    /// <inheritdoc />
    public abstract bool IsMediaPresent();

    /// <inheritdoc />
    public abstract bool TryReadSector(uint lba, uint buffer, uint bufferSize, out uint bytesRead);

    /// <inheritdoc />
    public abstract uint GetDeviceParameters();

    /// <inheritdoc />
    public abstract bool TryHandleIoctl(byte command, uint parameterBuffer);
}
