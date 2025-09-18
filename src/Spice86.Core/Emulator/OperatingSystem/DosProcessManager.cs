namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

/// <summary>
/// Manages DOS program loading and execution, implementing INT 21h function 4Bh (EXEC).
/// </summary>
/// <remarks>
/// This class handles the loading, setup, and execution of DOS programs (.COM and .EXE files).
/// It closely follows MS-DOS and DOSBox program execution models, supporting:
/// - Loading and executing programs (function 4B00h)
/// - Loading programs without execution (function 4B01h)
/// - Loading program overlays (function 4B03h)
/// - Process termination with optional TSR support (INT 21h functions 00h, 31h, 4Ch)
/// </remarks>
public class DosProcessManager : DosFileLoader {
    /// <summary>
    /// Standard offset where COM programs are loaded (after the PSP).
    /// </summary>
    private const ushort ComOffset = 0x100;

    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryManager _memoryManager;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly Stack _stack;

    /// <summary>
    /// The master environment block that all DOS PSPs inherit.
    /// </summary>
    private readonly EnvironmentVariables _environmentVariables;

    /// <summary>
    /// Creates a new instance of DosProcessManager
    /// </summary>
    public DosProcessManager(IMemory memory, State state, Stack stack,
        DosProgramSegmentPrefixTracker dosPspTracker, DosMemoryManager dosMemoryManager,
        DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        IDictionary<string, string> envVars, ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _pspTracker = dosPspTracker;
        _memoryManager = dosMemoryManager;
        _fileManager = dosFileManager;
        _driveManager = dosDriveManager;
        _stack = stack;
        _interruptVectorTable = new InterruptVectorTable(memory);
        _environmentVariables = new();

        // Setup initial environment variables
        envVars.Add("PATH", $"{_driveManager.CurrentDrive.DosVolume}{HostFolderFileSystemResolver.DirectorySeparatorChar}");

        foreach (KeyValuePair<string, string> envVar in envVars) {
            _environmentVariables.Add(envVar.Key, envVar.Value);
        }
    }

