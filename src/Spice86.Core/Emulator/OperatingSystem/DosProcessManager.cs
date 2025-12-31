namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Setups the loading and execution of DOS programs and maintains the DOS PSP chains in memory.
/// </summary>
public class DosProcessManager {
    private const byte FarCallOpcode = 0x9A;
    private const byte IntOpcode = 0xCD;
    private const byte Int21Number = 0x21;
    private const byte RetfOpcode = 0xCB;
    private const ushort FakeCpmSegment = 0xDEAD;
    private const ushort FakeCpmOffset = 0xFFFF;
    private const uint NoPreviousPsp = 0xFFFFFFFF;
    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryManager _memoryManager;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// The segment address where the root COMMAND.COM PSP is created.
    /// Follows FreeDOS convention: at 0x0060, and with no PSP MCB.
    /// </summary>
    public const ushort CommandComSegment = 0x60;
    private const byte DefaultDosVersionMajor = 5;
    private const byte DefaultDosVersionMinor = 0;
    private const ushort FileTableOffset = 0x18;
    private const byte DefaultMaxOpenFiles = 20;
    private const byte UnusedFileHandle = 0xFF;
    private const int FcbSize = 16;
    private readonly InterruptVectorTable _interruptVectorTable;

    /// <summary>
    /// The master environment block that all DOS PSPs inherit.
    /// </summary>
    /// <remarks>
    /// Not stored in emulated memory, so no one can modify it.
    /// </remarks>
    private readonly EnvironmentVariables _environmentVariables;

    public DosProcessManager(IMemory memory, State state,
        DosProgramSegmentPrefixTracker dosPspTracker, DosMemoryManager dosMemoryManager,
        DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        IDictionary<string, string> envVars, InterruptVectorTable interruptVectorTable, ILoggerService loggerService) {
        _memory = memory;
        _pspTracker = dosPspTracker;
        _memoryManager = dosMemoryManager;
        _fileManager = dosFileManager;
        _driveManager = dosDriveManager;
        _state = state;
        _loggerService = loggerService;
        _environmentVariables = new();
        _interruptVectorTable = interruptVectorTable;

        envVars.Add("PATH", $"{_driveManager.CurrentDrive.DosVolume}{DosPathResolver.DirectorySeparatorChar}");

        foreach (KeyValuePair<string, string> envVar in envVars) {
            _environmentVariables.Add(envVar.Key, envVar.Value);
        }
    }

    /// <summary>
    /// Gets or sets the return code of the last terminated child process.
    /// </summary>
    /// <remarks>
    /// The low byte (AL) contains the exit code, and the high byte (AH) contains
    /// the termination type. See <see cref="DosTerminationType"/> for termination types.
    /// </remarks>
    public ushort LastChildReturnCode { get; private set; }

    /// <summary>
    /// Creates the root COMMAND.COM PSP that acts as the parent for all programs.
    /// </summary>
    public void CreateRootCommandComPsp() {
        if (_pspTracker.PspCount > 0) {
            // Root PSP already exists
            return;
        }
        DosProgramSegmentPrefix rootPsp = _pspTracker.PushPspSegment(CommandComSegment);

        rootPsp.Exit[0] = 0xCD;
        rootPsp.Exit[1] = 0x20;
        rootPsp.NextSegment = DosMemoryManager.LastFreeSegment;

        // Root PSP: parent points to itself
        rootPsp.ParentProgramSegmentPrefix = CommandComSegment;
        rootPsp.PreviousPspAddress = CommandComSegment;

        // Initialize interrupt vectors from IVT so child PSPs inherit proper addresses
        SegmentedAddress int22 = _interruptVectorTable[0x22];
        rootPsp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);

