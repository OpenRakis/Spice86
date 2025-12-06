namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Simulates COMMAND.COM - the DOS command interpreter.
/// This is the root of the PSP (Program Segment Prefix) chain.
/// All DOS programs launched by Spice86 have COMMAND.COM as their ancestor.
/// </summary>
/// <remarks>
/// In real DOS, COMMAND.COM is loaded by the kernel and becomes the parent
/// of all user-launched programs. The PSP chain allows programs to trace
/// back to their parent processes. COMMAND.COM's PSP points to itself as
/// its own parent (marking it as the root).
/// <para>
/// This implementation is non-interactive. We don't support an interactive
/// shell since Spice86 is focused on reverse engineering specific DOS programs.
/// </para>
/// <para>
/// The initial program is launched via <see cref="DosProcessManager.LoadFile"/>,
/// which converts the Configuration.Exe path to a DOS path and calls the EXEC API
/// to simulate COMMAND.COM launching the program.
/// </para>
/// <para>
/// See https://github.com/FDOS/freecom for FreeDOS COMMAND.COM reference.
/// </para>
/// </remarks>
public class CommandCom : DosProgramSegmentPrefix {
    /// <summary>
    /// The segment where COMMAND.COM's PSP is located.
    /// </summary>
    /// <remarks>
    /// COMMAND.COM occupies a small memory area. Its PSP starts at segment 0x60
    /// (after DOS internal structures) and takes minimal space since we don't
    /// load actual COMMAND.COM code - just simulate its PSP for the chain.
    /// </remarks>
    public const ushort CommandComSegment = 0x60;

    /// <summary>
    /// Offset of the Job File Table (JFT) within the PSP structure.
    /// </summary>
    private const ushort JftOffset = 0x18;

    /// <summary>
    /// Gets the segment address of COMMAND.COM's PSP.
    /// </summary>
    public ushort PspSegment => CommandComSegment;

    /// <summary>
    /// Initializes a new instance of COMMAND.COM simulation.
    /// Creates a fully initialized PSP structure in memory at the designated segment.
    /// </summary>
    /// <param name="memory">The emulator memory.</param>
    /// <param name="loggerService">The logger service.</param>
    public CommandCom(IMemory memory, ILoggerService loggerService)
        : base(memory, MemoryUtils.ToPhysicalAddress(CommandComSegment, 0)) {
        InitializePsp();

        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information(
                "COMMAND.COM PSP initialized at segment {Segment:X4}",
                CommandComSegment);
        }
    }

    /// <summary>
    /// Initializes all PSP fields with values appropriate for COMMAND.COM.
    /// Based on FreeDOS FREECOM and MS-DOS COMMAND.COM conventions.
    /// </summary>
    private void InitializePsp() {
        // Offset 0x00-0x01: CP/M-80-like program exit sequence (INT 20h = CD 20)
        Exit[0] = 0xCD;
        Exit[1] = 0x20;

        // Offset 0x02-0x03: Segment of first byte beyond program allocation
        // For COMMAND.COM, point just past the PSP (minimal allocation)
        NextSegment = (ushort)(CommandComSegment + 0x10);

        // Offset 0x05: Far call to DOS function dispatcher (CP/M compatibility)
        // This contains a far call instruction (9Ah) but we leave it minimal
        FarCall = 0x9A;

        // Offset 0x06-0x09: CP/M service request address (obsolete, set to 0)
        CpmServiceRequestAddress = 0;

        // Offset 0x0A-0x0D: Terminate address (INT 22h)
        // For COMMAND.COM, we point to the PSP's INT 20h exit sequence at offset 0
        // This means when a child program terminates, control returns to the exit handler
        TerminateAddress = MemoryUtils.ToPhysicalAddress(CommandComSegment, 0);

        // Offset 0x0E-0x11: Break address (INT 23h)
        // For the root shell, we set this to 0 to use the default handler
        BreakAddress = 0;

        // Offset 0x12-0x15: Critical error address (INT 24h)
        // For the root shell, we set this to 0 to use the default handler
        CriticalErrorAddress = 0;

        // Offset 0x16-0x17: Parent PSP segment
        // COMMAND.COM is its own parent (marks it as the root of the PSP chain)
        ParentProgramSegmentPrefix = CommandComSegment;

        // Offset 0x18-0x2B: Job File Table (JFT) - file handle array (20 bytes)
        // Initialize all to 0xFF (unused/closed)
        for (int i = 0; i < 20; i++) {
            Files[i] = 0xFF;
        }
        // Set up standard handles (0=stdin, 1=stdout, 2=stderr, 3=stdaux, 4=stdprn)
        // These map to System File Table entries 0-4
        Files[0] = 0; // STDIN  -> SFT entry 0 (CON)
        Files[1] = 1; // STDOUT -> SFT entry 1 (CON)
        Files[2] = 2; // STDERR -> SFT entry 2 (CON)
        Files[3] = 3; // STDAUX -> SFT entry 3 (AUX)
        Files[4] = 4; // STDPRN -> SFT entry 4 (PRN)

        // Offset 0x2C-0x2D: Environment segment
        // Set to 0 for COMMAND.COM (it has the master environment or uses its own segment)
        EnvironmentTableSegment = 0;

        // Offset 0x2E-0x31: SS:SP on entry to last INT 21h call
        StackPointer = 0;

        // Offset 0x32-0x33: Maximum number of file handles (default 20)
        MaximumOpenFiles = 20;

        // Offset 0x34-0x37: Pointer to JFT (file handle table)
        // Points to the internal JFT at offset 0x18 in this PSP
        FileTableAddress = MemoryUtils.ToPhysicalAddress(CommandComSegment, JftOffset);

        // Offset 0x38-0x3B: Pointer to previous PSP (for nested command interpreters)
        // Set to 0 for the primary shell
        PreviousPspAddress = 0;

        // Offset 0x3C: Interim console flag (DOS 4+)
        InterimFlag = 0;

        // Offset 0x3D: Truename flag (DOS 4+)
        TrueNameFlag = 0;

        // Offset 0x3E-0x3F: Next PSP sharing the same file handles
        NNFlags = 0;

        // Offset 0x40: DOS version to return (major)
        DosVersionMajor = 5;

        // Offset 0x41: DOS version to return (minor)
        DosVersionMinor = 0;

        // Offset 0x50-0x52: DOS function dispatcher (INT 21h, RETF)
        // CD 21 CB = INT 21h followed by RETF
        Service[0] = 0xCD;
        Service[1] = 0x21;
        Service[2] = 0xCB;
    }
}
