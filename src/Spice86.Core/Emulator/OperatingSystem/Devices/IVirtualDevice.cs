namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The interface for all DOS virtual devices.
/// </summary>
public interface IVirtualDevice : IVirtualFile {

    /// <summary>
    /// Gets the device status
    /// </summary>
    /// <param name="inputFlag">Whether it's for input data or output data</param>
    /// <returns>The DOS device status in a <see langword="byte"/>.</returns>
    public byte GetStatus(bool inputFlag);

    /// <summary>
    /// Tries to read data from the control channel.
    /// </summary>
    /// <remarks>
    /// Not all devices support IOCTL.
    /// </remarks>
    /// <returns>Whether the read operation was successful.</returns>
    public bool TryReadFromControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode);

    /// <summary>
    /// Tries to read data from the control channel.
    /// </summary>
    /// <remarks>
    /// Not all devices support IOCTL.
    /// </remarks>
    /// <returns>Whether the write operation was successful.</returns>
    public bool TryWriteToControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode);

    /// <summary>
    /// The index of the device in the DOS device list.
    /// </summary>
    public uint DeviceNumber { get; set; }

    /// <summary>
    /// The corresponding DOS device header for this device.
    /// </summary>
    public DosDeviceHeader Header { get; init; }

    /// <summary>
    /// Gets the DOS Device characteristics. Largely undocumented, and device-specific.
    /// </summary>
    public ushort Information { get; }
}