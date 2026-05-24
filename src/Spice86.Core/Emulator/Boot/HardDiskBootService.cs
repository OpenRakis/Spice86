namespace Spice86.Core.Emulator.Boot;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Storage.FileSystem.Partitions;
using Spice86.Shared.Interfaces;

using System;
using System.IO;

/// <summary>
/// Bootstraps the CPU from an MBR-partitioned hard-disk image, mirroring the
/// IBM PC BIOS hard-disk boot convention used by DOSBox Staging's <c>boot.cpp</c>.
/// <para>
/// The service inspects the MBR (sector 0) of the supplied image, selects the
/// partition flagged as bootable (boot indicator 0x80) or falls back to the
/// first non-empty partition, copies that partition's first sector to physical
/// 0x7C00, and configures the CPU registers per the BIOS bootstrap convention
/// so the emulator resumes execution at <c>0000:7C00</c> with <c>DL</c> set to
/// the BIOS hard-disk drive number (typically 0x80).
/// </para>
/// </summary>
public sealed class HardDiskBootService
{
    /// <summary>Size of a hard-disk sector in bytes.</summary>
    public const int SectorSize = 512;

    /// <summary>Offset within segment 0 where the BIOS loads the boot sector.</summary>
    public const ushort BootSectorLoadOffset = 0x7C00;

    private const uint BootSectorLoadAddress = BootSectorLoadOffset;

    private readonly IMemory _memory;
    private readonly State _state;
    private readonly ILoggerService _loggerService;

    /// <summary>Creates a new <see cref="HardDiskBootService"/>.</summary>
    /// <param name="memory">Emulated memory used to stage the partition boot sector.</param>
    /// <param name="state">CPU state to configure for the BIOS bootstrap protocol.</param>
    /// <param name="loggerService">Logger used to emit informational boot traces.</param>
    public HardDiskBootService(IMemory memory, State state, ILoggerService loggerService)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(loggerService);
        _memory = memory;
        _state = state;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Validates an HDD image's MBR, selects a partition to boot, copies the
    /// partition's first 512 bytes to physical 0x7C00, and configures the CPU
    /// registers per the BIOS bootstrap convention.
    /// <para>
    /// Registers set: <c>CS:IP=0000:7C00</c>, <c>SS:SP=0000:7C00</c>,
    /// <c>DS=ES=0</c>, <c>AX=0</c>, <c>BX=0x7C00</c>, <c>CX=1</c>,
    /// <c>DX=0</c>, <c>DL</c>=<paramref name="driveNumber"/>,
    /// <c>SI=DI=BP=0</c>, IF set.
    /// </para>
    /// </summary>
    /// <param name="imageData">The full hard-disk image bytes.</param>
    /// <param name="driveNumber">BIOS drive number (typically 0x80 for first HDD).</param>
    /// <param name="imagePathForLogging">Optional path used in info logs.</param>
    /// <returns><c>true</c> when the partition boot sector was loaded and the
    /// CPU prepared; <c>false</c> when the image is missing, the MBR is
    /// invalid, no usable partition exists, or the selected partition's boot
    /// sector lacks the 0xAA55 signature.</returns>
    public bool TryBootFromHardDiskImage(byte[]? imageData, byte driveNumber, string? imagePathForLogging)
    {
        if (imageData is null || imageData.Length < SectorSize)
        {
            return false;
        }
        if (imageData[SectorSize - 2] != 0x55 || imageData[SectorSize - 1] != 0xAA)
        {
            return false;
        }

        MasterBootRecord mbr;
        try
        {
            mbr = MbrCodec.Parse(imageData.AsSpan(0, SectorSize));
        }
        catch (InvalidDataException)
        {
            return false;
        }

        PartitionTableEntry? partition = mbr.FindBootablePartition() ?? mbr.FindFirstNonEmptyPartition();
        if (partition is null)
        {
            return false;
        }

        long partitionByteOffset = (long)partition.LbaStart * SectorSize;
        if (partitionByteOffset < 0 || partitionByteOffset + SectorSize > imageData.Length)
        {
            return false;
        }

        int partitionOffset = (int)partitionByteOffset;
        if (imageData[partitionOffset + SectorSize - 2] != 0x55 || imageData[partitionOffset + SectorSize - 1] != 0xAA)
        {
            return false;
        }

        byte[] sectorBytes = new byte[SectorSize];
        Array.Copy(imageData, partitionOffset, sectorBytes, 0, SectorSize);
        _memory.LoadData(BootSectorLoadAddress, sectorBytes, SectorSize);
        SetupCpuRegistersForBoot(driveNumber);

        if (_loggerService.IsEnabled(LogEventLevel.Information))
        {
            _loggerService.Information(
                "BOOT: loaded {Bytes} bytes from HDD partition LBA={Lba} ('{Path}') at 0000:7C00, DL={DL:X2}",
                SectorSize, partition.LbaStart, imagePathForLogging ?? string.Empty, _state.DL);
        }
        return true;
    }

    private void SetupCpuRegistersForBoot(byte driveNumber)
    {
        _state.CS = 0;
        _state.IP = BootSectorLoadOffset;
        _state.DS = 0;
        _state.ES = 0;
        _state.SS = 0;
        _state.SP = BootSectorLoadOffset;
        _state.AX = 0;
        _state.BX = BootSectorLoadOffset;
        _state.CX = 1;
        _state.DX = 0;
        _state.DL = driveNumber;
        _state.SI = 0;
        _state.DI = 0;
        _state.BP = 0;
        _state.InterruptFlag = true;
    }
}