    /// <summary>
    /// Loads the initial program specified on the command line.
    /// </summary>
    public override byte[] LoadFile(string file, string? arguments) {
        // For initial program load, create a dummy parameter block and call LoadAndOrExecute
        // This path is used when Spice86 first starts and needs to load the initial program
        uint parameterBlockAddress = 0x0; // Not used for initial load

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Loading initial program: {ProgramName}, Args: {Arguments}",
                file, arguments ?? "(none)");
        }

        LoadAndOrExecute(DosExecuteMode.LoadAndExecute, file, parameterBlockAddress, arguments);
        return ReadFile(file);
    }

    /// <summary>
    /// Implements DOS INT 21h Function 4Bh - Load and/or Execute Program
    /// </summary>
    /// <param name="mode">The execution mode (Load and Execute, Load Only, or Load Overlay)</param>
    /// <param name="programName">The program to load</param>
    /// <param name="parameterBlockAddress">Address of the parameter block structure</param>
    /// <param name="initialArguments">Command line arguments (only used for initial program)</param>
    /// <returns>True if successful, false otherwise</returns>
    public bool LoadAndOrExecute(DosExecuteMode mode, string programName,
        uint parameterBlockAddress, string? initialArguments = null) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("DOS EXEC Function called: Mode={Mode}, Program={ProgramName}, ParameterBlock=0x{ParameterBlock:X8}",
                mode, programName, parameterBlockAddress);
        }

        switch (mode) {
            case DosExecuteMode.LoadAndExecute:
            case DosExecuteMode.LoadButDoNotRun:
                return ExecuteLoadAndExecuteMode(programName, parameterBlockAddress, mode, initialArguments);

            case DosExecuteMode.LoadOverlay:
                return ExecuteLoadOverlayMode(programName, parameterBlockAddress);

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("Invalid EXEC mode specified: {Mode}", mode);
                }
                _state.AX = (ushort)DosErrorCode.FunctionNumberInvalid;
                return false;
        }
    }

    /// <summary>
    /// Terminates the current process and returns control to the parent.
    /// </summary>
    /// <remarks>
    /// Implements the functionality for DOS INT 21h functions 00h, 31h, and 4Ch,
    /// as well as INT 20h.
    /// </remarks>
    /// <param name="pspSegment">PSP segment of the process to terminate</param>
    /// <param name="isTerminateAndStayResident">Whether this is a TSR termination</param>
    /// <param name="exitCode">Process exit code</param>
    /// <param name="paragraphsToKeep">For TSR, how many paragraphs to keep resident</param>
    public void TerminateProcess(ushort pspSegment, bool isTerminateAndStayResident,
        byte exitCode, ushort paragraphsToKeep = 0) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Terminating process at PSP 0x{PspSegment:X4}, TSR={IsTsr}, ExitCode={ExitCode}",
                pspSegment, isTerminateAndStayResident, exitCode);
        }

        DosProgramSegmentPrefix? currentPsp = _pspTracker.GetCurrentPsp();
        if (currentPsp == null || _pspTracker.GetCurrentPspSegment() != pspSegment) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Cannot terminate process: PSP segment mismatch");
            }
            return;
        }

        ushort parentPspSegment = currentPsp.ParentProgramSegmentPrefix;
        if (pspSegment == parentPspSegment) {
            // Root process termination
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Root process terminated, stopping execution");
            }
            return;
        }

        // Close all files owned by this process (except for TSR)
        if (!isTerminateAndStayResident) {
            CloseProcessFiles(currentPsp);
        }

        // Get termination address (INT 22h vector)
        uint terminationAddress = currentPsp.TerminateAddress;

        // Restore interrupt vectors 22h, 23h, 24h
        RestoreInterruptVectors(currentPsp);

        // Set return to parent PSP
        _pspTracker.PopCurrentPspSegment();
        DosProgramSegmentPrefix parentPsp = new DosProgramSegmentPrefix(_memory,
            MemoryUtils.ToPhysicalAddress(parentPspSegment, 0));

        // Restore parent's stack (SS:SP)
        _state.SS = MemoryUtils.ToSegment(parentPsp.StackPointer);
        _state.SP = (ushort)parentPsp.StackPointer;

        // For TSR, resize memory block to keep only specified paragraphs
        if (isTerminateAndStayResident) {
            DosErrorCode result = _memoryManager.TryModifyBlock(pspSegment, paragraphsToKeep, out _);
            if (result != DosErrorCode.NoError && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Failed to resize TSR memory block: {Error}", result);
            }
        } else {
            // Free all memory owned by the process
            FreeProcessMemory(pspSegment);
        }

        // Restore registers from stack
        RestoreRegistersFromStack();

        // Push termination address onto stack for RETF
        ushort terminationOffset = (ushort)(terminationAddress & 0xFFFF);
        ushort terminationSegment = (ushort)(terminationAddress >> 16);

        _state.SP -= 4; // Make room for CS:IP
        uint stackAddress = MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP);
        _memory.UInt16[stackAddress] = terminationOffset;
        _memory.UInt16[stackAddress + 2] = terminationSegment;

        // Set flags register (IOPL=3, interrupts enabled, test flags cleared)
        _state.SP -= 2;
        _memory.UInt16[MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP)] = 0x7202;

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Process termination complete, returned to parent PSP 0x{ParentPsp:X4}",
                parentPspSegment);
        }
    }

    /// <summary>
    /// Implements the Load and Execute (AL=00h) and Load (AL=01h) modes of the EXEC function.
    /// </summary>
    private bool ExecuteLoadAndExecuteMode(string programName,
        uint parameterBlockAddress, DosExecuteMode mode, string? initialArguments) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Starting {Mode} for program: {ProgramName}",
                mode == DosExecuteMode.LoadAndExecute ? "Load and Execute" : "Load Only",
                programName);
        }

        // Step 1: Parse parameter block or create default parameters for initial load
        (ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address) =
            PrepareExecutionParameters(programName, parameterBlockAddress, initialArguments);

        // Step 2: Load the program file from disk
        if (!TryLoadProgramFile(programName, out byte[]? fileBytes)) {
            return false;
        }

        // Step 3: Determine program type (.COM vs .EXE) and parse headers
        DosExeFile? exeFile = AnalyzeProgramFormat(fileBytes);
        bool isComFile = exeFile == null;

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Program type detected: {ProgramType}, Size: {Size} bytes",
                isComFile ? ".COM" : ".EXE", fileBytes.Length);
        }

        // Step 4: Allocate memory for the program
        DosMemoryControlBlock? programMcb = AllocateProgramMemory(exeFile, fileBytes.Length,
            parameterBlockAddress == 0);

        if (programMcb == null) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Failed to allocate memory for program {ProgramName}", programName);
            }
            _state.AX = (ushort)DosErrorCode.InsufficientMemory;
            return false;
        }

        ushort pspSegment = programMcb.DataBlockSegment;
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Allocated memory block at segment 0x{PspSegment:X4}, size {Size} paragraphs",
                pspSegment, programMcb.Size);
        }

        // Step 5: Save current state for parent process if this is not initial load
        if (parameterBlockAddress != 0) {
            SaveParentProcessState();
        }

        // Step 6: Register the new PSP in the process chain
        _pspTracker.PushPspSegment(pspSegment);

        // Step 7: Create and initialize the Program Segment Prefix
        InitializeProgramSegmentPrefix(pspSegment, programMcb.Size, environmentSegment,
            commandTailAddress, fcb1Address, fcb2Address);

        // Step 8: Load program into memory and prepare execution environment
        ProgramLoadInfo loadInfo;

        if (isComFile) {
            loadInfo = LoadComProgram(fileBytes, pspSegment);
        } else {
            loadInfo = LoadExeProgram(exeFile!, pspSegment, programMcb);
        }

        // Step 9: Handle different execution modes
        if (mode == DosExecuteMode.LoadButDoNotRun) {
            HandleLoadOnlyMode(parameterBlockAddress, loadInfo);
            return true;
        }

        // Step 10: For LOADNGO mode, setup the CPU state for execution
        // This is similar to DOSBox's approach in the LOADNGO code path

        // Get Caller's return address from stack
        uint callerStackAddress = MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP);
        ushort callerIP = _memory.UInt16[callerStackAddress];
        ushort callerCS = _memory.UInt16[callerStackAddress + 2];

        // Set INT 22h vector to caller's return address
        _interruptVectorTable[0x22] = new SegmentedAddress(callerCS, callerIP);

        // Save interrupt vectors in new PSP
        DosProgramSegmentPrefix newPsp = loadInfo.Psp;
        newPsp.TerminateAddress = _interruptVectorTable[0x22].Linear;
        newPsp.BreakAddress = _interruptVectorTable[0x23].Linear;
        newPsp.CriticalErrorAddress = _interruptVectorTable[0x24].Linear;

        // Set the stack for the new program
        _state.SS = loadInfo.StackSegment;
        _state.SP = loadInfo.StackPointer;

        // Set the CPU registers for program entry
        _state.AX = 0;
        _state.BX = 0;
        _state.CX = 0xFF;
        _state.DX = pspSegment;
        _state.CS = loadInfo.CodeSegment;
        _state.IP = loadInfo.InstructionPointer;
        _state.SI = loadInfo.InstructionPointer;
        _state.DI = loadInfo.StackPointer;
        _state.BP = 0x91C; // DOS internal stack begin marker
        _state.DS = pspSegment;
        _state.ES = pspSegment;

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Executing program {ProgramName} at CS:IP={CS:X4}:{IP:X4}, SS:SP={SS:X4}:{SP:X4}",
                programName, loadInfo.CodeSegment, loadInfo.InstructionPointer, loadInfo.StackSegment, loadInfo.StackPointer);
        }

        return true;
    }

    /// <summary>
    /// Implements the Load Overlay (AL=03h) mode of the EXEC function.
    /// </summary>
    private bool ExecuteLoadOverlayMode(string programName, uint parameterBlockAddress) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Starting Load Overlay mode for program: {ProgramName}", programName);
        }

        // Parse overlay parameter block
        DosOverlayParameterBlock overlayBlock = new DosOverlayParameterBlock(_memory, parameterBlockAddress);
        ushort loadSegment = overlayBlock.OverlayLoadSegment;
        ushort relocationFactor = overlayBlock.OverlayRelocationFactor;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Overlay parameters: LoadSegment=0x{LoadSegment:X4}, RelocationFactor=0x{RelocationFactor:X4}",
                loadSegment, relocationFactor);
        }

        // Load program file
        if (!TryLoadProgramFile(programName, out byte[]? fileBytes)) {
            return false;
        }

        // Overlays must be .EXE format for relocation support
        DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
        if (!exeFile.IsValid) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Overlay file {ProgramName} is not a valid .EXE file", programName);
            }
            _state.AX = (ushort)DosErrorCode.FormatInvalid;
            return false;
        }

        // Load overlay at specified address
        uint physicalAddress = MemoryUtils.ToPhysicalAddress(loadSegment, 0);
        _memory.LoadData(physicalAddress, exeFile.ProgramImage);

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Loaded overlay at physical address 0x{PhysicalAddress:X8}, applying {RelocationCount} relocations",
                physicalAddress, exeFile.RelocationTable.Count());
        }

        // Apply relocations
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset) + physicalAddress;
            ushort originalValue = _memory.UInt16[addressToEdit];
            ushort newValue = (ushort)(originalValue + relocationFactor);
            _memory.UInt16[addressToEdit] = newValue;

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Verbose("Applied relocation at 0x{Address:X8}: 0x{Original:X4} → 0x{New:X4}",
                    addressToEdit, originalValue, newValue);
            }
        }

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Successfully loaded overlay {ProgramName} at segment 0x{LoadSegment:X4}",
                programName, loadSegment);
        }
        return true;
    }

    /// <summary>
    /// Saves the current CPU registers on stack for restoration when child process terminates.
    /// This matches DOSBox's SaveRegisters function.
    /// </summary>
    private void SaveParentProcessState() {
        _state.SP -= 18; // Reserve space for 9 words (18 bytes)

        uint stackBase = MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP);
        _memory.UInt16[stackBase + 0] = _state.AX;
        _memory.UInt16[stackBase + 2] = _state.CX;
        _memory.UInt16[stackBase + 4] = _state.DX;
        _memory.UInt16[stackBase + 6] = _state.BX;
        _memory.UInt16[stackBase + 8] = _state.SI;
        _memory.UInt16[stackBase + 10] = _state.DI;
        _memory.UInt16[stackBase + 12] = _state.BP;
        _memory.UInt16[stackBase + 14] = _state.DS;
        _memory.UInt16[stackBase + 16] = _state.ES;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Saved parent process registers on stack at SS:SP {StackAddress}",
                ConvertUtils.ToSegmentedAddressRepresentation(_state.SS, _state.SP));
        }
    }

    /// <summary>
    /// Restores registers from stack when returning to parent process.
    /// This matches DOSBox's RestoreRegisters function.
    /// </summary>
    private void RestoreRegistersFromStack() {
        uint stackBase = MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP);

        _state.AX = _memory.UInt16[stackBase + 0];
        _state.CX = _memory.UInt16[stackBase + 2];
        _state.DX = _memory.UInt16[stackBase + 4];
        _state.BX = _memory.UInt16[stackBase + 6];
        _state.SI = _memory.UInt16[stackBase + 8];
        _state.DI = _memory.UInt16[stackBase + 10];
        _state.BP = _memory.UInt16[stackBase + 12];
        _state.DS = _memory.UInt16[stackBase + 14];
        _state.ES = _memory.UInt16[stackBase + 16];

        _state.SP += 18; // Restore stack pointer

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Restored parent process registers from stack");
        }
    }

    /// <summary>
    /// Closes all files owned by the specified process.
    /// </summary>
    private void CloseProcessFiles(DosProgramSegmentPrefix psp) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Closing files for PSP at segment 0x{PspSegment:X4}",
                MemoryUtils.ToSegment(psp.BaseAddress));
        }

        // Close all file handles in the PSP file table
        uint fileTableAddress = psp.FileTableAddress;
        ushort maxFiles = psp.MaximumOpenFiles;

        for (ushort i = 0; i < maxFiles; i++) {
            byte fileHandle = _memory.UInt8[fileTableAddress + i];
            if (fileHandle != 0xFF) {
                DosFileOperationResult result = _fileManager.CloseFileOrDevice(fileHandle);
                if (result.IsError && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Failed to close file handle {FileHandle} during process termination", fileHandle);
                }
            }
        }
    }

    /// <summary>
    /// Restores interrupt vectors 22h, 23h, and 24h from the PSP.
    /// </summary>
    private void RestoreInterruptVectors(DosProgramSegmentPrefix psp) {
        _interruptVectorTable[0x22] = new SegmentedAddress(
            MemoryUtils.ToSegment(psp.TerminateAddress), 0);

        _interruptVectorTable[0x23] = new SegmentedAddress(
            MemoryUtils.ToSegment(psp.BreakAddress), 0);

        _interruptVectorTable[0x24] = new(
            MemoryUtils.ToSegment(psp.CriticalErrorAddress), 0);

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Restored interrupt vectors from PSP");
        }
    }

    /// <summary>
    /// Frees all memory blocks owned by the specified process.
    /// </summary>
    private void FreeProcessMemory(ushort pspSegment) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Freeing memory for process at PSP segment 0x{PspSegment:X4}", pspSegment);
        }

        // Free the main program memory block (MCB is at pspSegment - 1)
        ushort mcbSegment = (ushort)(pspSegment - 1);
        if (!_memoryManager.FreeMemoryBlock(mcbSegment)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Failed to free main memory block for PSP 0x{PspSegment:X4}", pspSegment);
            }
        }

        // Free environment block if it exists
        DosProgramSegmentPrefix psp = new DosProgramSegmentPrefix(_memory,
            MemoryUtils.ToPhysicalAddress(pspSegment, 0));

        if (psp.EnvironmentTableSegment != 0) {
            ushort envMcbSegment = (ushort)(psp.EnvironmentTableSegment - 1);
            if (!_memoryManager.FreeMemoryBlock(envMcbSegment)) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Failed to free environment block for PSP 0x{PspSegment:X4}", pspSegment);
                }
            }
        }
    }

    /// <summary>
    /// Prepares execution parameters from the parameter block or creates defaults for initial load.
    /// </summary>
    private (ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address)
        PrepareExecutionParameters(string programName, uint parameterBlockAddress, string? initialArguments) {

        if (parameterBlockAddress == 0) {
            // Initial program load - create default parameters
            TryPrepareInitialLoadParameters(programName, initialArguments, out
                (ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address)? values);
            return values ?? new(0, 0, 0, 0);
        } else {
            // Child program load - parse parameter block
            return ParseParameterBlock(parameterBlockAddress);
        }
    }

    /// <summary>
    /// Creates default parameters for initial program load (when DOS is bootstrapping).
    /// </summary>
    private bool
        TryPrepareInitialLoadParameters(string programName, string? initialArguments,
        [NotNullWhen(true)] out (ushort environmentSegment, uint commandTailAddress,
        uint fcb1Address, uint fcb2Address)? values) {

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Preparing initial load parameters for program: {ProgramName}", programName);
        }

        // Create environment block
        byte[] environmentBlockBytes = CreateEnvironmentBlock(programName);
        ushort envSizeInParagraphs = (ushort)((environmentBlockBytes.Length + 15) / 16);
        DosMemoryControlBlock? envMcb = _memoryManager.AllocateMemoryBlock(envSizeInParagraphs);
        if (envMcb == null) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Could not allocate memory for environment block ({Size} paragraphs)",
                    envSizeInParagraphs);
            }
            _state.AX = (ushort)DosErrorCode.InsufficientMemory;
            values = null;
            return false;
        }

        ushort environmentSegment = envMcb.DataBlockSegment;
        _memory.LoadData(MemoryUtils.ToPhysicalAddress(environmentSegment, 0), environmentBlockBytes);

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Created environment block at segment 0x{EnvironmentSegment:X4}, size {Size} bytes",
                environmentSegment, environmentBlockBytes.Length);
        }

        // Create command tail
        uint commandTailAddress = 0;
        if (!string.IsNullOrEmpty(initialArguments)) {
            byte[] argumentsBytes = ConvertArgumentsToDosFormat(initialArguments);
            ushort argsSizeInParagraphs = (ushort)((argumentsBytes.Length + 15) / 16);
            DosMemoryControlBlock? argsMcb = _memoryManager.AllocateMemoryBlock(argsSizeInParagraphs);
            if (argsMcb == null) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("Could not allocate memory for command line arguments ({Size} paragraphs)",
                        argsSizeInParagraphs);
                }
                _state.AX = (ushort)DosErrorCode.InsufficientMemory;
                values = null;
                return false;
            }
            commandTailAddress = MemoryUtils.ToPhysicalAddress(argsMcb.DataBlockSegment, 0);
            _memory.LoadData(commandTailAddress, argumentsBytes);

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Created command tail at address 0x{CommandTailAddress:X8}: '{Arguments}'",
                    commandTailAddress, initialArguments);
            }
        }

        values = (environmentSegment, commandTailAddress, 0, 0); // FCBs not used in initial load
        return true;
    }

    /// <summary>
    /// Parses the parameter block for child program execution.
    /// </summary>
    private (ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address)
        ParseParameterBlock(uint parameterBlockAddress) {

        DosLoadOrLoadAndExecuteParameterBlock parameterBlock = new
            DosLoadOrLoadAndExecuteParameterBlock(_memory, parameterBlockAddress);

        ushort environmentSegment = parameterBlock.EnvironmentSegment;
        uint commandTailAddress = parameterBlock.CommandTailAddress;
        uint fcb1Address = parameterBlock.FirstFcbAddress;
        uint fcb2Address = parameterBlock.SecondFcbAddress;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Parsed parameter block: Env=0x{Env:X4}, CmdTail=0x{CmdTail:X8}, FCB1=0x{FCB1:X8}, FCB2=0x{FCB2:X8}",
                environmentSegment, commandTailAddress, fcb1Address, fcb2Address);
        }

        // Handle environment inheritance
        if (environmentSegment == 0) {
            DosProgramSegmentPrefix? parentPsp = _pspTracker.GetCurrentPsp();
            if (parentPsp != null) {
                environmentSegment = parentPsp.EnvironmentTableSegment;
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("Inheriting parent environment from segment 0x{ParentEnvSegment:X4}",
                        environmentSegment);
                }
            } else {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("No parent PSP found for environment inheritance, creating default environment");
                }
                // Create default environment as fallback
                if (TryPrepareInitialLoadParameters("", null, out
                    (ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address)? values)) {
                    environmentSegment = values.Value.environmentSegment;
                }
            }
        }

        return (environmentSegment, commandTailAddress, fcb1Address, fcb2Address);
    }

    protected override byte[] ReadFile(string file) {
        string? hostFilePath = _driveManager.GetCurrentDosPathResolver().GetFullHostPathFromDosOrDefault(file);
        if (!string.IsNullOrWhiteSpace(hostFilePath) && File.Exists(hostFilePath)) {
            return File.ReadAllBytes(hostFilePath);
        }
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file)) {
            return File.ReadAllBytes(file);
        }
        throw new FileNotFoundException(file);
    }

    /// <summary>
    /// Loads the program file from disk and validates it exists.
    /// </summary>
    private bool TryLoadProgramFile(string programName, [NotNullWhen(true)] out byte[]? fileBytes) {
        try {
            fileBytes = ReadFile(programName);
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Successfully loaded program file {ProgramName}, size: {Size} bytes",
                    programName, fileBytes.Length);
            }
            return true;
        } catch (IOException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(ex, "Failed to load program file: {ProgramName}", programName);
            }
            _state.AX = (ushort)DosErrorCode.FileNotFound;
            fileBytes = null;
            return false;
        }
    }

    /// <summary>
    /// Analyzes the program format and returns EXE file info if it's an EXE, null if it's a COM.
    /// </summary>
    private DosExeFile? AnalyzeProgramFormat(byte[] fileBytes) {
        if (fileBytes.Length >= DosExeFile.MinExeSize) {
            DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));

            // Basic EXE signature check
            if (exeFile.Signature is "MZ" or "ZM" && exeFile.IsValid) {
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("Detected valid .EXE file: HeaderSize={HeaderSize}, MinAlloc={MinAlloc}, MaxAlloc={MaxAlloc}, CS:IP={InitCS:X4}:{InitIP:X4}",
                        exeFile.HeaderSizeInParagraphs, exeFile.MinAlloc, exeFile.MaxAlloc, exeFile.InitCS, exeFile.InitIP);
                }
                return exeFile;
            }
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Detected .COM file format (memory image)");
        }
        return null;
    }

    /// <summary>
    /// Allocates memory for the program, avoiding double allocation for initial loads.
    /// </summary>
    private DosMemoryControlBlock? AllocateProgramMemory(DosExeFile? exeFile,
        int fileSizeBytes, bool isInitialLoad) {
        if (exeFile != null) {
            // .EXE file memory allocation
            return _memoryManager.ReserveSpaceForExe(exeFile);
        } else {
            // .COM file - calculate required size including PSP and program
            ushort sizeInParagraphs = (ushort)((fileSizeBytes + ComOffset + 15) / 16);

            // Ensure minimum size and add space for stack
            sizeInParagraphs = Math.Max(sizeInParagraphs, (ushort)0x1000);

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Allocating {Size} paragraphs for .COM file (includes PSP + program + stack space)", sizeInParagraphs);
            }

            return _memoryManager.AllocateMemoryBlock(sizeInParagraphs);
        }
    }

    /// <summary>
    /// Initializes the Program Segment Prefix with all required fields following DOS specification.
    /// </summary>
    private void InitializeProgramSegmentPrefix(ushort pspSegment, ushort memorySize,
        ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address) {
        DosProgramSegmentPrefix psp = new DosProgramSegmentPrefix(_memory, MemoryUtils.ToPhysicalAddress(pspSegment, 0));

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Initializing PSP at segment 0x{PspSegment:X4} with memory size {MemorySize} paragraphs",
                pspSegment, memorySize);
        }

        // Set INT 20h instruction at start of PSP (CP/M compatibility)
        psp.Exit[0] = 0xCD; // INT opcode
        psp.Exit[1] = 0x20; // INT 20h vector

        // Set segment of first byte beyond program (for memory allocation tracking)
        psp.NextSegment = (ushort)(pspSegment + memorySize);

        // Reserved byte
        psp.Reserved = 0;

        // Far call to DOS INT 21h dispatcher (obsolete but some programs check it)
        psp.FarCall = 0xEA; // FAR JMP opcode
        psp.CpmServiceRequestAddress = 0xF01D0000; // Points to DOS service dispatcher

        // Setup parent relationship
        DosProgramSegmentPrefix? parentPsp = _pspTracker.GetCurrentPsp();
        if (parentPsp != null) {
            psp.ParentProgramSegmentPrefix = MemoryUtils.ToSegment(parentPsp.BaseAddress);

            // Copy interrupt vectors from parent or set defaults
            psp.TerminateAddress = parentPsp.TerminateAddress;
            psp.BreakAddress = parentPsp.BreakAddress;
            psp.CriticalErrorAddress = parentPsp.CriticalErrorAddress;

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Set parent PSP to segment 0x{ParentPspSegment:X4}, inherited interrupt vectors",
                    psp.ParentProgramSegmentPrefix);
            }
        } else {
            psp.ParentProgramSegmentPrefix = pspSegment; // Root process points to itself

            // Set default interrupt vectors for root process
            psp.TerminateAddress = _interruptVectorTable[0x22].Linear;
            psp.BreakAddress = _interruptVectorTable[0x23].Linear;
            psp.CriticalErrorAddress = _interruptVectorTable[0x24].Linear;
        }

        // Set environment table segment
        psp.EnvironmentTableSegment = environmentSegment;

        // Initialize file handle table
        InitializeFileHandleTable(psp);

        // Set stack pointer (will be updated when program starts)
        psp.StackPointer = MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP);

        // Setup command tail if provided
        if (commandTailAddress != 0) {
            SetupCommandTail(psp, commandTailAddress);
        } else {
            // Set empty command tail
            psp.DosCommandTail.Length = 0;
            psp.DosCommandTail.Command = "";
        }

        // Setup FCBs if provided
        SetupFileControlBlocks(psp, fcb1Address, fcb2Address);

        // Set DOS version in PSP
        psp.DosVersionMajor = 5; // DOS 5.0 compatibility
        psp.DosVersionMinor = 0;

        // Initialize service routine entry points (INT 21h RETF sequence)
        psp.Service[0] = 0xCD; // INT opcode
        psp.Service[1] = 0x21; // INT 21h
        psp.Service[2] = 0xCB; // RETF opcode

        // Set the disk transfer area address to the command tail area in PSP
        _fileManager.SetDiskTransferAreaAddress(pspSegment, DosCommandTail.OffsetInPspSegment);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("PSP initialization complete for segment 0x{PspSegment:X4}, parent=0x{ParentSegment:X4}, env=0x{EnvSegment:X4}",
                pspSegment, psp.ParentProgramSegmentPrefix, psp.EnvironmentTableSegment);
        }
    }

    /// <summary>
    /// Initializes the file handle table in the PSP with standard DOS file handles.
    /// Sets up STDIN, STDOUT, STDERR, STDAUX, and STDPRN, and initializes remaining handles to 0xFF (closed).
    /// </summary>
    private void InitializeFileHandleTable(DosProgramSegmentPrefix psp) {
        // Get parent PSP if available
        DosProgramSegmentPrefix? parentPsp = _pspTracker.GetCurrentPsp();

        if (parentPsp != null && psp.ParentProgramSegmentPrefix != MemoryUtils.ToSegment(psp.BaseAddress)) {
            // Copy handles from parent PSP (proper DOS inheritance behavior)
            for (int i = 0; i < 20; i++) {
                psp.Files[i] = parentPsp.Files[i];
            }

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Inherited file handles from parent PSP at segment 0x{ParentPspSegment:X4}",
                    MemoryUtils.ToSegment(parentPsp.BaseAddress));
            }
        } else {
            // For root process, initialize standard handles
            psp.Files[0] = 0; // STDIN
            psp.Files[1] = 1; // STDOUT
            psp.Files[2] = 2; // STDERR
            psp.Files[3] = 3; // STDAUX
            psp.Files[4] = 4; // STDPRN
            for (int i = 5; i < 20; i++) {
                psp.Files[i] = 0xFF; // Closed handle marker
            }

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Initialized standard file handles for root PSP");
            }
        }

        // Always use standard file table in PSP for maximum compatibility
        psp.FileTableAddress = psp.BaseAddress + 0x18; // Standard file table offset in PSP
        psp.MaximumOpenFiles = 20;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Initialized file handle table: STDIN={Stdin}, STDOUT={Stdout}, STDERR={Stderr}, STDAUX={Stdaux}, STDPRN={Stdprn}, MaxFiles={MaxFiles}",
                psp.Files[0], psp.Files[1], psp.Files[2], psp.Files[3], psp.Files[4], psp.MaximumOpenFiles);
        }
    }

    /// <summary>
    /// Sets up the command tail in the PSP from the provided command tail address.
    /// </summary>
    private void SetupCommandTail(DosProgramSegmentPrefix psp, uint commandTailAddress) {
        byte length = _memory.UInt8[commandTailAddress];
        if (length > 0 && length <= DosCommandTail.MaxCharacterLength) {
            byte[] commandBytes = _memory.GetData(commandTailAddress + 1, length);
            string command = Encoding.ASCII.GetString(commandBytes);

            // Remove trailing carriage return if present
            if (command.EndsWith('\r')) {
                command = command[..^1];
                length--;
            }

            psp.DosCommandTail.Length = (byte)(length + 1);
            psp.DosCommandTail.Command = command;

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Set command tail: '{Command}' (length {Length})", command, length);
            }
        } else {
            // Set empty command tail
            psp.DosCommandTail.Length = 0;
            psp.DosCommandTail.Command = "";
        }
    }

    /// <summary>
    /// Sets up the File Control Blocks (FCBs) in the PSP.
    /// </summary>
    private void SetupFileControlBlocks(DosProgramSegmentPrefix psp, uint fcb1Address, uint fcb2Address) {
        // Setup first FCB
        if (fcb1Address != 0) {
            for (int i = 0; i < 16; i++) {
                psp.FirstFileControlBlock[i] = _memory.UInt8[fcb1Address + (uint)i];
            }
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Copied first FCB from address 0x{FCB1Address:X8}", fcb1Address);
            }
        } else {
            // Initialize with empty FCB
            for (int i = 0; i < 16; i++) {
                psp.FirstFileControlBlock[i] = 0;
            }
        }

        // Setup second FCB
        if (fcb2Address != 0) {
            for (int i = 0; i < 20; i++) {
                psp.SecondFileControlBlock[i] = _memory.UInt8[fcb2Address + (uint)i];
            }
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Copied second FCB from address 0x{FCB2Address:X8}", fcb2Address);
            }
        } else {
            // Initialize with empty FCB
            for (int i = 0; i < 20; i++) {
                psp.SecondFileControlBlock[i] = 0;
            }
        }
    }

    /// <summary>
    /// Loads a .COM program into memory and prepares for execution.
    /// </summary>
    private ProgramLoadInfo LoadComProgram(byte[] programBytes, ushort pspSegment) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Loading .COM program at PSP segment 0x{PspSegment:X4}, program size: {Size} bytes",
                pspSegment, programBytes.Length);
        }

        // Load program at offset 100h after PSP
        uint loadAddress = MemoryUtils.ToPhysicalAddress(pspSegment, ComOffset);
        _memory.LoadData(loadAddress, programBytes);

        // Zero memory beyond the loaded program up to the end of the allocated block
        ZeroMemoryBeyondProgram(loadAddress, (uint)programBytes.Length, pspSegment);

        DosProgramSegmentPrefix psp = new DosProgramSegmentPrefix(_memory,
            MemoryUtils.ToPhysicalAddress(pspSegment, 0));

        // For COM files, all segment registers point to the PSP
        // and IP is set to 0x100 (ComOffset)
        return new ProgramLoadInfo {
            Psp = psp,
            LoadSegment = pspSegment,
            CodeSegment = pspSegment,
            InstructionPointer = ComOffset,
            StackSegment = pspSegment,
            StackPointer = 0xFFFE,
            IsComProgram = true
        };
    }

    /// <summary>
    /// Loads an .EXE program into memory and prepares for execution.
    /// </summary>
    private ProgramLoadInfo LoadExeProgram(DosExeFile exeFile, ushort pspSegment,
        DosMemoryControlBlock programBlock) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Loading .EXE program at PSP segment 0x{PspSegment:X4}", pspSegment);
        }

        // Calculate load segment (PSP + 16 bytes = program start)
        ushort programLoadSegment = (ushort)(pspSegment + 0x10);

        // Handle special case for EXE files with no memory allocation requirements
        // For MinAlloc=0, MaxAlloc=0, load program at high end of memory block
        if (exeFile.MinAlloc == 0 && exeFile.MaxAlloc == 0) {
            ushort programOffset = (ushort)(programBlock.Size - exeFile.ProgramSizeInParagraphsPerHeader);
            programLoadSegment = (ushort)(pspSegment + programOffset);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Program load segment: 0x{ProgramLoadSegment:X4}, applying {RelocationCount} relocations",
                programLoadSegment, exeFile.RelocationTable.Count());
        }

        // Load program image
        uint physicalLoadAddress = MemoryUtils.ToPhysicalAddress(programLoadSegment, 0);
        _memory.LoadData(physicalLoadAddress, exeFile.ProgramImage, (int)exeFile.ProgramSize);

        // Zero memory beyond the loaded program up to the end of the allocated block
        ZeroMemoryBeyondProgram(physicalLoadAddress, exeFile.ProgramSize, pspSegment,
            (uint)programBlock.Size * 16);

        // Apply relocations
        foreach (SegmentedAddress relocationAddress in exeFile.RelocationTable) {
            uint addressToRelocate = MemoryUtils.ToPhysicalAddress(relocationAddress.Segment,
                relocationAddress.Offset) + physicalLoadAddress;
            ushort originalValue = _memory.UInt16[addressToRelocate];
            ushort relocatedValue = (ushort)(originalValue + programLoadSegment);
            _memory.UInt16[addressToRelocate] = relocatedValue;

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Relocation at {Address}: 0x{Original:X4} → 0x{Relocated:X4}",
                    ConvertUtils.ToSegmentedAddressRepresentation(relocationAddress.Segment,
                    relocationAddress.Offset),
                    originalValue, relocatedValue);
            }
        }

        DosProgramSegmentPrefix psp = new DosProgramSegmentPrefix(_memory,
            MemoryUtils.ToPhysicalAddress(pspSegment, 0));

        // Return the program load info with EXE-specific settings
        return new ProgramLoadInfo {
            Psp = psp,
            LoadSegment = programLoadSegment,
            CodeSegment = (ushort)(exeFile.InitCS + programLoadSegment),
            InstructionPointer = exeFile.InitIP,
            StackSegment = (ushort)(exeFile.InitSS + programLoadSegment),
            StackPointer = exeFile.InitSP,
            IsComProgram = false
        };
    }

    /// <summary>
    /// Zeros memory beyond the loaded program up to the end of the allocated memory block.
    /// </summary>
    private void ZeroMemoryBeyondProgram(uint programStartAddress, uint programSize,
        ushort pspSegment, uint? allocatedBlockSizeBytes = null) {
        uint programEndAddress = programStartAddress + programSize;
        uint zeroEndAddress;

        if (allocatedBlockSizeBytes.HasValue) {
            // .EXE file: Zero to end of allocated block
            uint blockStartAddress = MemoryUtils.ToPhysicalAddress(pspSegment, 0);
            zeroEndAddress = Math.Min(blockStartAddress + allocatedBlockSizeBytes.Value,
                MemoryUtils.ToPhysicalAddress(0x9FFF, 0xFFFF));
        } else {
            // .COM file: Zero to end of PSP segment (64KB limit) or conventional memory limit
            uint pspStartAddress = MemoryUtils.ToPhysicalAddress(pspSegment, 0);
            uint segmentEndAddress = pspStartAddress + 0x10000; // 64KB segment
            zeroEndAddress = Math.Min(segmentEndAddress,
                MemoryUtils.ToPhysicalAddress(0x9FFF, 0xFFFF));
        }

        if (programEndAddress < zeroEndAddress) {
            uint bytesToZero = zeroEndAddress - programEndAddress;

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Zeroing {BytesToZero} bytes from 0x{StartAddress:X8} to 0x{EndAddress:X8}",
                    bytesToZero, programEndAddress, zeroEndAddress);
            }

            _memory.LoadData(programEndAddress, new byte[bytesToZero]);
        }
    }

    /// <summary>
    /// Handles the load-only mode by saving entry point information to the parameter block.
    /// </summary>
    private void HandleLoadOnlyMode(uint parameterBlockAddress, ProgramLoadInfo loadInfo) {
        if (parameterBlockAddress == 0) {
            return;
        }

        DosLoadProgramParameterBlock loadOnlyBlock = new DosLoadProgramParameterBlock(_memory,
            parameterBlockAddress);

        loadOnlyBlock.EntryPointAddress = new SegmentedAddress(
            loadInfo.CodeSegment, loadInfo.InstructionPointer);

        loadOnlyBlock.StackAddress = new SegmentedAddress(
            loadInfo.StackSegment, loadInfo.StackPointer);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Load-only mode: Entry point saved as {EntryPoint}, Stack saved as {StackAddress}",
                loadOnlyBlock.EntryPointAddress, loadOnlyBlock.StackAddress);
        }
    }

    /// <summary>
    /// Converts command line arguments to DOS format (length byte + ASCII + carriage return).
    /// </summary>
    private static byte[] ConvertArgumentsToDosFormat(string? arguments) {
        if (string.IsNullOrWhiteSpace(arguments)) {
            return new byte[] { 0, 0x0D }; // Empty command line
        }

        // Truncate if too long (DOS command line limit is 127 characters)
        string truncatedArgs = arguments.Length > 127 ? arguments[..127] : arguments;

        byte[] result = new byte[truncatedArgs.Length + 2];
        result[0] = (byte)truncatedArgs.Length; // Length byte

        byte[] argumentsBytes = Encoding.ASCII.GetBytes(truncatedArgs);
        argumentsBytes.CopyTo(result, 1);

        result[^1] = 0x0D; // Carriage return terminator

        return result;
    }

    /// <summary>
    /// Creates a DOS environment block from the current environment variables.
    /// </summary>
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

        // Write a word with value 1 after the environment variables (DOS 3.0+ requirement)
        ms.WriteByte(1);
        ms.WriteByte(0);

        // Get the DOS path for the program (not the host path)
        string dosPath = _fileManager.GetDosProgramPath(programPath);

        // Write the DOS path to the environment block
        byte[] programPathBytes = Encoding.ASCII.GetBytes(dosPath);
        ms.Write(programPathBytes, 0, programPathBytes.Length);
        ms.WriteByte(0); // Null terminator for program path

        return ms.ToArray();
    }
}

/// <summary>
/// Contains all information needed for program loading and execution
/// </summary>
public record ProgramLoadInfo {
    /// <summary>
    /// The Program Segment Prefix for the program
    /// </summary>
    public required DosProgramSegmentPrefix Psp { get; init; }

    /// <summary>
    /// The segment where the program code starts
    /// </summary>
    public required ushort LoadSegment { get; init; }

    /// <summary>
    /// The segment to use for CS register
    /// </summary>
    public required ushort CodeSegment { get; init; }

    /// <summary>
    /// The offset to use for IP register
    /// </summary>
    public required ushort InstructionPointer { get; init; }

    /// <summary>
    /// The segment to use for SS register
    /// </summary>
    public required ushort StackSegment { get; init; }

    /// <summary>
    /// The offset to use for SP register
    /// </summary>
    public required ushort StackPointer { get; init; }

    /// <summary>
    /// Whether this is a COM program (vs EXE)
    /// </summary>
    public required bool IsComProgram { get; init; }
}