        SegmentedAddress int23 = _interruptVectorTable[0x23];
        rootPsp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);

        SegmentedAddress int24 = _interruptVectorTable[0x24];
        rootPsp.CriticalErrorAddress = MakeFarPointer(int24.Segment, int24.Offset);

        rootPsp.MaximumOpenFiles = DosFileManager.MaxOpenFilesPerProcess;
        rootPsp.FileTableAddress = MakeFarPointer(CommandComSegment, 0x18);

        // Initialize standard file handles in the PSP file handle table
        // Standard handles: 0=stdin, 1=stdout, 2=stderr, 3=stdaux, 4=stdprn
        // These correspond to the devices opened in Dos.OpenDefaultFileHandles()
        for (byte i = 0; i < 5; i++) {
            rootPsp.Files[i] = i;
        }
        // Mark remaining handles as unused
        for (byte i = 5; i < 20; i++) {
            rootPsp.Files[i] = UnusedFileHandle;
        }

        byte[] environmentBlock = CreateEnvironmentBlock("C:\\COMMAND.COM");
        ushort paragraphsNeeded = (ushort)((environmentBlock.Length + 15) / 16);
        paragraphsNeeded = paragraphsNeeded == 0 ? (ushort)1 : paragraphsNeeded;
        DosMemoryControlBlock? envBlock = _memoryManager.AllocateMemoryBlock(paragraphsNeeded);

        if (envBlock != null) {
            _memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlock.DataBlockSegment, 0), environmentBlock);
            rootPsp.EnvironmentTableSegment = envBlock.DataBlockSegment;
            envBlock.Owner = BuildMcbOwnerName("COMMAND");
        }

        _fileManager.SetDiskTransferAreaAddress(
            CommandComSegment, DosCommandTail.OffsetInPspSegment);
    }

    public void TerminateProcess(byte exitCode, DosTerminationType terminationType,
         InterruptVectorTable interruptVectorTable) {

        // Store the return code for parent to retrieve via INT 21h AH=4Dh
        // Format: AH = termination type, AL = exit code
        LastChildReturnCode = (ushort)(((ushort)terminationType << 8) | exitCode);
        LastChildExitCode = LastChildReturnCode;

        DosProgramSegmentPrefix currentPsp = _pspTracker.GetCurrentPsp();

        ushort currentPspSegment = _pspTracker.GetCurrentPspSegment();
        ushort parentPspSegment = currentPsp.ParentProgramSegmentPrefix;

        // Check if this is the root process (current PSP = parent PSP, which means this IS the shell itself terminating)
        // In that case, there's no parent to return to
        bool isRootProcess = currentPspSegment == parentPspSegment;

        // Check if parent is the root COMMAND.COM PSP - if so, we shouldn't try to return to it
        // because it has no valid stack or execution context. Instead, we halt like a normal program exit.
        bool parentIsRootCommandCom = parentPspSegment == CommandComSegment;

        // If this is a child process (not the main program) with a real parent (not root COMMAND.COM), we have a parent to return to
        bool hasParentToReturnTo = !isRootProcess && !parentIsRootCommandCom && _pspTracker.PspCount > 1;

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "Terminating process at PSP {CurrentPsp:X4}, exit code {ExitCode:X2}, type {Type}, parent PSP {ParentPsp:X4}",
                currentPspSegment, exitCode, terminationType, parentPspSegment);
            _loggerService.Information(
                "Root check: isRootProcess={IsRoot}, parentIsRootCommandCom={ParentIsRoot}, hasParentToReturnTo={HasParent}",
                isRootProcess, parentIsRootCommandCom, hasParentToReturnTo);
        }

        // Close all non-standard file handles (5+) opened by this process
        // Standard handles 0-4 (stdin, stdout, stderr, stdaux, stdprn) are inherited and not closed
        _fileManager.CloseAllNonStandardFileHandles();

        // Cache interrupt vectors from child PSP before freeing memory
        uint terminateAddr = currentPsp.TerminateAddress;
        uint breakAddr = currentPsp.BreakAddress;
        uint criticalErrorAddr = currentPsp.CriticalErrorAddress;

        // This follows FreeDOS
        // TSR (term_type == 3) does NOT free memory - it keeps the program resident
        if (terminationType != DosTerminationType.TSR) {
            _memoryManager.FreeProcessMemory(currentPspSegment);
        }

        // Restore interrupt vectors from cached values
        RestoreInterruptVector(0x22, terminateAddr, interruptVectorTable);
        RestoreInterruptVector(0x23, breakAddr, interruptVectorTable);
        RestoreInterruptVector(0x24, criticalErrorAddr, interruptVectorTable);

        _pspTracker.PopCurrentPspSegment();

        if (hasParentToReturnTo) {
            DosProgramSegmentPrefix parentPsp = _pspTracker.GetCurrentPsp();

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("BEFORE parent stack restore: Current SS:SP={CurrentSs:X4}:{CurrentSp:X4}, CS:IP={CurrentCs:X4}:{CurrentIp:X4}",
                    _state.SS, _state.SP, _state.CS, _state.IP);
                _loggerService.Information("Parent PSP StackPointer field = {ParentStack:X8}, will restore SS:SP to {Ss:X4}:{Sp:X4}",
                    parentPsp.StackPointer, (ushort)(parentPsp.StackPointer >> 16), (ushort)(parentPsp.StackPointer & 0xFFFF));
            }

            // We are in an interrupt handler, so the solution is not in freeDOS but DOSBOX:
            // Restore parent's stack pointer WITHOUT skipping the interrupt frame
            // We'll modify the frame contents so IRET goes to the right place
            _state.SS = (ushort)(parentPsp.StackPointer >> 16);
            _state.SP = (ushort)(parentPsp.StackPointer & 0xFFFF);
            _state.DS = parentPspSegment;
            _state.ES = parentPspSegment;

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("AFTER parent stack restore: SS:SP={Ss:X4}:{Sp:X4}, DS={Ds:X4}, ES={Es:X4}",
                    _state.SS, _state.SP, _state.DS, _state.ES);
            }

            // Get the terminate address from INT 22h vector (restored from child PSP)
            SegmentedAddress returnAddress = interruptVectorTable[0x22];

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information(
                    "Returning to parent at {Segment:X4}:{Offset:X4} from child PSP {ChildPsp:X4}, parent PSP {ParentPsp:X4}",
                    returnAddress.Segment, returnAddress.Offset, currentPspSegment, parentPspSegment);
            }

            // Modify the interrupt frame on the stack
            // so that when IRET pops it, execution continues at the return address
            uint stackPhysicalAddress = MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP);

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Stack physical address = {StackPhys:X8}, reading current frame values...",
                    stackPhysicalAddress);
                ushort oldIP = _memory.UInt16[stackPhysicalAddress];
                ushort oldCS = _memory.UInt16[stackPhysicalAddress + 2];
                ushort oldFlags = _memory.UInt16[stackPhysicalAddress + 4];
                _loggerService.Information("OLD interrupt frame on stack: IP={OldIp:X4}, CS={OldCs:X4}, FLAGS={OldFlags:X4}",
                    oldIP, oldCS, oldFlags);
            }

            _memory.UInt16[stackPhysicalAddress] = returnAddress.Offset;     // IP
            _memory.UInt16[stackPhysicalAddress + 2] = returnAddress.Segment; // CS

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                ushort newIP = _memory.UInt16[stackPhysicalAddress];
                ushort newCS = _memory.UInt16[stackPhysicalAddress + 2];
                ushort newFlags = _memory.UInt16[stackPhysicalAddress + 4];
                _loggerService.Information("NEW interrupt frame on stack: IP={NewIp:X4}, CS={NewCs:X4}, FLAGS={NewFlags:X4}",
                    newIP, newCS, newFlags);
                _loggerService.Information("IRET will pop these values and jump to {Cs:X4}:{Ip:X4}",
                    newCS, newIP);
            }

            // DON'T manually set CS:IP - let IRET handle it by popping the modified frame

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Returning TRUE from TerminateProcess, callback will execute IRET");
            }
        }

        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("No parent to return to - returning to command.com PSP!");
        }
    }

    private static void RestoreInterruptVector(byte vectorNumber, uint storedFarPointer,
        InterruptVectorTable interruptVectorTable) {
        if (storedFarPointer != 0) {
            ushort offset = (ushort)(storedFarPointer & 0xFFFF);
            ushort segment = (ushort)(storedFarPointer >> 16);
            interruptVectorTable[vectorNumber] = new SegmentedAddress(segment, offset);
        }
    }

    /// <summary>
    /// Gets the last child process exit code. Used by INT 21h AH=4Dh.
    /// </summary>
    /// <returns>Exit code in AL, termination type in AH (always 0 for normal termination).</returns>
    public ushort LastChildExitCode { get; set; } = 0;

    /// <summary>
    /// Implements INT 21h, AH=26h - Create New PSP.
    /// Copies the current PSP to a new segment and updates INT 22h/23h/24h vectors and DOS version.
    /// The new PSP's parent field is set to the current PSP segment (matching DOS reference behavior).
    /// </summary>
    /// <param name="newPspSegment">The segment address where the new PSP will be created.</param>
    /// <param name="interruptVectorTable">The interrupt vector table for reading current vectors.</param>
    public void CreateNewPsp(ushort newPspSegment, InterruptVectorTable interruptVectorTable) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "CreateNewPsp: Copying current PSP to segment {NewPspSegment:X4}",
                newPspSegment);
        }

        ushort currentPspSegment = _pspTracker.GetCurrentPspSegment();

        uint currentPspAddress = MemoryUtils.ToPhysicalAddress(currentPspSegment, 0);
        uint newPspAddress = MemoryUtils.ToPhysicalAddress(newPspSegment, 0);

        byte[] pspData = _memory.ReadRam(DosProgramSegmentPrefix.MaxLength, currentPspAddress);
        _memory.LoadData(newPspAddress, pspData);

        DosProgramSegmentPrefix currentPsp = _pspTracker.GetCurrentPsp();
        DosProgramSegmentPrefix newPsp = new(_memory, newPspAddress);

        newPsp.Exit[0] = 0xCD;
        newPsp.Exit[1] = 0x20;

        newPsp.ParentProgramSegmentPrefix = currentPspSegment;
        newPsp.EnvironmentTableSegment = currentPsp.EnvironmentTableSegment;

        SegmentedAddress int22 = interruptVectorTable[0x22];
        newPsp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);

        SegmentedAddress int23 = interruptVectorTable[0x23];
        newPsp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);

        SegmentedAddress int24 = interruptVectorTable[0x24];
        newPsp.CriticalErrorAddress = MakeFarPointer(int24.Segment, int24.Offset);

        newPsp.DosVersionMajor = DefaultDosVersionMajor;
        newPsp.DosVersionMinor = DefaultDosVersionMinor;

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            newPsp = new(_memory, newPspAddress);
            _loggerService.Information(
                "CreateNewPsp: Created PSP at {NewPspSegment:X4} from {CurrentPspSegment:X4}, Parent={Parent:X4}, " +
                "TerminateAddr={TermAddr:X8}, BreakAddr={BreakAddr:X8}, CriticalErrorAddr={CritErr:X8}, " +
                "Exit[0]={Exit0:X2}, Exit[1]={Exit1:X2}, Env={Env:X4}",
                newPspSegment, currentPspSegment, newPsp.ParentProgramSegmentPrefix,
                newPsp.TerminateAddress, newPsp.BreakAddress, newPsp.CriticalErrorAddress,
                newPsp.Exit[0], newPsp.Exit[1], newPsp.EnvironmentTableSegment);
        }
    }

    /// <summary>
    /// Implements INT 21h, AH=55h - Create Child PSP.
    /// Creates a child PSP initialized from the current PSP, copying handles, FCBs, command tail, and environment.
    /// </summary>
    public void CreateChildPsp(ushort childSegment, ushort sizeInParagraphs, InterruptVectorTable interruptVectorTable) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "CreateChildPsp: Creating child PSP at segment {ChildSegment:X4}, size {Size} paragraphs",
                childSegment, sizeInParagraphs);
        }

        ushort parentPspSegment = _pspTracker.GetCurrentPspSegment();
        uint childPspAddress = MemoryUtils.ToPhysicalAddress(childSegment, 0);
        DosProgramSegmentPrefix childPsp = new(_memory, childPspAddress);

        InitializeChildPsp(childPsp, childSegment, parentPspSegment, sizeInParagraphs, interruptVectorTable);

        uint parentPspAddress = MemoryUtils.ToPhysicalAddress(parentPspSegment, 0);
        DosProgramSegmentPrefix parentPsp = new(_memory, parentPspAddress);

        CopyFileTableFromParent(childPsp, parentPsp);
        CopyCommandTailFromParent(childPsp, parentPsp);
        CopyFcb1FromParent(childPsp, parentPsp);
        CopyFcb2FromParent(childPsp, parentPsp);

        childPsp.EnvironmentTableSegment = parentPsp.EnvironmentTableSegment;
        childPsp.StackPointer = parentPsp.StackPointer;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug(
                "CreateChildPsp: Parent={Parent:X4}, Env={Env:X4}, NextSeg={Next:X4}",
                parentPspSegment, childPsp.EnvironmentTableSegment, childPsp.NextSegment);
        }
    }

    private void InitializeChildPsp(DosProgramSegmentPrefix psp, ushort pspSegment,
        ushort parentPspSegment, ushort sizeInParagraphs, InterruptVectorTable interruptVectorTable) {
        for (int i = 0; i < DosProgramSegmentPrefix.MaxLength; i++) {
            _memory.UInt8[psp.BaseAddress + (uint)i] = 0;
        }

        InitializeCommonPspFields(psp, parentPspSegment);

        psp.NextSegment = (ushort)(pspSegment + sizeInParagraphs);
        psp.FarCall = FarCallOpcode;
        psp.CpmServiceRequestAddress = MakeFarPointer(FakeCpmSegment, FakeCpmOffset);

        psp.Service[0] = IntOpcode;
        psp.Service[1] = Int21Number;
        psp.Service[2] = RetfOpcode;

        psp.PreviousPspAddress = NoPreviousPsp;
        psp.DosVersionMajor = DefaultDosVersionMajor;
        psp.DosVersionMinor = DefaultDosVersionMinor;

        SaveInterruptVectors(psp, interruptVectorTable);

        psp.FileTableAddress = MakeFarPointer(pspSegment, FileTableOffset);
        psp.MaximumOpenFiles = DefaultMaxOpenFiles;
        for (int i = 0; i < DefaultMaxOpenFiles; i++) {
            psp.Files[i] = UnusedFileHandle;
        }
    }

    private static void SaveInterruptVectors(DosProgramSegmentPrefix psp, InterruptVectorTable ivt) {
        SegmentedAddress int22 = ivt[0x22];
        psp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);

        SegmentedAddress int23 = ivt[0x23];
        psp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);

        SegmentedAddress int24 = ivt[0x24];
        psp.CriticalErrorAddress = MakeFarPointer(int24.Segment, int24.Offset);
    }

    private static void InitializeCommonPspFields(DosProgramSegmentPrefix psp, ushort parentPspSegment) {
        psp.Exit[0] = IntOpcode;
        psp.Exit[1] = 0x20;
        psp.ParentProgramSegmentPrefix = parentPspSegment;
        psp.PreviousPspAddress = NoPreviousPsp;
    }

    private void CopyFileTableFromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
        for (int i = 0; i < DefaultMaxOpenFiles; i++) {
            byte parentHandle = parentPsp.Files[i];

            if (parentHandle == UnusedFileHandle) {
                childPsp.Files[i] = UnusedFileHandle;
                continue;
            }

            if (parentHandle < _fileManager.OpenFiles.Length) {
                VirtualFileBase? file = _fileManager.OpenFiles[parentHandle];
                if (file is DosFile dosFile && (dosFile.Flags & (byte)FileAccessMode.Private) != 0) {
                    childPsp.Files[i] = UnusedFileHandle;
                    continue;
                }
            }

            childPsp.Files[i] = parentHandle;
        }
    }

    private static void CopyCommandTailFromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
        childPsp.DosCommandTail.Command = parentPsp.DosCommandTail.Command;
    }

    private static void CopyFcb1FromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
        for (int i = 0; i < FcbSize; i++) {
            childPsp.FirstFileControlBlock[i] = parentPsp.FirstFileControlBlock[i];
        }
    }

    private static void CopyFcb2FromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
        for (int i = 0; i < FcbSize; i++) {
            childPsp.SecondFileControlBlock[i] = parentPsp.SecondFileControlBlock[i];
        }
    }

    private void CopyFcbFromPointer(SegmentedAddress pointer, UInt8Array destination) {
        // FreeDOS treats 0000:0000 and FFFF:FFFF as "no FCB" markers.
        if ((pointer.Segment == 0 && pointer.Offset == 0) || (pointer.Segment == 0xFFFF && pointer.Offset == 0xFFFF)) {
            return;
        }

        uint source = MemoryUtils.ToPhysicalAddress(pointer.Segment, pointer.Offset);
        for (int i = 0; i < FcbSize; i++) {
            destination[i] = _memory.UInt8[source + (uint)i];
        }
    }

    private static uint MakeFarPointer(ushort segment, ushort offset) {
        return (uint)((segment << 16) | offset);
    }

    private static string BuildMcbOwnerName(string programPath) {
        string name = Path.GetFileNameWithoutExtension(programPath).ToUpperInvariant();
        // The MCB owner field is 8 bytes and this setter writes a zero terminator,
        // so keep the textual portion to at most 7 characters.
        if (name.Length > 7) {
            name = name[..7];
        }

        return name;
    }

    private ushort ComputeFcbCode(DosProgramSegmentPrefix psp) {
        ushort code = 0;

        byte drive1 = psp.FirstFileControlBlock[0];
        if (!IsFcbDriveValid(drive1)) {
            code |= 0x00FF;
        }

        byte drive2 = psp.SecondFileControlBlock[0];
        if (!IsFcbDriveValid(drive2)) {
            code |= 0xFF00;
        }

        return code;
    }

    private bool IsFcbDriveValid(byte driveByte) {
        if (driveByte == 0) {
            return true;
        }

        if (driveByte == 0xFF) {
            return false;
        }

        if (driveByte > 26) {
            return false;
        }

        ushort zeroBasedIndex = (ushort)(driveByte - 1);
        return _driveManager.HasDriveAtIndex(zeroBasedIndex);
    }

    public DosExecResult LoadOrLoadAndExecute(string programName, DosExecParameterBlock paramBlock,
        string commandTail, DosExecLoadType loadType, ushort environmentSegment, InterruptVectorTable interruptVectorTable) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("EXEC: Loading program={Program}, loadType={LoadType}, envSeg={EnvSeg:X4}",
                programName, loadType, environmentSegment);
        }

        // We read CS:IP from the stack to get the address where the parent will resume.
        ushort callerIP = _memory.UInt16[_state.StackPhysicalAddress];
        ushort callerCS = _memory.UInt16[_state.StackPhysicalAddress + 2];
        ushort callerFlags = _memory.UInt16[_state.StackPhysicalAddress + 4];

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("EXEC: Stack at SS:SP={StackSeg:X4}:{StackOff:X4}, read caller CS:IP={CallerCs:X4}:{CallerIp:X4}, FLAGS={Flags:X4}",
                _state.SS, _state.SP, callerCS, callerIP, callerFlags);
            _loggerService.Information("EXEC: Bytes at return-2: {Byte0:X2} {Byte1:X2}, Bytes at return address: {Byte2:X2} {Byte3:X2} {Byte4:X2} {Byte5:X2}",
                _memory.UInt8[MemoryUtils.ToPhysicalAddress(callerCS, (ushort)(callerIP - 2))],
                _memory.UInt8[MemoryUtils.ToPhysicalAddress(callerCS, (ushort)(callerIP - 1))],
                _memory.UInt8[MemoryUtils.ToPhysicalAddress(callerCS, callerIP)],
                _memory.UInt8[MemoryUtils.ToPhysicalAddress(callerCS, (ushort)(callerIP + 1))],
                _memory.UInt8[MemoryUtils.ToPhysicalAddress(callerCS, (ushort)(callerIP + 2))],
                _memory.UInt8[MemoryUtils.ToPhysicalAddress(callerCS, (ushort)(callerIP + 3))]);
        }

        ushort parentPspSegment = _pspTracker.GetCurrentPspSegment();

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("EXEC: Current PSP={CurrentPsp:X4}, will become parent",
                parentPspSegment);
        }

        string? hostPath = _fileManager.TryGetFullHostPathFromDos(programName) ?? programName;
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath)) {
            return DosExecResult.Fail(DosErrorCode.FileNotFound);
        }

        byte[] fileBytes = ReadFileBytes(hostPath);

        string upperCaseExtension = Path.GetExtension(hostPath).ToUpperInvariant();
        bool isExeCandidate = fileBytes.Length >= DosExeFile.MinExeSize && upperCaseExtension == ".EXE";
        bool isLoadAndExecute = loadType == DosExecLoadType.LoadAndExecute;

        // Save parent's current SS:SP BEFORE any CPU state changes
        // This captures the parent's stack context before the child modifies anything
        uint parentStackPointer = ((uint)_state.SS << 16) | _state.SP;

        // Try to load as EXE first if it looks like an EXE file
        if (isExeCandidate) {
            DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            if (exeFile.IsValid) {
                DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);
                if (block is null) {
                    return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
                }

                InitializePsp(block.DataBlockSegment, hostPath, commandTail, environmentSegment, interruptVectorTable, parentPspSegment, parentStackPointer, callerCS, callerIP);

                DosProgramSegmentPrefix exePsp = new(_memory, MemoryUtils.ToPhysicalAddress(block.DataBlockSegment, 0));
                CopyFcbFromPointer(paramBlock.FirstFcbPointer, exePsp.FirstFileControlBlock);
                CopyFcbFromPointer(paramBlock.SecondFcbPointer, exePsp.SecondFileControlBlock);

                ushort fcbCode = ComputeFcbCode(exePsp);

                LoadExeFile(exeFile, block.DataBlockSegment, block, isLoadAndExecute);

                ushort loadImageSegment = (ushort)(block.DataBlockSegment + DosProgramSegmentPrefix.PspSizeInParagraphs);
                if (exeFile.MinAlloc == 0 && exeFile.MaxAlloc == 0) {
                    ushort imageDistanceInParagraphs = (ushort)(block.Size - exeFile.ProgramSizeInParagraphsPerHeader);
                    loadImageSegment = (ushort)(block.DataBlockSegment + imageDistanceInParagraphs);
                }

                DosExecResult result = loadType == DosExecLoadType.LoadOnly
                    ? DosExecResult.SuccessLoadOnly((ushort)(exeFile.InitCS + loadImageSegment), exeFile.InitIP,
                        (ushort)(exeFile.InitSS + loadImageSegment), exeFile.InitSP)
                    : DosExecResult.SuccessExecute((ushort)(exeFile.InitCS + loadImageSegment), exeFile.InitIP,
                        (ushort)(exeFile.InitSS + loadImageSegment), exeFile.InitSP);

                if (isLoadAndExecute) {
                    _state.AX = fcbCode;
                    _state.BX = fcbCode;
                }

                if (!isLoadAndExecute) {
                    // LoadOnly: restore parent PSP as current
                    // Do NOT modify CS:IP/SS:SP - the IRET instruction will restore them from the stack
                    _pspTracker.SetCurrentPspSegment(parentPspSegment);
                }

                // For AL=01 (Load Only), DOS fills the EPB with initial CS:IP and SS:SP.
                if (loadType == DosExecLoadType.LoadOnly && result.Success) {
                    paramBlock.InitialCS = result.InitialCS;
                    paramBlock.InitialIP = result.InitialIP;
                    paramBlock.InitialSS = result.InitialSS;
                    paramBlock.InitialSP = result.InitialSP;

                    if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information("EXEC: LoadOnly filled parameter block at ES:BX with CS:IP={Cs:X4}:{Ip:X4}, SS:SP={Ss:X4}:{Sp:X4}",
                            paramBlock.InitialCS, paramBlock.InitialIP, paramBlock.InitialSS, paramBlock.InitialSP);
                    }
                }

                return result;
            }
            // If file has .EXE extension but isn't a valid EXE, fall through to try COM loading
            // This matches FreeDOS behavior
        }

        // Load as COM file (either explicitly .COM or invalid .EXE that we'll try as COM)
        ushort paragraphsNeeded = (ushort)((DosProgramSegmentPrefix.PspSize + fileBytes.Length + 15) / 16);
        paragraphsNeeded = (ushort)(paragraphsNeeded == 0 ? 1 : paragraphsNeeded);
        DosMemoryControlBlock? comBlock = _memoryManager.AllocateMemoryBlock(paragraphsNeeded);
        if (comBlock is null) {
            return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
        }
        InitializePsp(comBlock.DataBlockSegment, hostPath, commandTail, environmentSegment, interruptVectorTable, parentPspSegment, parentStackPointer, callerCS, callerIP);

        DosProgramSegmentPrefix comPsp = new(_memory, MemoryUtils.ToPhysicalAddress(comBlock.DataBlockSegment, 0));
        CopyFcbFromPointer(paramBlock.FirstFcbPointer, comPsp.FirstFileControlBlock);
        CopyFcbFromPointer(paramBlock.SecondFcbPointer, comPsp.SecondFileControlBlock);

        ushort comFcbCode = ComputeFcbCode(comPsp);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("EXEC: Loading COM at PSP={PspSeg:X4}, size={Size} bytes",
                comBlock.DataBlockSegment, fileBytes.Length);
        }

        LoadComFile(fileBytes, comBlock.DataBlockSegment, isLoadAndExecute);

        DosExecResult comResult = loadType == DosExecLoadType.LoadOnly
            ? DosExecResult.SuccessLoadOnly(comBlock.DataBlockSegment, DosProgramSegmentPrefix.PspSize,
                comBlock.DataBlockSegment, 0xFFFE)
            : DosExecResult.SuccessExecute(comBlock.DataBlockSegment, DosProgramSegmentPrefix.PspSize,
                comBlock.DataBlockSegment, 0xFFFE);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("EXEC: COM loaded successfully, CS:IP={Cs:X4}:{Ip:X4}, SS:SP={Ss:X4}:{Sp:X4}",
                comResult.InitialCS, comResult.InitialIP, comResult.InitialSS, comResult.InitialSP);
        }

        if (!isLoadAndExecute) {
            // LoadOnly: restore parent PSP as current
            // Do NOT modify CS:IP/SS:SP - the IRET instruction will restore them from the stack
            _pspTracker.SetCurrentPspSegment(parentPspSegment);

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("EXEC: COM LoadOnly - restoring parent PSP={ParentPsp:X4}, IRET will restore caller's CS:IP",
                    parentPspSegment);
            }
        } else {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("EXEC: COM LoadAndExecute - CPU state updated, now at CS:IP={Cs:X4}:{Ip:X4}, current PSP={CurrentPsp:X4}",
                    _state.CS, _state.IP, _pspTracker.GetCurrentPspSegment());
            }
        }

        // For AL=01 (Load Only), DOS fills the EPB with initial CS:IP and SS:SP.
        if (loadType == DosExecLoadType.LoadOnly && comResult.Success) {
            paramBlock.InitialCS = comResult.InitialCS;
            paramBlock.InitialIP = comResult.InitialIP;
            paramBlock.InitialSS = comResult.InitialSS;
            paramBlock.InitialSP = comResult.InitialSP;

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("EXEC: LoadOnly filled parameter block at ES:BX with CS:IP={Cs:X4}:{Ip:X4}, SS:SP={Ss:X4}:{Sp:X4}",
                    paramBlock.InitialCS, paramBlock.InitialIP, paramBlock.InitialSS, paramBlock.InitialSP);
            }
        }

        if (isLoadAndExecute) {
            _state.AX = comFcbCode;
            _state.BX = comFcbCode;
        }

        return comResult;
    }

    public DosExecResult LoadOverlay(string programName, ushort loadSegment, ushort relocationFactor) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("LoadOverlay: programName={ProgramName}, loadSegment={LoadSegment:X4}, relocationFactor={RelocationFactor:X4}",
                programName, loadSegment, relocationFactor);
        }

        string? hostPath = _fileManager.TryGetFullHostPathFromDos(programName) ?? programName;
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("LoadOverlay: File not found - hostPath={HostPath}", hostPath ?? "null");
            }
            return DosExecResult.Fail(DosErrorCode.FileNotFound);
        }

        byte[] fileBytes = ReadFileBytes(hostPath);

        if (fileBytes.Length < DosExeFile.MinExeSize) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("LoadOverlay: File too small - length={Length}, min={Min}", fileBytes.Length, DosExeFile.MinExeSize);
            }
            return DosExecResult.Fail(DosErrorCode.FormatInvalid);
        }

        DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
        if (!exeFile.IsValid) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("LoadOverlay: Invalid EXE file format");
            }
            return DosExecResult.Fail(DosErrorCode.FormatInvalid);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("LoadOverlay: Loading EXE at segment {LoadSegment:X4}, relocation factor={RelocationFactor:X4}, InitCS={InitCS:X4}, InitIP={InitIP:X4}",
                loadSegment, relocationFactor, exeFile.InitCS, exeFile.InitIP);
        }

        // For overlays, load at loadSegment but relocate using relocationFactor
        // This matches DOSBox staging behavior where overlay relocation uses the relocation factor parameter
        LoadExeFileInMemoryAndApplyRelocations(exeFile, loadSegment, relocationFactor);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("LoadOverlay: Successfully loaded overlay");
            // Log first few bytes at load segment to verify code is there
            uint physicalAddress = MemoryUtils.ToPhysicalAddress(loadSegment, 0);
            string bytesHex = string.Join(" ", Enumerable.Range(0, Math.Min(16, (int)exeFile.ProgramSize))
                .Select(i => _memory.UInt8[physicalAddress + (uint)i].ToString("X2")));
            _loggerService.Information("LoadOverlay: First 16 bytes at {LoadSegment:X4}:0000 (physical {PhysicalAddress:X5}): {Bytes}",
                loadSegment, physicalAddress, bytesHex);
        }

        // For overlays, DOS doesn't return anything in the parameter block
        // Just return success with AX=0 and DX=0
        return DosExecResult.SuccessLoadOverlay();
    }

    private void InitializePsp(ushort pspSegment, string programHostPath, string? arguments, ushort environmentSegment, InterruptVectorTable interruptVectorTable, ushort parentPspSegment, uint parentStackPointer, ushort callerCS, ushort callerIP) {
        // Establish parent-child PSP relationship and create the new PSP
        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(pspSegment);

        psp.Exit[0] = 0xCD;
        psp.Exit[1] = 0x20;
        psp.NextSegment = DosMemoryManager.LastFreeSegment;

        // This sets INT 22h to point to the CALLER'S return address
        psp.TerminateAddress = MakeFarPointer(callerCS, callerIP);

        SegmentedAddress breakVector = interruptVectorTable[0x23];
        SegmentedAddress criticalErrorVector = interruptVectorTable[0x24];
        psp.BreakAddress = MakeFarPointer(breakVector.Segment, breakVector.Offset);
        psp.CriticalErrorAddress = MakeFarPointer(criticalErrorVector.Segment, criticalErrorVector.Offset);

        // Save parent's stack pointer in the PARENT PSP's StackPointer field (offset 0x2E)
        // This mirrors FreeDOS task.c
        DosProgramSegmentPrefix parentPsp = new(_memory, MemoryUtils.ToPhysicalAddress(parentPspSegment, 0));
        parentPsp.StackPointer = parentStackPointer;

        psp.ParentProgramSegmentPrefix = parentPspSegment;
        psp.MaximumOpenFiles = DosFileManager.MaxOpenFilesPerProcess;
        // file table address points to file table at offset 0x18 inside this PSP
        psp.FileTableAddress = ((uint)pspSegment << 16) | 0x18;
        psp.PreviousPspAddress = parentPspSegment;

        // Inherit parent's JFT entries except those marked as DoNotInherit or Unused
        for (int i = 0; i < parentPsp.Files.Count; i++) {
            byte parentEntry = parentPsp.Files[i];
            if (parentEntry == (byte)Enums.DosPspFileTableEntry.Unused) {
                continue;
            }
            if ((parentEntry & (byte)Enums.DosPspFileTableEntry.DoNotInherit) != 0) {
                // do not inherit
                continue;
            }
            psp.Files[i] = parentEntry;
        }

        psp.DosCommandTail.Command = DosCommandTail.PrepareCommandlineString(arguments);

        // Always clone the environment into a fresh block (FreeDOS ChildEnv behavior).
        SetupEnvironmentForProcess(programHostPath, environmentSegment, psp, parentPsp);

        _fileManager.SetDiskTransferAreaAddress(
            pspSegment, DosCommandTail.OffsetInPspSegment);
    }

    private void SetupEnvironmentForProcess(string programHostPath,
        ushort environmentSegment, DosProgramSegmentPrefix psp,
        DosProgramSegmentPrefix parentPsp) {
        ushort sourceEnvironmentSegment = environmentSegment != 0
                    ? environmentSegment
                    : parentPsp.EnvironmentTableSegment;

        byte[] environmentBlock;
        if (sourceEnvironmentSegment != 0) {
            environmentBlock = CreateEnvironmentBlockFromParent(sourceEnvironmentSegment, programHostPath);
        } else {
            environmentBlock = CreateEnvironmentBlock(programHostPath);
        }

        ushort paragraphsNeeded = (ushort)((environmentBlock.Length + 15) / 16);
        paragraphsNeeded = paragraphsNeeded == 0 ? (ushort)1 : paragraphsNeeded;
        DosMemoryControlBlock? envBlock = _memoryManager.AllocateMemoryBlock(paragraphsNeeded);

        if (envBlock != null) {
            _memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlock.DataBlockSegment, 0), environmentBlock);
            psp.EnvironmentTableSegment = envBlock.DataBlockSegment;
                envBlock.Owner = BuildMcbOwnerName(programHostPath);
        }
    }

    private byte[] ReadFileBytes(string file) {
        return File.ReadAllBytes(file);
    }

    /// <summary>
    /// Creates a DOS environment block from the current environment variables.
    /// </summary>
    /// <param name="programPath">The path to the program being executed.</param>
    /// <returns>A byte array containing the DOS environment block.</returns>
    private byte[] CreateEnvironmentBlock(string programPath) {
        using MemoryStream ms = new();

        // Add each environment variable as NAME=VALUE followed by a null terminator
        foreach (KeyValuePair<string, string> envVar in _environmentVariables) {
            string envString = $"{envVar.Key}={envVar.Value}";
            byte[] envBytes = Encoding.ASCII.GetBytes(envString);
            ms.Write(envBytes, 0, envBytes.Length);
            ms.WriteByte(0); // Null terminator for this variable
        }

        // Add final null byte to mark end of environment block
        ms.WriteByte(0);

        // MS-DOS format: after the double null, write a WORD with value 1 to indicate
        // that one additional string (the program path) follows.
        // This is the correct DOS format
        ms.WriteByte(1); // Low byte of WORD = 1
        ms.WriteByte(0); // High byte of WORD = 0

        // Get the DOS path for the program (not the host path)
        string dosPath = _fileManager.GetDosProgramPath(programPath);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Environment block: writing program path \"{DosPath}\" after variables",
                dosPath);
        }

        // Write the DOS path to the environment block
        byte[] programPathBytes = Encoding.ASCII.GetBytes(dosPath);
        ms.Write(programPathBytes, 0, programPathBytes.Length);
        ms.WriteByte(0); // Null terminator for program path

        return ms.ToArray();
    }

    /// <summary>
    /// Creates an environment block from the parent process.
    /// </summary>
    private byte[] CreateEnvironmentBlockFromParent(ushort environmentSegment, string programPath) {
        using MemoryStream ms = new();

        uint environmentBaseAddress = MemoryUtils.ToPhysicalAddress(environmentSegment, 0);
        int offset = 0;
        // Match FreeDOS env scan ceiling (MAXENV) to avoid walking huge memory regions.
        const int maximumScanLength = 32768;
        bool doubleNullFound = false;

        // Copy the parent's environment variables up to and including the double null terminator.
        while (offset + 1 < maximumScanLength) {
            byte current = _memory.UInt8[environmentBaseAddress + (uint)offset];
            byte next = _memory.UInt8[environmentBaseAddress + (uint)(offset + 1)];

            ms.WriteByte(current);
            offset++;

            if (current == 0 && next == 0) {
                ms.WriteByte(next);
                offset++;
                doubleNullFound = true;
                break;
            }
        }

        if (!doubleNullFound) {
            return CreateEnvironmentBlock(programPath);
        }

        // Replace the extra strings section with the current program path.
        ms.WriteByte(1);
        ms.WriteByte(0);

        string dosPath = _fileManager.GetDosProgramPath(programPath);
        byte[] programPathBytes = Encoding.ASCII.GetBytes(dosPath);
        ms.Write(programPathBytes, 0, programPathBytes.Length);
        ms.WriteByte(0);

        return ms.ToArray();
    }

    private void LoadComFile(byte[] com, ushort pspSegment, bool isLoadAndExecute) {
        uint physicalLoadAddress = MemoryUtils.ToPhysicalAddress(pspSegment, DosProgramSegmentPrefix.PspSize);
        _memory.LoadData(physicalLoadAddress, com);

        if (isLoadAndExecute) {
            _state.CS = pspSegment;
            _state.IP = DosProgramSegmentPrefix.PspSize;
            _state.DS = pspSegment;
            _state.ES = pspSegment;
            _state.SS = pspSegment;
            _state.SP = 0xFFFE; // Expected stack pointer value
            _state.InterruptFlag = true;
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("COM load register state CS:IP={Cs}:{Ip} DS=ES=SS={Segment} SP={Sp}",
                    ConvertUtils.ToHex16(_state.CS), ConvertUtils.ToHex16(_state.IP), ConvertUtils.ToHex16(pspSegment), ConvertUtils.ToHex16(_state.SP));
            }
        }
    }

    private void LoadExeFile(DosExeFile exeFile, ushort pspSegment, DosMemoryControlBlock block, bool updateCpuState) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Read header: {ReadHeader}", exeFile);
        }
        ushort pspLoadSegment = block.DataBlockSegment;
        // The program image is loaded immediately above the PSP, which is the start of
        // the memory block that we just allocated.
        // Jump over the PSP to get the EXE image segment.
        ushort loadImageSegment = (ushort)(pspLoadSegment + DosProgramSegmentPrefix.PspSizeInParagraphs);

        // Adjust image load segment when PSP and exe image gets splitted due to load into high memory
        if (exeFile.MinAlloc == 0 && exeFile.MaxAlloc == 0) {
            ushort imageDistanceInParagraphs = (ushort)(block.Size - exeFile.ProgramSizeInParagraphsPerHeader);
            loadImageSegment = (ushort)(pspLoadSegment + imageDistanceInParagraphs);
        }
        LoadExeFileInMemoryAndApplyRelocations(exeFile, loadImageSegment, loadImageSegment);
        if (updateCpuState) {
            SetupCpuForExe(exeFile, loadImageSegment, pspSegment);
        }
    }

    /// <summary>
    /// Loads the program image and applies any necessary relocations to it.
    /// </summary>
    /// <param name="exeFile">The EXE file to load.</param>
    /// <param name="loadImageSegment">The segment where the program image will be loaded.</param>
    /// <param name="relocationSegment">The segment value to add to relocation table entries. For normal EXE loading, this is the same as loadImageSegment. For overlays, this is the relocation factor from the parameter block.</param>
    private void LoadExeFileInMemoryAndApplyRelocations(DosExeFile exeFile, ushort loadImageSegment, ushort relocationSegment) {
        uint physicalLoadAddress = MemoryUtils.ToPhysicalAddress(loadImageSegment, 0);
        _memory.LoadData(physicalLoadAddress, exeFile.ProgramImage, (int)exeFile.ProgramSize);
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            // Read value from memory, add the relocation segment offset and write back
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset)
                + physicalLoadAddress;
            _memory.UInt16[addressToEdit] += relocationSegment;
        }
    }

    /// <summary>
    /// Sets up the CPU to execute the loaded program.
    /// </summary>
    /// <param name="exeFile">The EXE file that was loaded.</param>
    /// <param name="loadSegment">The segment where the program has been loaded.</param>
    /// <param name="pspSegment">The segment address of the program's PSP (Program Segment Prefix).</param>
    private void SetupCpuForExe(DosExeFile exeFile, ushort loadSegment, ushort pspSegment) {
        // MS-DOS uses the values in the file header to set the SP and SS registers and
        // adjusts the initial value of the SS register by adding the start-segment
        // address to it.
        _state.SS = (ushort)(exeFile.InitSS + loadSegment);
        _state.SP = exeFile.InitSP;

        // Make DS and ES point to the PSP
        _state.DS = pspSegment;
        _state.ES = pspSegment;

        _state.InterruptFlag = true;

        // Finally, MS-DOS reads the initial CS and IP values from the program's file
        // header, adjusts the CS register value by adding the start-segment address to
        // it, and transfers control to the program at the adjusted address.
        SetEntryPoint((ushort)(exeFile.InitCS + loadSegment), exeFile.InitIP);
    }

    private void SetEntryPoint(ushort cs, ushort ip) {
        _state.CS = cs;
        _state.IP = ip;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Program entry point is {ProgramEntry}", ConvertUtils.ToSegmentedAddressRepresentation(cs, ip));
        }
    }
}