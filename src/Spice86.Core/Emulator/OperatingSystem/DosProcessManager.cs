namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections.Generic;
using System.IO;
using System.Text;

using Stack = CPU.Stack;

/// <summary>
/// Manages the lifecycle of DOS processes, including creation, execution, termination, and environment management, by
/// emulating the behavior of the DOS Program Segment Prefix (PSP) and related process structures.
/// </summary>
/// <remarks>The DosProcessManager provides high-level operations for loading and executing DOS programs (EXE and
/// COM), cloning and terminating processes, and managing process-specific resources such as file handles and
/// environment blocks. It emulates key DOS process management interrupts (such as INT 21h AH=4Bh, 26h, 55h) and ensures
/// correct parent-child relationships, memory allocation, and file handles cleanup.</remarks>
public class DosProcessManager {
    private const byte FarCallOpcode = 0x9A;
    private const byte IntOpcode = 0xCD;
    private const byte Int21Number = 0x21;
    private const byte Int20TerminateNumber = 0x20;
    private const byte TerminateVectorNumber = 0x22;
    private const byte CtrlBreakVectorNumber = 0x23;
    private const byte CriticalErrorVectorNumber = 0x24;
    private const byte RetfOpcode = 0xCB;

    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryManager _memoryManager;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;
    private readonly BatchFileManager _batchFileManager;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly ILoggerService _loggerService;
    private readonly Dictionary<ushort, (ushort SS, ushort SP)> _pendingParentStackPointers = new();
    private readonly Dictionary<ushort, ResidentBlockInfo> _pendingResidentBlocks = new();
    private ushort? _initialProgramPspSegment;  // Track the PSP of the initial program loaded by DosProgramLoader

    /// <summary>
    /// The segment address where the root COMMAND.COM PSP is created.
    /// Follows FreeDOS convention: at 0x60, and with no PSP MCB.
    /// </summary>
    public const ushort CommandComSegment = 0x60;
    private const byte DefaultDosVersionMajor = 5;
    private const byte DefaultDosVersionMinor = 0;
    private const ushort FileTableOffset = 0x18;
    private const ushort Call5StubOffset = 0x50;
    private const ushort RootEnvironmentParagraphOffset = 0x8;
    private const int JobFileTableLength = DosFileManager.MaxOpenFilesPerProcess;
    private const byte StandardFileHandleCount = 5;
    private const byte UnusedFileHandle = 0xFF;
    private const int FcbSize = 16;
    private const int FcbFilenameLength = 11;
    private const int FcbMetadataStartIndex = 12;
    private const byte FcbFilenamePaddingByte = 0x20;
    private const byte FcbUnusedDriveMarker = 0xFF;
    private const ushort FirstFcbInvalidMask = 0x00FF;
    private const ushort SecondFcbInvalidMask = 0xFF00;
    private const ushort ComDefaultStackPointer = 0xFFFE;
    private const int ParagraphSizeBytes = 16;
    private const int ParagraphRoundingMask = ParagraphSizeBytes - 1;
    private const ushort NullSegment = 0x0000;
    private const ushort NullOffset = 0x0000;
    private const ushort SentinelSegment = 0xFFFF;
    private const ushort SentinelOffset = 0xFFFF;
    public const int MaximumEnvironmentScanLength = 32768;
    internal const int EnvironmentKeepFreeBytes = 0x83;
    private const string RootCommandPath = "C:\\COMMAND.COM";
    private const ushort ExecRegisterContractCxValue = 0x00FF;
    private const ushort ExecRegisterContractBpValue = 0x091E;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly Stack _stack;

    /// <summary>
    /// The master environment block that all DOS PSPs inherit.
    /// </summary>
    /// <remarks>
    /// Not stored in emulated memory, so no one can modify it.
    /// </remarks>
    private readonly EnvironmentVariables _environmentVariables;

    /// <summary>
    /// Builds the DOS process manager with the components required to create, clone, and tear down PSPs while managing file handles and environment blocks.
    /// </summary>
    /// <param name="memory">The emulated memory interface used to read and write PSP and environment data.</param>
    /// <param name="stack">The CPU stack, used to get the parent process CS and IP.</param>
    /// <param name="state">The CPU state that is adjusted during EXEC operations.</param>
    /// <param name="dosPspTracker">Tracks the current and historical PSP segments to maintain parent-child relationships.</param>
    /// <param name="dosMemoryManager">Allocates and frees DOS memory control blocks for PSPs and environments.</param>
    /// <param name="dosFileManager">Resolves DOS paths and manages open file tables shared across processes.</param>
    /// <param name="dosDriveManager">Manages the DOS drive system and resolves paths.</param>
    /// <param name="batchFileManager">Manages the execution stack of batch files.</param>
    /// <param name="envVars">The initial host environment variables to seed the master environment block.</param>
    /// <param name="loggerService">Logger for emitting diagnostic information during process lifecycle changes.</param>
    public DosProcessManager(IMemory memory, Stack stack, State state,
        DosProgramSegmentPrefixTracker dosPspTracker, DosMemoryManager dosMemoryManager,
        DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        BatchFileManager batchFileManager, IDictionary<string, string> envVars, 
        ILoggerService loggerService) {
        _memory = memory;
        _pspTracker = dosPspTracker;
        _memoryManager = dosMemoryManager;
        _stack = stack;
        _fileManager = dosFileManager;
        _driveManager = dosDriveManager;
        _batchFileManager = batchFileManager;
        _state = state;
        _loggerService = loggerService;
        _environmentVariables = new();
        _interruptVectorTable = new(memory);

        if (!envVars.ContainsKey("PATH")) {
            envVars.Add("PATH", $"{_driveManager.CurrentDrive.DosVolume}{DosPathResolver.DirectorySeparatorChar}");
        }

        foreach (KeyValuePair<string, string> envVar in envVars) {
            _environmentVariables.Add(envVar.Key, envVar.Value);
        }
    }

    internal BatchFileManager BatchFileManager => _batchFileManager;

    internal DosFileManager FileManager => _fileManager;

    internal DosDriveManager DriveManager => _driveManager;

    internal EnvironmentVariables EnvironmentVariables => _environmentVariables;

    /// <summary>
    /// Gets the PSP tracker for managing program segment prefixes.
    /// </summary>
    public DosProgramSegmentPrefixTracker PspTracker => _pspTracker;

    public void TrackResidentBlock(ushort pspSegment, DosMemoryControlBlock block) {
        ushort mcbSegment = (ushort)(block.DataBlockSegment - 1);
        _pendingResidentBlocks[pspSegment] = new ResidentBlockInfo(mcbSegment);
    }

    /// <summary>
    /// Gets or sets the return code of the last terminated child process.
    /// </summary>
    /// <remarks>
    /// The low byte (AL) contains the exit code, and the high byte (AH) contains
    /// the termination type. See <see cref="DosTerminationType"/> for termination types.
    /// </remarks>
    public ushort LastChildReturnCode { get; set; }

