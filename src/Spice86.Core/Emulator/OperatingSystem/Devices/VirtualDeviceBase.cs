namespace Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The abstract base class for all DOS virtual devices
/// </summary>
public abstract class VirtualDeviceBase : VirtualFileBase, IVirtualDevice {
    /// <summary>
    /// Create a new virtual device.
    /// </summary>
    protected VirtualDeviceBase(DosDeviceHeader dosDeviceHeader) {
        Header = dosDeviceHeader;
    }

    /// <inheritdoc />
    public DosDeviceHeader Header { get; init; }

    /// <inheritdoc />
    public uint DeviceNumber { get; set; }

    public virtual byte GetStatus(bool inputFlag) => 0;
    public virtual bool TryReadFromControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }

    public virtual bool TryWriteToControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }


    /// <inheritdoc />
    public abstract ushort Information { get; }

    /// <summary>
    /// The unique DOS device name, set by the DOS device implementer.
    /// </summary>
    /// <remarks>
    /// Limited to 8 ASCII encoded characters. A block device does not have a name, but an assigned block device letter.
    /// </remarks>
    [Range(0, 8)]
    public override string Name => Header.Name;

    public virtual string? Alias { get; init; }
}