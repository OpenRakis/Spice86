namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Keeps track of the DOS PSP chains in memory.
/// </summary>
/// <remarks>
/// This class is responsible for keeping track of the initial <see cref="DosProgramSegmentPrefix"/>
/// location in memory and maintaining a list of the PSP for each program loaded thereafter.
/// <br/><br/>
/// It does <strong>not</strong> actually setup the PSP itself. That is closely tied to loading and
/// executing a program, so <see cref="DosProcessManager"/> is responsible for setting it up
/// instead.
/// <br/><br/>
/// This class is closely tied to <see cref="DosProcessManager"/>. It is effectively a process
/// management implementation detail that is split out as its own class because...
/// <ul>
/// <li>it is a logically independent unit that can be reasonably tested on its own,</li>
/// <li>it eliminates the need for the configuration in <c>DosProcessManager</c>, and</li>
/// <li>it allows both the memory and process managers to use it without creating a circular dependency.</li>
/// </ul>
/// </remarks>
public class DosProgramSegmentPrefixTracker {
    /// <summary>
    /// The memory segment where the <em>first</em> program will be loaded.
    /// </summary>
    /// <remarks>
    /// Technically this class doesn't <em>really</em> need to know where any program is loaded
    /// because it isn't responsible for loading them. However, the initial PSP is relative to the
    /// initial entry point that we get from the configuration, so we keep track of it exactly as
    /// configured as our reference point.
    /// </remarks>
    private readonly ushort _initialProgramEntryPointSegment;

    private readonly IMemory _memory;
    private readonly ILoggerService _loggerService;
    private readonly DosSwappableDataArea _dosSwappableDataArea;


    /// <summary>
    /// The PSPs for each program that is currently loaded.
    /// </summary>
    private readonly List<DosProgramSegmentPrefix> _loadedPsps;

    public DosProgramSegmentPrefixTracker(Configuration configuration, IMemory memory,
        DosSwappableDataArea dosSwappableDataArea,
        ILoggerService loggerService) {
        _dosSwappableDataArea = dosSwappableDataArea;
        _initialProgramEntryPointSegment = configuration.ProgramEntryPointSegment;
        _memory = memory;
        _loggerService = loggerService;
        _loadedPsps = new();

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Initial program entry point at segment: {EntryPointSegment}",
                ConvertUtils.ToHex16(_initialProgramEntryPointSegment));
        }

        // Warn if the configured segment is too low and could overwrite BIOS data or interrupt vectors
        // The BIOS data area ends at segment 0x50, and the MCB starts at InitialPspSegment - 1
        ushort pspSegment = InitialPspSegment;
        if (pspSegment <= MemoryMap.FreeMemoryStartSegment && _loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning(
                "Initial PSP segment {PspSegment:X4} is at or below FreeMemoryStartSegment {FreeMemory:X4}. " +
                "This may overwrite BIOS data or interrupt vectors!",
                pspSegment, MemoryMap.FreeMemoryStartSegment);
        }
    }

    /// <summary>
    /// PSP segment for the first program loaded into memory.
    /// </summary>
    /// <remarks>
    /// Typically you don't want to use the <em>initial</em> PSP segment. You want to get the
    /// <em>current</em> PSP segment for the program that is loaded into memory.
    /// </remarks>
    public ushort InitialPspSegment { get => (ushort)(_initialProgramEntryPointSegment - 0x10); }

    /// <summary>
    /// Number of PSPs that are currently loaded, which may be zero if the first program hasn't been
    /// loaded yet.
    /// </summary>
    public int PspCount { get => _loadedPsps.Count; }

    /// <summary>
    /// Gets the PSP for the program that is currently loaded.
    /// </summary>
    /// <returns>
    /// Returns the PSP set in the <see cref="DosSwappableDataArea"/>
    /// </returns>
    public DosProgramSegmentPrefix GetCurrentPsp() {
        return new DosProgramSegmentPrefix(_memory, MemoryUtils.ToPhysicalAddress(_dosSwappableDataArea.CurrentProgramSegmentPrefix, 0));
    }

    /// <summary>
    /// Gets the address of the PSP segment for the current program that is loaded.
    /// </summary>
    /// <returns>Returns the PSP segment for the current program.</returns>
    public ushort GetCurrentPspSegment() {
        return _dosSwappableDataArea.CurrentProgramSegmentPrefix;
    }

    /// <summary>
    /// Sets the current Program Segment Prefix (PSP) segment value for the DOS swappable data area.
    /// </summary>
    /// <param name="segment">The segment value to assign as the current Program Segment Prefix. Must be a valid <see cref="DosProgramSegmentPrefix"/> structure.</param>
    public void SetCurrentPspSegment(ushort segment) {
        _dosSwappableDataArea.CurrentProgramSegmentPrefix = segment;
    }

    /// <summary>
    /// Gets the address where the program image itself starts for the current program that is
    /// loaded.
    /// </summary>
    /// <remarks>
    /// Only the DOS program manager should need to know this. It can be easily calculated from the
    /// current PSP segment. This function is provided just for the convenience of the program
    /// manager.
    /// </remarks>
    /// <returns>
    /// Returns the address where the current program should be loaded after the current PSP,
    /// or <c>0</c> if there is no current PSP segment.
    /// </returns>
    public ushort GetProgramEntryPointSegment() {
        ushort currentPspSegment = GetCurrentPspSegment();
        return (ushort)(currentPspSegment == 0 ? 0 : currentPspSegment + 0x10);
    }

    /// <summary>
    /// Registers a new PSP segment for a new program at the given address.
    /// </summary>
    /// <remarks>
    /// You may call this function either before or after you have registered the new memory with
    /// the memory manager. It does not write to the memory address, so it is safe to call in either
    /// order. However, you should make sure that you allocate the memory before you write to it!
    /// <br/><br/>
    /// This function is intended to be called by the DOS process manager when it is loading a new
    /// process (either COM or EXE). It will become the current PSP from the perspective of this
    /// class after this function returns.
    /// </remarks>
    /// <param name="pspSegment">Address of the PSP segment for a new program.</param>
    /// <returns>Returns the prefix structure of the segment that was just added.</returns>
    public DosProgramSegmentPrefix PushPspSegment(ushort pspSegment) {
        uint pspAddress = MemoryUtils.ToPhysicalAddress(pspSegment, 0);
        DosProgramSegmentPrefix psp = new(_memory, pspAddress);
        _dosSwappableDataArea.CurrentProgramSegmentPrefix = pspSegment;
        _loadedPsps.Add(psp);
        return psp;
    }

    /// <summary>
    /// Removes the given PSP segment after the process has exited.
    /// </summary>
    /// <param name="pspSegment">Address of the PSP segment for the program that is exiting.</param>
    /// <returns>Returns true if the given segment was found and could be removed.</returns>
    public bool PopPspSegment(ushort pspSegment) {
        return _loadedPsps.RemoveAll(psp => MemoryUtils.ToSegment(psp.BaseAddress) == pspSegment) > 0;
    }

    /// <summary>
    /// Removes the last PSP segment from the top of the stack.
    /// </summary>
    public void PopCurrentPspSegment() {
        DosProgramSegmentPrefix currentPsp = GetCurrentPsp();
        _loadedPsps.Remove(currentPsp);
    }
}