    /// <summary>
    /// Creates the root COMMAND.COM PSP that acts as the parent for all programs.
    /// </summary>
    public DosProgramSegmentPrefix CreateRootCommandComPsp() {
        DosProgramSegmentPrefix rootPsp = _pspTracker.PushPspSegment(CommandComSegment);

        rootPsp.Exit[0] = IntOpcode;
        rootPsp.Exit[1] = Int20TerminateNumber;
        rootPsp.CurrentSize = DosMemoryManager.LastFreeSegment;

        // Root PSP: parent points to itself
        rootPsp.ParentProgramSegmentPrefix = CommandComSegment;
        rootPsp.PreviousPspAddress = MemoryUtils.To32BitAddress(SentinelSegment, SentinelOffset);

        rootPsp.FarCall = FarCallOpcode;
        rootPsp.CpmServiceRequestAddress = MemoryUtils.To32BitAddress(CommandComSegment, Call5StubOffset);
        rootPsp.Service[0] = IntOpcode;
        rootPsp.Service[1] = Int21Number;
        rootPsp.Service[2] = RetfOpcode;

        // Initialize interrupt vectors from IVT so child PSPs inherit proper addresses
        SegmentedAddress int22 = _interruptVectorTable[TerminateVectorNumber];
        rootPsp.TerminateAddress = MemoryUtils.To32BitAddress(int22.Segment, int22.Offset);

        SegmentedAddress int23 = _interruptVectorTable[CtrlBreakVectorNumber];
        rootPsp.BreakAddress = MemoryUtils.To32BitAddress(int23.Segment, int23.Offset);

        SegmentedAddress int24 = _interruptVectorTable[CriticalErrorVectorNumber];
        rootPsp.CriticalErrorAddress = MemoryUtils.To32BitAddress(int24.Segment, int24.Offset);

        rootPsp.MaximumOpenFiles = DosFileManager.MaxOpenFilesPerProcess;
        rootPsp.FileTableAddress = MemoryUtils.To32BitAddress(CommandComSegment, FileTableOffset);

        rootPsp.DosVersionMajor = DefaultDosVersionMajor;
        rootPsp.DosVersionMinor = DefaultDosVersionMinor;

        // Initialize standard file handles in the PSP file handle table
        // Standard handles: 0=stdin, 1=stdout, 2=stderr, 3=stdaux, 4=stdprn
        // These correspond to the devices opened in Dos.OpenDefaultFileHandles()
        for (byte i = 0; i < StandardFileHandleCount; i++) {
            rootPsp.Files[i] = i;
        }
        // Mark remaining handles as unused
        for (int i = StandardFileHandleCount; i < JobFileTableLength; i++) {
            rootPsp.Files[i] = UnusedFileHandle;
        }

        ResetFcb(rootPsp.FirstFileControlBlock);
        ResetFcb(rootPsp.SecondFileControlBlock);
        InitializeRootEnvironment(rootPsp, CreateEnvironmentBlock(RootCommandPath));

        _fileManager.SetDiskTransferAreaAddress(
            CommandComSegment, DosCommandTail.OffsetInPspSegment);

        //We 'executed' COMMAND.COM - setup CPU registers to reflect that, before launching the emulated program.
        SetupCpuRegistersForComFileExecution(CommandComSegment);
        return rootPsp;
    }

    /// <summary>
    /// Marks the next loaded program as the initial program.
    /// When this program terminates, the emulator will halt instead of trying to return to the parent.
    /// This is used by DosProgramLoader to indicate that there's no real parent process to return to.
    /// </summary>
    internal void MarkNextProgramAsInitial() {
        // Store a special marker value to indicate the next PSP should be marked as initial
        // We use 0xFFFF as a marker because it's not a valid PSP segment
        _initialProgramPspSegment = 0xFFFF;
    }


    /// <summary>
    /// Implements INT 21h AH=4Bh loading logic: resolves the DOS path, allocates memory, builds a PSP, loads EXE or COM images, updates CPU registers, and optionally executes or returns load metadata.
    /// </summary>
    /// <param name="programName">DOS path to the program to load.</param>
    /// <param name="paramBlock">The EXEC parameter block containing FCB pointers and initial register outputs.</param>
    /// <param name="commandTail">The command tail passed to the child process.</param>
    /// <param name="loadType">Whether to load-only or load-and-execute.</param>
    /// <param name="environmentSegment">Optional environment block to inherit; 0 clones the parent's environment.</param>
    /// <returns>EXEC result metadata indicating success, failure code, and entry register values.</returns>
    public DosExecResult LoadOrLoadAndExecute(string programName, DosExecParameterBlock paramBlock,
        string commandTail, DosExecLoadType loadType, ushort environmentSegment) {
        // We read CS:IP from the stack to get the address where the parent will resume.
        ushort callerIP = _stack.Peek16(0);
        ushort callerCS = _stack.Peek16(2);

        ushort parentPspSegment = _pspTracker.GetCurrentPspSegment();
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("EXEC: Current PSP={CurrentPsp:X4}, will become parent",
                parentPspSegment);
        }

