namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Represents the DOS device header structure stored in memory.
/// </summary>
public class DosDeviceHeader : MemoryBasedDataStructure {
    public const int HeaderLength = 18;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosDeviceHeader"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    /// <param name="baseAddress">The base address of the structure in memory.</param>
    public DosDeviceHeader(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// The absolute address of the next device header in the linked list.
    /// </summary>
    /// <remarks>
    /// Contains 0xFFFF, 0xFFFF if there is no next device.
    /// </remarks>
    public SegmentedAddress NextDevicePointer { get; set; } = new(0xFFFF, 0xFFFF);

    public DosDeviceHeader? NextDeviceHeader { get; set; }

    /// <summary>
    /// The device attributes.
    /// <see href="https://github.com/microsoft/MS-DOS/blob/master/v2.0/bin/DEVDRIV.DOC#L125"/>
    /// </summary>
    public DeviceAttributes Attributes { get; init; }

    /// <summary>
    /// This is the entrypoint for the strategy routine.
    /// DOS will give this routine a Device Request Header when it wants the device to do something.
    /// </summary>
    public ushort StrategyEntryPoint { get; init; }

    /// <summary>
    /// This is the entrypoint for the interrupt routine.
    /// DOS will call this routine immediately after calling the strategy endpoint.
    /// </summary>
    public ushort InterruptEntryPoint { get; init; }

    /// <summary>
    /// The unique DOS device name, set by the DOS device implementer.
    /// </summary>
    /// <remarks>
    /// Limited to 8 ASCII encoded characters.
    /// </remarks>
    [Range(0, 8)]
    public string Name { get; init; } = "";

}