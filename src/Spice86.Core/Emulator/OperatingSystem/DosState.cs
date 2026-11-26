namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Devices;

using System.Collections.Immutable;

/// <summary>
///     State of the DOS kernel that Spice86 keeps in C# objects, as captured by <see cref="Dos.CaptureState" />.
/// </summary>
/// <remarks>
///     DOS state that lives in emulated memory (the current PSP, the memory control block chain, the SDA, the
///     list of lists, the job file tables of the PSPs) is not part of it. Neither are the open file table, the
///     mounted drives and the XMS and EMS drivers.
/// </remarks>
/// <param name="DiskTransferAreaSegment">Segment of the disk transfer area address (INT 21h AH=1Ah and 2Fh).</param>
/// <param name="DiskTransferAreaOffset">Offset of the disk transfer area address.</param>
/// <param name="CurrentDrive">Letter of the default drive (INT 21h AH=0Eh and 19h).</param>
/// <param name="CurrentDirectories">Current directory of each mounted drive (INT 21h AH=3Bh and 47h).</param>
/// <param name="AllocationStrategy">Memory allocation strategy (INT 21h AX=5800h and 5801h).</param>
/// <param name="LastChildReturnCode">
///     Return code of the last child process, termination type in the high byte (INT 21h AH=4Dh).
/// </param>
/// <param name="IsCtrlCCheckEnabled">Ctrl-C check flag (INT 21h AX=3300h and 3301h).</param>
/// <param name="ActiveCodePage">Active code page (INT 21h AX=6601h and 6602h).</param>
/// <param name="FileSearches">Find first / find next searches that have not ended (INT 21h AH=4Eh and 4Fh).</param>
/// <param name="Console">State of the CON device.</param>
public sealed record DosState(
    ushort DiskTransferAreaSegment,
    ushort DiskTransferAreaOffset,
    char CurrentDrive,
    ImmutableArray<DosDriveDirectoryState> CurrentDirectories,
    ushort AllocationStrategy,
    ushort LastChildReturnCode,
    bool IsCtrlCCheckEnabled,
    ushort ActiveCodePage,
    ImmutableArray<DosFileSearchState> FileSearches,
    ConsoleDeviceState Console);
