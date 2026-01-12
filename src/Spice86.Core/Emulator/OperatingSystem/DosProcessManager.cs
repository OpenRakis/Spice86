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

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Stack = CPU.Stack;

/// <summary>
/// Setups the loading and execution of DOS programs and maintains the DOS PSP chains in memory.
/// </summary>
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
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly ILoggerService _loggerService;
    private readonly Dictionary<ushort, uint> _pendingParentStackPointers = new();
    private readonly Dictionary<ushort, ResidentBlockInfo> _pendingResidentBlocks = new();

    /// <summary>
    /// The segment address where the root COMMAND.COM PSP is created.
    /// Follows FreeDOS convention: at 0x0060, and with no PSP MCB.
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
    private const ushort AdditionalEnvironmentStringsCount = 1;
    private const int MaximumEnvironmentScanLength = 32768;
    internal const int EnvironmentMaximumBytes = MaximumEnvironmentScanLength;
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
    /// <param name="dosDriveManager">Provides drive metadata and current drive context for path resolution.</param>
    /// <param name="envVars">The initial host environment variables to seed the master environment block.</param>
    /// <param name="loggerService">Logger for emitting diagnostic information during process lifecycle changes.</param>
    public DosProcessManager(IMemory memory, Stack stack, State state,
        DosProgramSegmentPrefixTracker dosPspTracker, DosMemoryManager dosMemoryManager,
        DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        IDictionary<string, string> envVars, ILoggerService loggerService) {
        _memory = memory;
        _pspTracker = dosPspTracker;
        _memoryManager = dosMemoryManager;
        _stack = stack;
        _fileManager = dosFileManager;
        _driveManager = dosDriveManager;
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

    public IReadOnlyDictionary<ushort, uint> PendingParentStackPointers => _pendingParentStackPointers;

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
    public void CreateRootCommandComPsp() {
        if (_pspTracker.PspCount > 0) {
            // Root PSP already exists
            return;
        }
        ClearPspMemory(CommandComSegment);
        DosProgramSegmentPrefix rootPsp = _pspTracker.PushPspSegment(CommandComSegment);

        rootPsp.Exit[0] = IntOpcode;
        rootPsp.Exit[1] = Int20TerminateNumber;
        rootPsp.CurrentSize = DosMemoryManager.LastFreeSegment;

        // Root PSP: parent points to itself
        rootPsp.ParentProgramSegmentPrefix = CommandComSegment;
        rootPsp.PreviousPspAddress = MakeFarPointer(SentinelSegment, SentinelOffset);

        rootPsp.FarCall = FarCallOpcode;
        rootPsp.CpmServiceRequestAddress = MakeFarPointer(CommandComSegment, Call5StubOffset);
        rootPsp.Service[0] = IntOpcode;
        rootPsp.Service[1] = Int21Number;
        rootPsp.Service[2] = RetfOpcode;

        // Initialize interrupt vectors from IVT so child PSPs inherit proper addresses
        SegmentedAddress int22 = _interruptVectorTable[TerminateVectorNumber];
        rootPsp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);

        SegmentedAddress int23 = _interruptVectorTable[CtrlBreakVectorNumber];
        rootPsp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);

        SegmentedAddress int24 = _interruptVectorTable[CriticalErrorVectorNumber];
        rootPsp.CriticalErrorAddress = MakeFarPointer(int24.Segment, int24.Offset);

        rootPsp.MaximumOpenFiles = DosFileManager.MaxOpenFilesPerProcess;
        rootPsp.FileTableAddress = MakeFarPointer(CommandComSegment, FileTableOffset);

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
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("EXEC: Loading program={Program}, loadType={LoadType}, envSeg={EnvSeg:X4}",
                programName, loadType, environmentSegment);
        }

        // We read CS:IP from the stack to get the address where the parent will resume.
        ushort callerIP = _stack.Peek16(0);
        ushort callerCS = _stack.Peek16(2);
        ushort callerFlags = _stack.Peek16(4);

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

        byte[] fileBytes = File.ReadAllBytes(hostPath);

        string upperCaseExtension = Path.GetExtension(hostPath).ToUpperInvariant();
        bool isExeCandidate = fileBytes.Length >= DosExeFile.MinExeSize && upperCaseExtension == ".EXE";
        bool isLoadAndExecute = loadType == DosExecLoadType.LoadAndExecute;

        // Save parent's current SS:SP BEFORE any CPU state changes
        // This captures the parent's stack context before the child modifies anything
        uint parentStackPointer = ((uint)_state.SS << 16) | _state.SP;

        // Allocate environment block FIRST before allocating program memory, as we might decide to take ALL the remaining free memory
        DosMemoryControlBlock? envBlock = null;
        if (environmentSegment == 0) {
            // Need to allocate a new environment block
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
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("EXEC: Failed to allocate environment block of {Size} paragraphs", envParagraphsNeeded);
                }
                return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
            }

            _memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlock.DataBlockSegment, 0), environmentBlock);
            envBlock.Owner = BuildMcbOwnerName(hostPath);

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("EXEC: Allocated environment block at {EnvSeg:X4}, size={Size} paragraphs",
                    envBlock.DataBlockSegment, envBlock.Size);
            }
        }

        // Try to load as EXE first if it looks like an EXE file
        if (isExeCandidate) {
            DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            if (exeFile.IsValid) {
                DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);
                if (block is null) {
                    // Free the environment block we just allocated
                    if (envBlock is not null) {
                        _memoryManager.FreeMemoryBlock(envBlock);
                        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("EXEC: Failed to allocate EXE memory, freed environment block at {EnvSeg:X4}",
                                envBlock.DataBlockSegment);
                        }
                    }
                    return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
                }

                // Mark the MCB with the owning PSP segment and label for diagnostics, matching DOS 4+ owner naming.
                block.PspSegment = block.DataBlockSegment;
                block.Owner = BuildMcbOwnerName(hostPath);

                // Set environment block ownership to the child PSP
                if (envBlock is not null) {
                    envBlock.PspSegment = block.DataBlockSegment;
                }

                // Use the pre-allocated environment segment or 0 if caller provided one
                ushort finalEnvironmentSegment = envBlock?.DataBlockSegment ?? environmentSegment;
                InitializePsp(block.DataBlockSegment, hostPath, commandTail,
                    finalEnvironmentSegment, parentPspSegment,
                    parentStackPointer, callerCS, callerIP, isLoadAndExecute);

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
        ushort paragraphsNeeded = CalculateParagraphsNeeded(DosProgramSegmentPrefix.PspSize + fileBytes.Length);
        DosMemoryControlBlock? comBlock = _memoryManager.AllocateMemoryBlock(paragraphsNeeded);
        if (comBlock is null) {
            //Free the environment block we just allocated
            if (envBlock is not null) {
                _memoryManager.FreeMemoryBlock(envBlock);
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("EXEC: Failed to allocate COM memory, freed environment block at {EnvSeg:X4}",
                        envBlock.DataBlockSegment);
                }
            }
            return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
        }

        // Align MCB ownership with the child PSP rather than the parent loader.
        comBlock.PspSegment = comBlock.DataBlockSegment;
        comBlock.Owner = BuildMcbOwnerName(hostPath);

        // Set environment block ownership to the child PSP
        if (envBlock is not null) {
            envBlock.PspSegment = comBlock.DataBlockSegment;
        }

        // Use the pre-allocated environment segment or 0 if caller provided one
        ushort comFinalEnvironmentSegment = envBlock?.DataBlockSegment ?? environmentSegment;
        InitializePsp(comBlock.DataBlockSegment, hostPath, commandTail, comFinalEnvironmentSegment, parentPspSegment, parentStackPointer, callerCS, callerIP, isLoadAndExecute);

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
                comBlock.DataBlockSegment, ComDefaultStackPointer)
            : DosExecResult.SuccessExecute(comBlock.DataBlockSegment, DosProgramSegmentPrefix.PspSize,
                comBlock.DataBlockSegment, ComDefaultStackPointer);

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

    /// <summary>
    /// Loads an EXE overlay at a specified segment and applies relocation using the provided factor without altering the current PSP or CPU entry state.
    /// </summary>
    /// <param name="programName">DOS path of the overlay module.</param>
    /// <param name="loadSegment">Segment where the overlay image should be written.</param>
    /// <param name="relocationFactor">Relocation adjustment applied to each relocation entry.</param>
    /// <returns>A result indicating success or the DOS error encountered.</returns>
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

        byte[] fileBytes = File.ReadAllBytes(hostPath);

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

        // Close non-standard handles only when the process fully terminates; TSR keeps them resident
        if (terminationType != DosTerminationType.TSR) {
            CloseProcessFileHandles(currentPsp, currentPspSegment);
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

        bool hasSavedParentStackPointer = _pendingParentStackPointers.TryGetValue(currentPspSegment, out uint savedParentStackPointer);
        if (hasSavedParentStackPointer) {
            _pendingParentStackPointers.Remove(currentPspSegment);
        }

        if (hasParentToReturnTo && parentPspOptional is not null) {
            DosProgramSegmentPrefix parentPsp = parentPspOptional;
            uint stackPointerToRestore = hasSavedParentStackPointer ? savedParentStackPointer : parentPsp.StackPointer;
            parentPsp.StackPointer = stackPointerToRestore;

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("BEFORE parent stack restore: Current SS:SP={CurrentSs:X4}:{CurrentSp:X4}, CS:IP={CurrentCs:X4}:{CurrentIp:X4}",
                    _state.SS, _state.SP, _state.CS, _state.IP);
                _loggerService.Information("Parent PSP StackPointer field = {ParentStack:X8}, will restore SS:SP to {Ss:X4}:{Sp:X4}",
                    stackPointerToRestore, (ushort)(stackPointerToRestore >> 16), (ushort)(stackPointerToRestore & 0xFFFF));
            }

            // We are in an interrupt handler, so the solution is not in freeDOS but DOSBOX:
            // Restore parent's stack pointer WITHOUT skipping the interrupt frame
            // We'll modify the frame contents so IRET goes to the right place
            _state.SS = (ushort)(stackPointerToRestore >> 16);
            _state.SP = (ushort)(stackPointerToRestore & 0xFFFF);
            _state.DS = parentPspSegment;
            _state.ES = parentPspSegment;

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("AFTER parent stack restore: SS:SP={Ss:X4}:{Sp:X4}, DS={Ds:X4}, ES={Es:X4}",
                    _state.SS, _state.SP, _state.DS, _state.ES);
            }

            // Get the terminate address from INT 22h vector (restored from child PSP)
            SegmentedAddress returnAddress = _interruptVectorTable[TerminateVectorNumber];

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

        if (!hasParentToReturnTo) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("No parent to return to - returning to command.com PSP!");
            }
            // When returning to COMMAND.COM with no parent to resume, halt emulation
            _state.IsRunning = false;
        }
    }

    /// <summary>
    /// Gets the last child process exit code. Used by INT 21h AH=4Dh.
    /// </summary>
    /// <returns>Exit code in AL, termination type in AH (always 0 for normal termination).</returns>

    /// <summary>
    /// Implements INT 21h, AH=26h by cloning the current PSP to a new segment and patching the parent pointer, DOS version fields, and INT 22h/23h/24h vectors so the child inherits the caller’s termination context.
    /// </summary>
    /// <param name="newPspSegment">The segment address where the new PSP will be created.</param>
    public void CreateNewPsp(ushort newPspSegment) {
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

        newPsp.Exit[0] = IntOpcode;
        newPsp.Exit[1] = Int20TerminateNumber;

        newPsp.ParentProgramSegmentPrefix = currentPspSegment;
        newPsp.EnvironmentTableSegment = currentPsp.EnvironmentTableSegment;

        SegmentedAddress int22 = _interruptVectorTable[TerminateVectorNumber];
        newPsp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);

        SegmentedAddress int23 = _interruptVectorTable[CtrlBreakVectorNumber];
        newPsp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);

        SegmentedAddress int24 = _interruptVectorTable[CriticalErrorVectorNumber];
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
    /// Implements INT 21h, AH=55h by cloning the current PSP to the target segment, wiring parent links, refreshing INT 22h/23h/24h vectors, rebuilding the file table, and clearing FCBs and command tail to FreeDOS defaults.
    /// </summary>
    public void CreateChildPsp(ushort childSegment, ushort sizeInParagraphs, InterruptVectorTable interruptVectorTable) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "CreateChildPsp: Creating child PSP at segment {ChildSegment:X4}, size {Size} paragraphs",
                childSegment, sizeInParagraphs);
        }

        ushort parentPspSegment = _pspTracker.GetCurrentPspSegment();
        uint childPspAddress = MemoryUtils.ToPhysicalAddress(childSegment, 0);
        uint parentPspAddress = MemoryUtils.ToPhysicalAddress(parentPspSegment, 0);

        DosProgramSegmentPrefix parentPsp = new(_memory, parentPspAddress);

        // FreeDOS child_psp starts from a memcpy of the current PSP, then patches fields.
        CloneCurrentPspTo(childSegment);
        DosProgramSegmentPrefix childPsp = new(_memory, childPspAddress);

        // Update vectors like FreeDOS new_psp.
        SaveInterruptVectors(childPsp, interruptVectorTable);

        // Parent/previous links.
        childPsp.ParentProgramSegmentPrefix = parentPspSegment;
        childPsp.PreviousPspAddress = MakeFarPointer(parentPspSegment, 0);

        // Size/next segment (ps_size in FreeDOS).
        childPsp.CurrentSize = sizeInParagraphs;

        // File table layout and cloning (start unused then clone handles).
        childPsp.MaximumOpenFiles = DosFileManager.MaxOpenFilesPerProcess;
        childPsp.FileTableAddress = MakeFarPointer(childSegment, FileTableOffset);
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
        childPsp.CpmServiceRequestAddress = MakeFarPointer(childSegment, Call5StubOffset);
        childPsp.Service[0] = IntOpcode;
        childPsp.Service[1] = Int21Number;
        childPsp.Service[2] = RetfOpcode;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug(
                "CreateChildPsp: Parent={Parent:X4}, Env={Env:X4}, NextSeg={Next:X4}",
                parentPspSegment, childPsp.EnvironmentTableSegment, childPsp.CurrentSize);
        }
    }

    private void RestoreInterruptVector(byte vectorNumber, uint storedFarPointer) {
        if (storedFarPointer != 0) {
            ushort offset = (ushort)(storedFarPointer & 0xFFFF);
            ushort segment = (ushort)(storedFarPointer >> 16);
            _interruptVectorTable[vectorNumber] = new SegmentedAddress(segment, offset);
        }
    }
    
    private static void SaveInterruptVectors(DosProgramSegmentPrefix psp, InterruptVectorTable ivt) {
        SegmentedAddress int22 = ivt[TerminateVectorNumber];
        psp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);

        SegmentedAddress int23 = ivt[CtrlBreakVectorNumber];
        psp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);

        SegmentedAddress int24 = ivt[CriticalErrorVectorNumber];
        psp.CriticalErrorAddress = MakeFarPointer(int24.Segment, int24.Offset);
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

    private void CloseProcessFileHandles(DosProgramSegmentPrefix psp, ushort pspSegment) {
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
            if (result.IsError && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Failed to close file handle {Handle} for PSP {PspSegment:X4}", handle, pspSegment);
            }

            psp.Files[i] = UnusedFileHandle;
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
        ushort environmentSegment = (ushort)(CommandComSegment + RootEnvironmentParagraphOffset);
        int capacityBytes = (DosProgramSegmentPrefix.PspSizeInParagraphs - RootEnvironmentParagraphOffset) * 16;
        
        if (environmentBlock.Length > capacityBytes && _loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning(
                "Root environment block truncated from {Original} to {Capacity} bytes to fit inside COMMAND.COM PSP.",
                environmentBlock.Length,
                capacityBytes);
        }

        byte[] buffer = new byte[capacityBytes];
        Array.Copy(environmentBlock, buffer, Math.Min(environmentBlock.Length, capacityBytes));
        uint environmentAddress = MemoryUtils.ToPhysicalAddress(environmentSegment, 0);
        _memory.LoadData(environmentAddress, buffer);
        rootPsp.EnvironmentTableSegment = environmentSegment;
        
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "Root environment placed at fixed segment {EnvSegment:X4} (NOT managed by memory manager)",
                environmentSegment);
        }
    }

    private static uint MakeFarPointer(ushort segment, ushort offset) {
        return (uint)((segment << 16) | offset);
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
        uint parentStackPointer, ushort callerCS, ushort callerIP, bool trackParentStackPointer) {
        ClearPspMemory(pspSegment);
        // Establish parent-child PSP relationship and create the new PSP
        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(pspSegment);

        psp.Exit[0] = IntOpcode;
        psp.Exit[1] = Int20TerminateNumber;
        psp.CurrentSize = DosMemoryManager.LastFreeSegment;

        psp.FarCall = FarCallOpcode;
        psp.CpmServiceRequestAddress = MakeFarPointer(pspSegment, Call5StubOffset);
        psp.Service[0] = IntOpcode;
        psp.Service[1] = Int21Number;
        psp.Service[2] = RetfOpcode;
        psp.DosVersionMajor = DefaultDosVersionMajor;
        psp.DosVersionMinor = DefaultDosVersionMinor;

        // This sets INT 22h to point to the CALLER'S return address
        psp.TerminateAddress = MakeFarPointer(callerCS, callerIP);

        SegmentedAddress breakVector = _interruptVectorTable[CtrlBreakVectorNumber];
        SegmentedAddress criticalErrorVector = _interruptVectorTable[CriticalErrorVectorNumber];
        psp.BreakAddress = MakeFarPointer(breakVector.Segment, breakVector.Offset);
        psp.CriticalErrorAddress = MakeFarPointer(criticalErrorVector.Segment, criticalErrorVector.Offset);

        // Save parent's stack pointer only when the child will run, mirroring FreeDOS load_transfer semantics.
        DosProgramSegmentPrefix parentPsp = new(_memory, MemoryUtils.ToPhysicalAddress(parentPspSegment, 0));
        if (trackParentStackPointer) {
            _pendingParentStackPointers[pspSegment] = parentStackPointer;
        }

        psp.ParentProgramSegmentPrefix = parentPspSegment;
        psp.MaximumOpenFiles = DosFileManager.MaxOpenFilesPerProcess;
        // file table address points to file table at offset FileTableOffset inside this PSP
        psp.FileTableAddress = ((uint)pspSegment << 16) | FileTableOffset;
        psp.PreviousPspAddress = MakeFarPointer(parentPspSegment, 0);

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

        // MS-DOS format: after the double null, write a WORD with value 1 to indicate
        // that one additional string (the program path) follows.
        // This is the correct DOS format
        ms.WriteByte((byte)(AdditionalEnvironmentStringsCount & 0xFF)); // Low byte of WORD
        ms.WriteByte((byte)(AdditionalEnvironmentStringsCount >> 8));   // High byte of WORD

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
                offset++;
                doubleNullFound = true;
                break;
            }
        }

        if (!doubleNullFound) {
            return CreateEnvironmentBlock(programPath);
        }

        // Replace the extra strings section with the current program path.
        ms.WriteByte((byte)(AdditionalEnvironmentStringsCount & 0xFF));
        ms.WriteByte((byte)(AdditionalEnvironmentStringsCount >> 8));

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
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Program entry point is {ProgramEntry}", ConvertUtils.ToSegmentedAddressRepresentation(cs, ip));
        }
    }

    private readonly record struct ResidentBlockInfo(ushort McbSegment);
}