        string? hostPath = _fileManager.TryGetFullHostPathFromDos(programName) ?? programName;
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath)) {
            return DosExecResult.Fail(DosErrorCode.FileNotFound);
        }

        byte[] fileBytes = File.ReadAllBytes(hostPath);

        // Save parent's current SS:SP BEFORE any CPU state changes
        // This captures the parent's stack context before the child modifies anything
        uint parentStackPointer = _state.StackPhysicalAddress;

        // Allocate environment block FIRST before allocating program memory, as we might decide to take ALL the remaining free memory
        DosMemoryControlBlock? envBlock = null;
        if (environmentSegment == 0 && !TryAllocateEnvironmentBlock(parentPspSegment, hostPath, out envBlock)) {
            return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
        }
        if(fileBytes.Length >= DosExeFile.MinExeSize) {
            // Try to load as EXE first if it looks like an EXE file
            DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            if (exeFile.IsValid) {
                return HandleExeFileLoading(paramBlock, commandTail, loadType,
                    environmentSegment, callerIP, callerCS, parentPspSegment, hostPath,
                    parentStackPointer, envBlock, exeFile);
            }
        }

        // If file has .EXE signature but isn't a valid EXE, fall through to try COM loading
        // This matches FreeDOS behavior
        return HandleComFileLoading(paramBlock, commandTail, loadType,
            environmentSegment, callerIP, callerCS, parentPspSegment,
            hostPath, fileBytes, parentStackPointer, envBlock);
    }

    private DosExecResult HandleExeFileLoading(DosExecParameterBlock paramBlock,
        string commandTail, DosExecLoadType loadType, ushort environmentSegment,
        ushort callerIP, ushort callerCS, ushort parentPspSegment, string hostPath,
        uint parentStackPointer, DosMemoryControlBlock? envBlock, DosExeFile exeFile) {
        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);
        if (block is null) {
            // Free the environment block we just allocated
            if (envBlock is not null) {
                _memoryManager.FreeMemoryBlock(envBlock);
            }
            return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
        }

        SetMemoryBlockOwnership(hostPath, envBlock, block);

        // Use the pre-allocated environment segment or 0 if caller provided one
        ushort finalEnvironmentSegment = envBlock?.DataBlockSegment ?? environmentSegment;
        InitializePsp(block.DataBlockSegment, hostPath, commandTail,
            finalEnvironmentSegment, parentPspSegment,
            callerCS, callerIP, loadType == DosExecLoadType.LoadAndExecute);

        DosProgramSegmentPrefix exePsp = new(_memory, MemoryUtils.ToPhysicalAddress(block.DataBlockSegment, 0));
        CopyFcbFromPointer(paramBlock.FirstFcbPointer, exePsp.FirstFileControlBlock);
        CopyFcbFromPointer(paramBlock.SecondFcbPointer, exePsp.SecondFileControlBlock);

        LoadExeFile(exeFile, block.DataBlockSegment, block, loadType == DosExecLoadType.LoadAndExecute);

        ushort loadImageSegment = (ushort)(block.DataBlockSegment + DosProgramSegmentPrefix.PspSizeInParagraphs);
        if (exeFile.MinAlloc == 0 && exeFile.MaxAlloc == 0) {
            ushort imageDistanceInParagraphs = (ushort)(block.Size - exeFile.ProgramSizeInParagraphsPerHeader);
            loadImageSegment = (ushort)(block.DataBlockSegment + imageDistanceInParagraphs);
        }

        SetupAxAndBxWithFcbValuesForExecute(loadType, exePsp);

        RestoreParentPspForLoadOnly(loadType, parentPspSegment);

        DosExecResult exeResult;
        if (loadType == DosExecLoadType.LoadOnly) {
            exeResult = DosExecResult.SuccessLoadOnly((ushort)(exeFile.InitCS + loadImageSegment), exeFile.InitIP,
            (ushort)(exeFile.InitSS + loadImageSegment), exeFile.InitSP);
        } else {
            exeResult = DosExecResult.SuccessExecute((ushort)(exeFile.InitCS + loadImageSegment), exeFile.InitIP,
            (ushort)(exeFile.InitSS + loadImageSegment), exeFile.InitSP);
        }

        // For AL=01 (Load Only), DOS fills the EPB with initial CS:IP and SS:SP.
        FillEPBForLoadOnlyMode(paramBlock, loadType, exeResult);

        return exeResult;
    }


    private DosExecResult HandleComFileLoading(DosExecParameterBlock paramBlock,
        string commandTail, DosExecLoadType loadType, ushort environmentSegment,
        ushort callerIP, ushort callerCS, ushort parentPspSegment, string hostPath,
        byte[] fileBytes, uint parentStackPointer, DosMemoryControlBlock? envBlock) {
        ushort paragraphsNeeded = CalculateParagraphsNeeded(DosProgramSegmentPrefix.PspSize + fileBytes.Length);
        DosMemoryControlBlock? comBlock = _memoryManager.AllocateMemoryBlock(paragraphsNeeded);
        if (comBlock is null) {
            //Free the environment block we just allocated
            if (envBlock is not null) {
                _memoryManager.FreeMemoryBlock(envBlock);
            }
            return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
        }

        SetMemoryBlockOwnership(hostPath, envBlock, comBlock);

        // Use the pre-allocated environment segment or 0 if caller provided one
        ushort comFinalEnvironmentSegment = envBlock?.DataBlockSegment ?? environmentSegment;
        InitializePsp(comBlock.DataBlockSegment, hostPath, commandTail,
            comFinalEnvironmentSegment, parentPspSegment,
            callerCS, callerIP, loadType == DosExecLoadType.LoadAndExecute);

        DosProgramSegmentPrefix comPsp = new(_memory, MemoryUtils.ToPhysicalAddress(comBlock.DataBlockSegment, 0));
        CopyFcbFromPointer(paramBlock.FirstFcbPointer, comPsp.FirstFileControlBlock);
        CopyFcbFromPointer(paramBlock.SecondFcbPointer, comPsp.SecondFileControlBlock);

        LoadComFile(fileBytes, comBlock.DataBlockSegment, loadType == DosExecLoadType.LoadAndExecute);

        DosExecResult comResult = loadType == DosExecLoadType.LoadOnly
            ? DosExecResult.SuccessLoadOnly(comBlock.DataBlockSegment, DosProgramSegmentPrefix.PspSize,
                comBlock.DataBlockSegment, ComDefaultStackPointer)
            : DosExecResult.SuccessExecute(comBlock.DataBlockSegment, DosProgramSegmentPrefix.PspSize,
                comBlock.DataBlockSegment, ComDefaultStackPointer);

        RestoreParentPspForLoadOnly(loadType, parentPspSegment);
        FillEPBForLoadOnlyMode(paramBlock, loadType, comResult);
        SetupAxAndBxWithFcbValuesForExecute(loadType, comPsp);

        return comResult;
    }

    private bool TryAllocateEnvironmentBlock(ushort parentPspSegment, string hostPath, out DosMemoryControlBlock? envBlock) {
        DosProgramSegmentPrefix parentPsp = new(_memory, MemoryUtils.ToPhysicalAddress(parentPspSegment, 0));
        ushort sourceEnvironmentSegment = parentPsp.EnvironmentTableSegment;

        byte[] environmentBlock;
        if (sourceEnvironmentSegment != 0) {
            environmentBlock = CreateEnvironmentBlockFromParent(sourceEnvironmentSegment, hostPath);
        } else {
            environmentBlock = CreateEnvironmentBlock(hostPath);
        }

        int bytesToAllocate = environmentBlock.Length + EnvironmentKeepFreeBytes;
        ushort envParagraphsNeeded = CalculateParagraphsNeeded(bytesToAllocate);

        envBlock = _memoryManager.AllocateMemoryBlock(envParagraphsNeeded);
        if (envBlock == null) {
            return false;
        }

        _memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlock.DataBlockSegment, 0), environmentBlock);
        envBlock.Owner = BuildMcbOwnerName(hostPath);
        return true;
    }

    private static void SetMemoryBlockOwnership(string hostPath, DosMemoryControlBlock? envBlock, DosMemoryControlBlock block) {
        // Mark the MCB with the owning PSP segment, matching DOS 4+ behavior.
        block.PspSegment = block.DataBlockSegment;
        block.Owner = BuildMcbOwnerName(hostPath);

        // Set environment block ownership to the child PSP
        envBlock?.PspSegment = block.DataBlockSegment;
    }

    private void SetupAxAndBxWithFcbValuesForExecute(DosExecLoadType loadType, DosProgramSegmentPrefix psp) {
        ushort fcbCode = ComputeFcbCode(psp);
        if (loadType == DosExecLoadType.LoadAndExecute) {
            _state.AX = fcbCode;
            _state.BX = fcbCode;
        }
    }

    private void RestoreParentPspForLoadOnly(DosExecLoadType loadType, ushort parentPspSegment) {
        if (loadType == DosExecLoadType.LoadOnly) {
            // LoadOnly: restore parent PSP as current
            // Do NOT modify CS:IP/SS:SP - the IRET instruction will restore them from the stack
            _pspTracker.SetCurrentPspSegment(parentPspSegment);
        }
    }

    private static void FillEPBForLoadOnlyMode(DosExecParameterBlock paramBlock, DosExecLoadType loadType, DosExecResult execResult) {
        if (loadType == DosExecLoadType.LoadOnly) {
            paramBlock.InitialCS = execResult.InitialCS;
            paramBlock.InitialIP = execResult.InitialIP;
            paramBlock.InitialSS = execResult.InitialSS;
            paramBlock.InitialSP = execResult.InitialSP;
        }
    }

    /// <summary>
    /// Loads an EXE overlay at a specified segment and applies relocation using the provided factor without altering the current PSP or CPU entry state.
    /// </summary>
    /// <param name="programName">DOS path of the overlay module.</param>
    /// <param name="loadSegment">Segment where the overlay image should be written.</param>
    /// <param name="relocationFactor">Relocation adjustment applied to each relocation entry.</param>
    /// <returns>A result indicating success or the DOS error encountered.</returns>
    public DosExecResult LoadOverlay(string programName, ushort loadSegment, ushort relocationFactor) {
        string? hostPath = _fileManager.TryGetFullHostPathFromDos(programName) ?? programName;
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath)) {
            return DosExecResult.Fail(DosErrorCode.FileNotFound);
        }

        byte[] fileBytes = File.ReadAllBytes(hostPath);

        if (fileBytes.Length < DosExeFile.MinExeSize) {
            return DosExecResult.Fail(DosErrorCode.FormatInvalid);
        }

        DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
        if (!exeFile.IsValid) {
            return DosExecResult.Fail(DosErrorCode.FormatInvalid);
        }

        // For overlays, load at loadSegment but relocate using relocationFactor
        // This matches FreeDOS behavior
        LoadExeFileInMemoryAndApplyRelocations(exeFile, loadSegment, relocationFactor);

        // For overlays, DOS doesn't return anything in the parameter block
        // Just return success with AX=0 and DX=0
        return DosExecResult.SuccessLoadOverlay();
    }

    /// <summary>
    /// Terminates the current DOS process, records the return code for the parent, restores interrupt vectors, and optionally frees memory depending on the termination type.
    /// </summary>
    /// <param name="exitCode">The DOS exit code to report to the parent in AL.</param>
    /// <param name="terminationType">The termination category stored in AH (normal, error, TSR).</param>
    public void TerminateProcess(byte exitCode, DosTerminationType terminationType) {

        // Store the return code for parent to retrieve via INT 21h AH=4Dh
        // Format: AH = termination type, AL = exit code
        LastChildReturnCode = (ushort)(((ushort)terminationType << 8) | exitCode);

        DosProgramSegmentPrefix currentPsp = _pspTracker.GetCurrentPsp();

        ushort currentPspSegment = _pspTracker.GetCurrentPspSegment();
        ushort parentPspSegment = currentPsp.ParentProgramSegmentPrefix;
        bool hasResidentBlock = _pendingResidentBlocks.TryGetValue(currentPspSegment, out ResidentBlockInfo residentBlockInfo);
        DosMemoryControlBlock? residentBlock = null;
        ushort? residentNextSegment = null;

        if (hasResidentBlock) {
            _pendingResidentBlocks.Remove(currentPspSegment);
            residentBlock = new DosMemoryControlBlock(_memory, MemoryUtils.ToPhysicalAddress(residentBlockInfo.McbSegment, 0));
        }

        // Check if this is the root process (current PSP = parent PSP, which means this IS the shell itself terminating)
        // In that case, there's no parent to return to
        bool isRootProcess = currentPspSegment == parentPspSegment;

        // If this is a child process (not the main program) with a real parent, we have a parent to return to
        // This includes children of COMMAND.COM (0x60) for batch file resumption
        bool hasParentToReturnTo = !isRootProcess && _pspTracker.PspCount > 1;

        // Close non-standard handles only when the process fully terminates; TSR keeps them resident
        if (terminationType != DosTerminationType.TSR) {
            CloseProcessFileHandles(currentPsp);
        }

        // Cache interrupt vectors from child PSP before freeing memory
        uint terminateAddr = currentPsp.TerminateAddress;
        uint breakAddr = currentPsp.BreakAddress;
        uint criticalErrorAddr = currentPsp.CriticalErrorAddress;

        // This follows FreeDOS
        // TSR (term_type == 3) does NOT free memory - it keeps the program resident
        if (terminationType != DosTerminationType.TSR) {
            _memoryManager.FreeProcessMemory(currentPspSegment);
        } else {
            _memoryManager.FreeEnvironmentBlock(currentPsp.EnvironmentTableSegment, currentPspSegment);
            if (residentBlock is not null) {
                residentBlock.PspSegment = currentPspSegment;
                residentNextSegment = (ushort)(residentBlock.DataBlockSegment + residentBlock.Size);
                currentPsp.CurrentSize = residentNextSegment.Value;
            }
        }

        // Restore interrupt vectors from cached values
        RestoreInterruptVector(TerminateVectorNumber, terminateAddr);
        RestoreInterruptVector(CtrlBreakVectorNumber, breakAddr);
        RestoreInterruptVector(CriticalErrorVectorNumber, criticalErrorAddr);

        _pspTracker.PopCurrentPspSegment();

        DosProgramSegmentPrefix? parentPspOptional = _pspTracker.PspCount > 0 ? _pspTracker.GetCurrentPsp() : null;

        if (residentNextSegment.HasValue && parentPspOptional is not null) {
            parentPspOptional.CurrentSize = residentNextSegment.Value;
        }

        // Check if this is the initial program loaded by DosProgramLoader
        // If so, halt the emulator instead of trying to return to the parent
        bool isInitialProgram = _initialProgramPspSegment.HasValue && currentPspSegment == _initialProgramPspSegment.Value;
        if (isInitialProgram) {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("TerminateProcess: Initial program PSP {Psp:X4} terminated. Halting emulator.", currentPspSegment);
            }
            _state.IsRunning = false;
            return;
        }

        bool hasSavedParentStackPointer = _pendingParentStackPointers.TryGetValue(currentPspSegment, out (ushort SS, ushort SP) savedParentStackPointer);
        if (hasSavedParentStackPointer) {
            _pendingParentStackPointers.Remove(currentPspSegment);
        }

        // Check if we have a parent but no saved stack pointer (e.g., initial program loaded by DosProgramLoader)
        // In this case, we cannot properly restore the parent context, so halt the emulator
        if (hasParentToReturnTo && !hasSavedParentStackPointer) {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("TerminateProcess: Child PSP {ChildPsp:X4} has parent {ParentPsp:X4} but no saved stack pointer. Halting emulator.",
                    currentPspSegment, parentPspSegment);
            }
            _state.IsRunning = false;
            return;
        }

        if (hasParentToReturnTo && parentPspOptional is not null && hasSavedParentStackPointer) {
            DosProgramSegmentPrefix parentPsp = parentPspOptional;
            // Restore parent's SS:SP that was saved during EXEC.
            // This points to the BASE of the 18-byte register save area (see InitializePsp comment).
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("TerminateProcess: Restoring parent stack from saved value: SS:SP={SavedSS:X4}:{SavedSP:X4}",
                    savedParentStackPointer.SS, savedParentStackPointer.SP);
            }
            _state.SS = savedParentStackPointer.SS;
            _state.SP = savedParentStackPointer.SP;
            
            // Log after setting SS:SP
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("TerminateProcess: After SS:SP assignment: SS:SP={SS:X4}:{SP:X4}, physical={Phys:X}",
                    _state.SS, _state.SP, MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP));
            }

            // RESTORE PARENT'S REGISTERS FROM STACK:
            //
            // DOS process termination protocol (matching DOSBox dos_execute.cpp:114-160):
            // 1. Restore SS:SP to point to base of register save area (done above)
            // 2. Read saved register values from [SP+0] through [SP+16]
            // 3. Restore those registers to CPU state
            // 4. Advance SP by 18 to point past the save area
            // 5. Write INT 22h terminate address at new [SP+0] and [SP+2]
            // 6. Write flags at [SP+4]
            // 7. IRET pops this reconstructed interrupt frame and resumes parent
            //
            // This matches real DOS behavior and is essential for programs that depend
            // on preserved register values across EXEC calls.
            
            uint stackPhysicalAddress = _state.StackPhysicalAddress;
            
            // Step 2-3: Read and restore saved registers from stack
            // (DOSBox dos_execute.cpp lines 150-158)
            _state.AX = _memory.UInt16[stackPhysicalAddress + 0];   // Offset 0
            _state.BX = _memory.UInt16[stackPhysicalAddress + 2];   // Offset 2
            _state.CX = _memory.UInt16[stackPhysicalAddress + 4];   // Offset 4
            _state.DX = _memory.UInt16[stackPhysicalAddress + 6];   // Offset 6
            _state.SI = _memory.UInt16[stackPhysicalAddress + 8];   // Offset 8
            _state.DI = _memory.UInt16[stackPhysicalAddress + 10];  // Offset 10
            _state.BP = _memory.UInt16[stackPhysicalAddress + 12];  // Offset 12
            _state.DS = _memory.UInt16[stackPhysicalAddress + 14];  // Offset 14
            _state.ES = _memory.UInt16[stackPhysicalAddress + 16];  // Offset 16
            
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("TerminateProcess: Restored parent registers: AX={AX:X4} BX={BX:X4} CX={CX:X4} DX={DX:X4} DS={DS:X4} ES={ES:X4}",
                    _state.AX, _state.BX, _state.CX, _state.DX, _state.DS, _state.ES);
            }
            
            // Step 4: Restore parent's original SP by advancing past the register save area
            // The saved SP points to the BASE of the 18-byte register save area.
            // To restore the parent's original SP, we need to advance by 18.
            // However, we must ensure this doesn't cause segment wrapping during IRET frame reads.
            // IRET will call PopSegmentedAddress which reads from [SP] and [SP+2],
            // followed by Pop16 at [SP+4]. For safety, we need SP + 6 < 0x10000.
            const ushort RegisterSaveAreaSize = 18;
            const ushort MaxSafeOffset = 0xFFF9;  // Ensures SP + 6 < 0x10000
            
            ushort restoredSP = (ushort)(_state.SP + RegisterSaveAreaSize);
            
            // Check if advancing would cause wrapping or exceed safe bounds
            if (restoredSP <= MaxSafeOffset) {
                // Safe to advance to parent's original SP
                _state.SP = restoredSP;
                stackPhysicalAddress += RegisterSaveAreaSize;
            } else {
                // Wrapping would occur. Use a safe SP value below 0xFFF9.
                // We'll write the frame at the current location (saved SP) instead.
                // The parent will resume with SP at the base of the register save area + 6,
                // which is slightly different from the original but avoids memory corruption.
                // This is acceptable because the registers have been restored and the
                // frame will be properly popped by IRET.
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("TerminateProcess: Parent SP {SP:X4} + 18 would exceed safe bounds. Keeping SP at save area base to avoid segment wrapping.",
                        _state.SP);
                }
                // stackPhysicalAddress stays at the register save area base
            }
            
            // Update PSP with the restored/advanced SP value
            parentPsp.StackPointer = MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP);

            ushort terminateSegment = MemoryUtils.GetHighWord(terminateAddr);
            ushort terminateOffset = MemoryUtils.GetLowWord(terminateAddr);

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Program {ChildPsp:X4} terminating: terminateAddr={TermAddr:X8} extracted as seg={Seg:X4} off={Off:X4}, returning to parent {ParentPsp:X4}, SS:SP now={StackSeg:X4}:{StackPtr:X4}",
                    currentPspSegment, terminateAddr, terminateSegment, terminateOffset, parentPspSegment, _state.SS, _state.SP);
            }

            // Step 5-6: Write interrupt frame for IRET
            // The IRET instruction will execute from assembly code at F000:XXXX after our callback returns.
            // It will pop from the CURRENT SP location (which we just set to parent_base + 18).
            // We write the terminate address at this location so IRET resumes the parent.
            
            // Log before frame write
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Before frame write: CPU SS:SP={SS:X4}:{SP:X4}, will write to phys={Phys:X}, CPU StackPhysicalAddress={CpuPhys:X}",
                    _state.SS, _state.SP, stackPhysicalAddress, _state.StackPhysicalAddress);
            }
            
            _memory.UInt16[stackPhysicalAddress + 0] = terminateOffset;     // IP at [SP+0]
            _memory.UInt16[stackPhysicalAddress + 2] = terminateSegment;    // CS at [SP+2]
            _memory.UInt16[stackPhysicalAddress + 4] = 0x7202;              // FLAGS at [SP+4] - IOPL=3, IF=1, nested task cleared

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                // Verify what we wrote
                ushort verifyIP = _memory.UInt16[stackPhysicalAddress + 0];
                ushort verifyCS = _memory.UInt16[stackPhysicalAddress + 2];
                ushort verifyFlags = _memory.UInt16[stackPhysicalAddress + 4];
                _loggerService.Verbose("Wrote terminate frame at SS:SP={SS:X4}:{SP:X4} (phys={Phys:X}): CS={CS:X4} IP={IP:X4} flags={Flags:X4}",
                    _state.SS, _state.SP, stackPhysicalAddress, verifyCS, verifyIP, verifyFlags);
                _loggerService.Verbose("TerminateProcess ending: IRET will pop from SS:SP={SS:X4}:{SP:X4} and resume at {CS:X4}:{IP:X4}",
                    _state.SS, _state.SP, verifyCS, verifyIP);
            }

            // DON'T manually set CS:IP registers - let IRET handle it by popping the modified frame
        }

        if (!hasParentToReturnTo) {
            // When returning to COMMAND.COM with no parent to resume, halt emulation
            _state.IsRunning = false;
        }
    }

    /// <summary>
    /// Implements INT 21h, AH=26h by cloning the current PSP to a new segment and patching the parent pointer, DOS version fields, and INT 22h/23h/24h vectors so the child inherits the caller’s termination context.
    /// </summary>
    /// <param name="newPspSegment">The segment address where the new PSP will be created.</param>
    public void CreateNewPsp(ushort newPspSegment) {
        ushort currentPspSegment = _pspTracker.GetCurrentPspSegment();

        uint currentPspAddress = MemoryUtils.ToPhysicalAddress(currentPspSegment, 0);
        uint newPspAddress = MemoryUtils.ToPhysicalAddress(newPspSegment, 0);

        byte[] pspData = _memory.ReadRam(DosProgramSegmentPrefix.MaxLength, currentPspAddress);
        _memory.LoadData(newPspAddress, pspData);

        DosProgramSegmentPrefix currentPsp = _pspTracker.GetCurrentPsp();
        DosProgramSegmentPrefix newPsp = new(_memory, newPspAddress);

        newPsp.Exit[0] = IntOpcode;
        newPsp.Exit[1] = Int20TerminateNumber;

        newPsp.ParentProgramSegmentPrefix = currentPspSegment;
        newPsp.EnvironmentTableSegment = currentPsp.EnvironmentTableSegment;

        SegmentedAddress int22 = _interruptVectorTable[TerminateVectorNumber];
        newPsp.TerminateAddress = MemoryUtils.To32BitAddress(int22.Segment, int22.Offset);

        SegmentedAddress int23 = _interruptVectorTable[CtrlBreakVectorNumber];
        newPsp.BreakAddress = MemoryUtils.To32BitAddress(int23.Segment, int23.Offset);

        SegmentedAddress int24 = _interruptVectorTable[CriticalErrorVectorNumber];
        newPsp.CriticalErrorAddress = MemoryUtils.To32BitAddress(int24.Segment, int24.Offset);

        newPsp.DosVersionMajor = DefaultDosVersionMajor;
        newPsp.DosVersionMinor = DefaultDosVersionMinor;
    }

    /// <summary>
    /// Implements INT 21h, AH=55h by cloning the current PSP to the target segment, wiring parent links, refreshing INT 22h/23h/24h vectors, rebuilding the file table, and clearing FCBs and command tail to FreeDOS defaults.
    /// </summary>
    public void CreateChildPsp(ushort childSegment, ushort sizeInParagraphs) {
        ushort parentPspSegment = _pspTracker.GetCurrentPspSegment();
        uint childPspAddress = MemoryUtils.ToPhysicalAddress(childSegment, 0);
        uint parentPspAddress = MemoryUtils.ToPhysicalAddress(parentPspSegment, 0);

        DosProgramSegmentPrefix parentPsp = new(_memory, parentPspAddress);

        // FreeDOS child_psp starts from a memcpy of the current PSP, then patches fields.
        CloneCurrentPspTo(childSegment);
        DosProgramSegmentPrefix childPsp = new(_memory, childPspAddress);

        // Update vectors like FreeDOS new_psp.
        SaveInterruptVectors(childPsp);

        // Parent/previous links.
        childPsp.ParentProgramSegmentPrefix = parentPspSegment;
        childPsp.PreviousPspAddress = MemoryUtils.To32BitAddress(parentPspSegment, 0);

        // Size/next segment (ps_size in FreeDOS).
        childPsp.CurrentSize = sizeInParagraphs;

        // File table layout and cloning (start unused then clone handles).
        childPsp.MaximumOpenFiles = DosFileManager.MaxOpenFilesPerProcess;
        childPsp.FileTableAddress = MemoryUtils.To32BitAddress(childSegment, FileTableOffset);
        for (int i = 0; i < JobFileTableLength; i++) {
            childPsp.Files[i] = UnusedFileHandle;
        }
        CopyFileTableFromParent(childPsp, parentPsp);

        // FCBs cleared (FreeDOS resets to drive 0, space-filled names).
        ResetFcb(childPsp.FirstFileControlBlock);
        ResetFcb(childPsp.SecondFileControlBlock);

        // Command tail cleared (ctCount=0, CR sentinel set via Command setter).
        childPsp.DosCommandTail.Command = string.Empty;

        // Environment/stack mirror parent.
        childPsp.EnvironmentTableSegment = parentPsp.EnvironmentTableSegment;
        childPsp.StackPointer = parentPsp.StackPointer;

        // Keep INT 21h entry and CP/M far call consistent with FreeDOS child_psp.
        childPsp.FarCall = FarCallOpcode;
        childPsp.CpmServiceRequestAddress = MemoryUtils.To32BitAddress(childSegment, Call5StubOffset);
        childPsp.Service[0] = IntOpcode;
        childPsp.Service[1] = Int21Number;
        childPsp.Service[2] = RetfOpcode;
    }

    private void RestoreInterruptVector(byte vectorNumber, uint storedFarPointer) {
        if (storedFarPointer != 0) {
            ushort offset = (ushort)(storedFarPointer & 0xFFFF);
            ushort segment = (ushort)(storedFarPointer >> 16);
            _interruptVectorTable[vectorNumber] = new SegmentedAddress(segment, offset);
        }
    }

    private void SaveInterruptVectors(DosProgramSegmentPrefix psp) {
        SegmentedAddress int22 = _interruptVectorTable[TerminateVectorNumber];
        psp.TerminateAddress = MemoryUtils.To32BitAddress(int22.Segment, int22.Offset);

        SegmentedAddress int23 = _interruptVectorTable[CtrlBreakVectorNumber];
        psp.BreakAddress = MemoryUtils.To32BitAddress(int23.Segment, int23.Offset);

        SegmentedAddress int24 = _interruptVectorTable[CriticalErrorVectorNumber];
        psp.CriticalErrorAddress = MemoryUtils.To32BitAddress(int24.Segment, int24.Offset);
    }

    private void CopyFileTableFromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
        for (int i = 0; i < JobFileTableLength; i++) {
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

            DosFileOperationResult duplicationResult = _fileManager.DuplicateHandleForChild(parentHandle);
            if (duplicationResult.IsError || duplicationResult.Value == null) {
                childPsp.Files[i] = UnusedFileHandle;
                continue;
            }

            childPsp.Files[i] = (byte)duplicationResult.Value.Value;
        }
    }

    private void CloseProcessFileHandles(DosProgramSegmentPrefix psp) {
        HashSet<byte> closedHandles = new();

        for (int i = StandardFileHandleCount; i < JobFileTableLength; i++) {
            byte handle = psp.Files[i];
            if (handle == UnusedFileHandle) {
                continue;
            }

            if (!closedHandles.Add(handle)) {
                continue;
            }

            DosFileOperationResult result = _fileManager.CloseFileOrDevice(handle);
            if (!result.IsError) {
                psp.Files[i] = UnusedFileHandle;
            }
        }
    }

    private static void ResetFcb(UInt8Array fcb) {
        fcb[0] = 0;
        for (int i = 1; i <= FcbFilenameLength && i < FcbSize; i++) {
            fcb[i] = FcbFilenamePaddingByte;
        }
        for (int i = FcbMetadataStartIndex; i < FcbSize; i++) {
            fcb[i] = 0;
        }
    }

    private void CopyFcbFromPointer(SegmentedAddress pointer, UInt8Array destination) {
        // FreeDOS treats 0000:0000 and FFFF:FFFF as "no FCB" markers.
        if (IsNoFcbPointer(pointer)) {
            return;
        }

        uint source = MemoryUtils.ToPhysicalAddress(pointer.Segment, pointer.Offset);
        for (int i = 0; i < FcbSize; i++) {
            destination[i] = _memory.UInt8[source + (uint)i];
        }
    }

    private static bool IsNoFcbPointer(SegmentedAddress pointer) {
        return (pointer.Segment == NullSegment && pointer.Offset == NullOffset)
            || (pointer.Segment == SentinelSegment && pointer.Offset == SentinelOffset);
    }

    private void CloneCurrentPspTo(ushort destinationSegment) {
        ushort currentPspSegment = _pspTracker.GetCurrentPspSegment();
        uint currentPspAddress = MemoryUtils.ToPhysicalAddress(currentPspSegment, 0);
        uint destinationAddress = MemoryUtils.ToPhysicalAddress(destinationSegment, 0);

        // FreeDOS new_psp copies the entire PSP structure before patching.
        byte[] pspData = _memory.ReadRam(DosProgramSegmentPrefix.PspSize, currentPspAddress);
        _memory.LoadData(destinationAddress, pspData);
    }

    private void ClearPspMemory(ushort pspSegment) {
        uint destinationAddress = MemoryUtils.ToPhysicalAddress(pspSegment, 0);
        byte[] zeros = new byte[DosProgramSegmentPrefix.PspSize];
        _memory.LoadData(destinationAddress, zeros);
    }

    private void InitializeRootEnvironment(DosProgramSegmentPrefix rootPsp, byte[] environmentBlock) {
        // FreeDOS places the root COMMAND.COM environment at a fixed offset (DOS_PSP + 8)
        // This is NOT managed by the memory manager's MCB chain - it's a pre-allocated reserved area
        // The memory manager will start its MCB chain after this reserved space
        ushort environmentSegment = CommandComSegment + RootEnvironmentParagraphOffset;
        int capacityBytes = (DosProgramSegmentPrefix.PspSizeInParagraphs - RootEnvironmentParagraphOffset) * 16;
        byte[] buffer = new byte[capacityBytes];
        Array.Copy(environmentBlock, buffer, Math.Min(environmentBlock.Length, capacityBytes));
        uint environmentAddress = MemoryUtils.ToPhysicalAddress(environmentSegment, 0);
        _memory.LoadData(environmentAddress, buffer);
        rootPsp.EnvironmentTableSegment = environmentSegment;
    }

    private static ushort CalculateParagraphsNeeded(int lengthInBytes) {
        int paragraphs = (lengthInBytes + ParagraphRoundingMask) / ParagraphSizeBytes;
        paragraphs = paragraphs == 0 ? 1 : paragraphs;
        return (ushort)paragraphs;
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
            code |= FirstFcbInvalidMask;
        }

        byte drive2 = psp.SecondFileControlBlock[0];
        if (!IsFcbDriveValid(drive2)) {
            code |= SecondFcbInvalidMask;
        }

        return code;
    }

    private bool IsFcbDriveValid(byte driveByte) {
        if (driveByte == 0) {
            return true;
        }

        if (driveByte == FcbUnusedDriveMarker) {
            return false;
        }

        if (driveByte > DosDriveManager.MaxDriveCount) {
            return false;
        }

        ushort zeroBasedIndex = (ushort)(driveByte - 1);
        return _driveManager.HasDriveAtIndex(zeroBasedIndex);
    }

    private void InitializePsp(ushort pspSegment, string programHostPath,
        string? arguments, ushort environmentSegment, ushort parentPspSegment,
        ushort callerCS, ushort callerIP, bool trackParentStackPointer) {
        ClearPspMemory(pspSegment);
        // Establish parent-child PSP relationship and create the new PSP
        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(pspSegment);

        psp.Exit[0] = IntOpcode;
        psp.Exit[1] = Int20TerminateNumber;
        psp.CurrentSize = DosMemoryManager.LastFreeSegment;

        psp.FarCall = FarCallOpcode;
        psp.CpmServiceRequestAddress = MemoryUtils.To32BitAddress(pspSegment, Call5StubOffset);
        psp.Service[0] = IntOpcode;
        psp.Service[1] = Int21Number;
        psp.Service[2] = RetfOpcode;
        psp.DosVersionMajor = DefaultDosVersionMajor;
        psp.DosVersionMinor = DefaultDosVersionMinor;

        // Set INT 22h terminate vector:
        // - If parent is shell (0x60) AND batch file execution is active, point to shell callback (0060:0100) so batch processing resumes
        // - Otherwise, point to caller's return address as per DOS convention
        if (parentPspSegment == CommandComSegment && _batchFileManager.IsExecutingBatch) {
            // Parent is shell AND batch execution is active - point to shell callback stub for batch resume
            psp.TerminateAddress = MemoryUtils.To32BitAddress(CommandComSegment, 0x0100);
        } else {
            // Normal DOS program or non-batch execution - point to caller's return address
            psp.TerminateAddress = MemoryUtils.To32BitAddress(callerCS, callerIP);
        }

        SegmentedAddress breakVector = _interruptVectorTable[CtrlBreakVectorNumber];
        SegmentedAddress criticalErrorVector = _interruptVectorTable[CriticalErrorVectorNumber];
        psp.BreakAddress = MemoryUtils.To32BitAddress(breakVector.Segment, breakVector.Offset);
        psp.CriticalErrorAddress = MemoryUtils.To32BitAddress(criticalErrorVector.Segment, criticalErrorVector.Offset);

        // Save parent's stack registers (SS:SP) only when the child will run, mirroring FreeDOS load_transfer semantics.
        DosProgramSegmentPrefix parentPsp = new(_memory, MemoryUtils.ToPhysicalAddress(parentPspSegment, 0));
        if (trackParentStackPointer) {
            // CRITICAL FIX FOR BATCH FILE EXECUTION AND PROGRAM TERMINATION:
            // 
            // When DOS EXEC (INT 21h AH=4Bh) loads a child process, it must preserve the parent's CPU state
            // so execution can resume after the child terminates. This includes saving all general-purpose 
            // registers (AX, BX, CX, DX, SI, DI, BP, DS, ES) on the parent's stack = 9 words = 18 bytes.
            //
            // The saved SS:SP must point to the BASE of this 18-byte register save area, NOT to the current
            // stack location (which is inside the INT 21h interrupt frame). Here's why:
            //
            // 1. At EXEC call time:
            //    - INT 21h has pushed FLAGS, CS, IP onto stack (6 bytes, the "interrupt frame")
            //    - Current SP points ABOVE this frame (inside the interrupt handler context)
            //    - We allocate 18 bytes BELOW current SP for register preservation
            //    - Save the ADJUSTED SP (current SP - 18) in parent PSP
            //
            // 2. When child terminates (TerminateProcess):
            //    - Restore SS:SP to the saved value (pointing to base of register save area)
            //    - Read saved registers from [SP+0] through [SP+16]
            //    - Advance SP by 18 (now pointing to where original interrupt frame was)
            //    - Write INT 22h terminate address at [SP+0] and [SP+2]
            //    - Write flags at [SP+4]
            //    - IRET pops this reconstructed interrupt frame → resumes parent at terminate address
            //
            // 3. Why this matters:
            //    - If we DON'T adjust SP by -18 here, we save SP pointing inside the interrupt frame
            //    - On termination, we restore to the WRONG location
            //    - Writing the terminate address overwrites random memory instead of stack frame
            //    - IRET pops garbage values → CPU jumps to invalid address → batch files fail
            //
            // This is standard DOS EXEC/terminate semantics as documented in:
            // - "DOS Programmer's Reference" (Microsoft Press) - INT 21h AH=4Bh specification
            // - "Advanced DOS Programming" (Ray Duncan) - Chapter on process management
            // - MS-DOS 6.0 source code (available via Microsoft Research license)
            // - FreeDOS kernel source: kernel/task.c:load_transfer() function
            //
            // Real-world impact: Without this fix, external programs called from batch files 
            // (e.g., "step1.com" in AUTOEXEC.BAT) cannot return control to the batch processor,
            // causing batch execution to hang or jump to random memory.
            // DOS EXEC register preservation protocol:
            // When a parent calls INT 21h AH=4Bh to execute a child, DOS must preserve the parent's
            // register state so it can resume correctly after the child terminates.
            // 
            // Standard DOS behavior (per MS-DOS 6.0 source and FreeDOS kernel/task.c):
            // 1. Allocate 18 bytes (9 words) on parent's stack for register save area
            // 2. Write current register values: AX, BX, CX, DX, SI, DI, BP, DS, ES
            // 3. Save the adjusted SS:SP (pointing to base of save area) in parent PSP
            // 4. When child terminates, restore registers from save area and jump to INT 22h
            //
            // This is MANDATORY for DOS compatibility - many programs depend on preserved registers.
            const ushort RegisterSaveAreaSize = 18;
            ushort adjustedSP = (ushort)(_state.SP - RegisterSaveAreaSize);
            
            // Write parent's register values to the allocated stack area
            // This matches DOSBox dos_execute.cpp line 468+ and FreeDOS load_transfer()
            uint saveAreaPhysical = MemoryUtils.ToPhysicalAddress(_state.SS, adjustedSP);
            _memory.UInt16[saveAreaPhysical + 0] = _state.AX;   // Offset 0
            _memory.UInt16[saveAreaPhysical + 2] = _state.BX;   // Offset 2
            _memory.UInt16[saveAreaPhysical + 4] = _state.CX;   // Offset 4  
            _memory.UInt16[saveAreaPhysical + 6] = _state.DX;   // Offset 6
            _memory.UInt16[saveAreaPhysical + 8] = _state.SI;   // Offset 8
            _memory.UInt16[saveAreaPhysical + 10] = _state.DI;  // Offset 10
            _memory.UInt16[saveAreaPhysical + 12] = _state.BP;  // Offset 12
            _memory.UInt16[saveAreaPhysical + 14] = _state.DS;  // Offset 14
            _memory.UInt16[saveAreaPhysical + 16] = _state.ES;  // Offset 16
            
            _pendingParentStackPointers[pspSegment] = (_state.SS, adjustedSP);
            
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("InitializePsp: Saved parent registers to stack for child PSP {ChildPsp:X4}: SS:SP={SavedSS:X4}:{SavedSP:X4}, AX={AX:X4} BX={BX:X4} CX={CX:X4} DX={DX:X4}",
                    pspSegment, _state.SS, adjustedSP, _state.AX, _state.BX, _state.CX, _state.DX);
            }
        }

        psp.ParentProgramSegmentPrefix = parentPspSegment;
        psp.MaximumOpenFiles = DosFileManager.MaxOpenFilesPerProcess;
        // file table address points to file table at offset FileTableOffset inside this PSP
        psp.FileTableAddress = MemoryUtils.To32BitAddress(pspSegment, FileTableOffset);
        psp.PreviousPspAddress = MemoryUtils.To32BitAddress(parentPspSegment, 0);

        for (int i = 0; i < psp.Files.Count; i++) {
            psp.Files[i] = (byte)Enums.DosPspFileTableEntry.Unused;
        }

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

        ResetFcb(psp.FirstFileControlBlock);
        ResetFcb(psp.SecondFileControlBlock);

        psp.DosCommandTail.Command = DosCommandTail.PrepareCommandlineString(arguments);

        // Assign the environment segment (already allocated by caller, or parent's environment if 0)
        SetupEnvironmentForProcess(programHostPath, environmentSegment, psp, parentPsp);

        _fileManager.SetDiskTransferAreaAddress(
            pspSegment, DosCommandTail.OffsetInPspSegment);

        // Check if this PSP should be marked as the initial program (set by DosProgramLoader)
        if (_initialProgramPspSegment == 0xFFFF) {
            _initialProgramPspSegment = pspSegment;
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("InitializePsp: Marked PSP {PspSegment:X4} as initial program", pspSegment);
            }
        }
    }

    private void SetupEnvironmentForProcess(string programHostPath,
        ushort environmentSegment, DosProgramSegmentPrefix psp,
        DosProgramSegmentPrefix parentPsp) {
        if (environmentSegment != 0) {
            // Caller supplied an environment segment (already allocated); use it directly and mark ownership.
            psp.EnvironmentTableSegment = environmentSegment;
            ushort mcbSegment = (ushort)(environmentSegment - 1);
            DosMemoryControlBlock existing = new(_memory, MemoryUtils.ToPhysicalAddress(mcbSegment, 0));
            if (existing.IsValid) {
                existing.Owner = BuildMcbOwnerName(programHostPath);
            }
            return;
        }

        // No environment provided, inherit parent's environment segment directly (no allocation)
        ushort sourceEnvironmentSegment = parentPsp.EnvironmentTableSegment;
        psp.EnvironmentTableSegment = sourceEnvironmentSegment;
    }

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

        WriteAdditionalStringCountWord(ms);

        // Get the DOS path for the program (not the host path)
        string dosPath = _fileManager.GetDosProgramPath(programPath);

        // Write the DOS path to the environment block
        byte[] programPathBytes = Encoding.ASCII.GetBytes(dosPath);
        ms.Write(programPathBytes, 0, programPathBytes.Length);
        ms.WriteByte(0); // Null terminator for program path

        return ms.ToArray();
    }

    private static void WriteAdditionalStringCountWord(MemoryStream ms) {
        // MS-DOS format: after the double null, write a WORD with value 1 to indicate
        // that one additional string (the program path) follows.
        // This is the correct DOS format
        ms.WriteByte(1); // Low byte of WORD
        ms.WriteByte(0);   // High byte of WORD
    }

    private byte[] CreateEnvironmentBlockFromParent(ushort environmentSegment, string programPath) {
        using MemoryStream ms = new();

        uint environmentBaseAddress = MemoryUtils.ToPhysicalAddress(environmentSegment, 0);
        int offset = 0;
        bool doubleNullFound = false;
        int maxParentBytes = MaximumEnvironmentScanLength - EnvironmentKeepFreeBytes;

        // Copy the parent's environment variables up to and including the double null terminator.
        while (offset + 1 < MaximumEnvironmentScanLength) {
            if (offset >= maxParentBytes) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Environment block exceeded {Max} bytes, rebuilding from defaults.", maxParentBytes);
                }
                return CreateEnvironmentBlock(programPath);
            }

            byte current = _memory.UInt8[environmentBaseAddress + (uint)offset];
            byte next = _memory.UInt8[environmentBaseAddress + (uint)(offset + 1)];

            ms.WriteByte(current);
            offset++;

            if (current == 0 && next == 0) {
                ms.WriteByte(next);
                doubleNullFound = true;
                break;
            }
        }

        if (!doubleNullFound) {
            return CreateEnvironmentBlock(programPath);
        }

        WriteAdditionalStringCountWord(ms);

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
            SetupCpuRegistersForComFileExecution(pspSegment);
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("COM load register state CS:IP={Cs}:{Ip} DS=ES=SS={Segment} SP={Sp}",
                    ConvertUtils.ToHex16(_state.CS), ConvertUtils.ToHex16(_state.IP), ConvertUtils.ToHex16(pspSegment), ConvertUtils.ToHex16(_state.SP));
            }
        }
    }

    private void SetupCpuRegistersForComFileExecution(ushort pspSegment) {
        _state.CS = pspSegment;
        _state.IP = DosProgramSegmentPrefix.PspSize;
        // Make DS and ES point to the PSP
        _state.DS = pspSegment;
        _state.ES = pspSegment;
        _state.SS = pspSegment;
        _state.SP = ComDefaultStackPointer; // Expected stack pointer value
                                            // INT 21h AH=4Bh register contract documented in RBIL
        _state.DX = pspSegment;
        _state.CX = ExecRegisterContractCxValue;
        _state.BP = ExecRegisterContractBpValue;
        _state.DI = 0;

        _state.InterruptFlag = true;
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

    private void SetupCpuForExe(DosExeFile exeFile, ushort loadSegment, ushort pspSegment) {
        // MS-DOS uses the values in the file header to set the SP and SS registers and
        // adjusts the initial value of the SS register by adding the start-segment
        // address to it.
        _state.SS = (ushort)(exeFile.InitSS + loadSegment);
        _state.SP = exeFile.InitSP;

        // Make DS and ES point to the PSP
        _state.DS = pspSegment;
        _state.ES = pspSegment;

        // INT 21h AH=4Bh register contract documented in RBIL
        _state.DX = pspSegment;
        _state.CX = ExecRegisterContractCxValue;
        _state.BP = ExecRegisterContractBpValue;
        _state.DI = 0;

        _state.InterruptFlag = true;

        // Finally, MS-DOS reads the initial CS and IP values from the program's file
        // header, adjusts the CS register value by adding the start-segment address to
        // it, and transfers control to the program at the adjusted address.
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SetupCpuForExe: InitCS={InitCS:X4}, InitIP={InitIP:X4}, loadSegment={LoadSegment:X4}, final CS={FinalCS:X4}",
                exeFile.InitCS, exeFile.InitIP, loadSegment, (ushort)(exeFile.InitCS + loadSegment));
        }
        SetEntryPoint((ushort)(exeFile.InitCS + loadSegment), exeFile.InitIP);
    }

    private void SetEntryPoint(ushort cs, ushort ip) {
        _state.CS = cs;
        _state.IP = ip;
    }

    private readonly record struct ResidentBlockInfo(ushort McbSegment);
}