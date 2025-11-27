namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Text;

/// <summary>
/// Setups the loading and execution of DOS programs and maintains the DOS PSP chains in memory.
/// Implements DOS INT 21h AH=4Bh (EXEC - Load and/or Execute Program) functionality.
/// </summary>
/// <remarks>
/// Based on MS-DOS 4.0 EXEC.ASM and RBIL documentation.
/// </remarks>
public class DosProcessManager : DosFileLoader {
    private const ushort ComOffset = 0x100;
    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryManager _memoryManager;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;

    /// <summary>
    /// The simulated COMMAND.COM that serves as the root of the PSP chain.
    /// </summary>
    private readonly CommandCom _commandCom;

    /// <summary>
    /// The master environment block that all DOS PSPs inherit.
    /// </summary>
    private readonly EnvironmentVariables _environmentVariables;

    /// <summary>
    /// Gets the simulated COMMAND.COM instance.
    /// </summary>
    public CommandCom CommandCom => _commandCom;

    public DosProcessManager(IMemory memory, State state,
        DosProgramSegmentPrefixTracker dosPspTracker, DosMemoryManager dosMemoryManager,
        DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        IDictionary<string, string> envVars, ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _pspTracker = dosPspTracker;
        _memoryManager = dosMemoryManager;
        _fileManager = dosFileManager;
        _driveManager = dosDriveManager;
        _environmentVariables = new();

        // Initialize COMMAND.COM as the root of the PSP chain
        _commandCom = new CommandCom(memory, loggerService);

        // Use TryAdd to avoid ArgumentException if PATH already exists in envVars
        string pathValue = $"{_driveManager.CurrentDrive.DosVolume}{DosPathResolver.DirectorySeparatorChar}";
        if (!envVars.ContainsKey("PATH")) {
            envVars.Add("PATH", pathValue);
        }

        foreach (KeyValuePair<string, string> envVar in envVars) {
            _environmentVariables.Add(envVar.Key, envVar.Value);
        }
    }

    /// <summary>
    /// Executes a program using DOS EXEC semantics (INT 21h, AH=4Bh).
    /// This is the main API for program loading that should be called by CommandCom
    /// and INT 21h handler.
    /// </summary>
    /// <param name="programPath">The DOS path to the program (must include extension).</param>
    /// <param name="arguments">Command line arguments for the program.</param>
    /// <param name="loadType">The type of load operation to perform.</param>
    /// <param name="environmentSegment">Environment segment to use (0 = inherit from parent).</param>
    /// <returns>The result of the EXEC operation.</returns>
    public DosExecResult Exec(string programPath, string? arguments, 
        DosExecLoadType loadType = DosExecLoadType.LoadAndExecute, 
        ushort environmentSegment = 0) {
        
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "EXEC: Loading program '{Program}' with args '{Args}', type={LoadType}",
                programPath, arguments ?? "", loadType);
        }

        // Resolve the program path to a host file path
        string? hostPath = ResolveToHostPath(programPath);
        if (hostPath is null || !File.Exists(hostPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC: Program file not found: {Program}", programPath);
            }
            return DosExecResult.Failed(DosErrorCode.FileNotFound);
        }

        // Read the program file
        byte[] fileBytes;
        try {
            fileBytes = ReadFile(hostPath);
        } catch (IOException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC: Failed to read program file: {Error}", ex.Message);
            }
            return DosExecResult.Failed(DosErrorCode.AccessDenied);
        } catch (UnauthorizedAccessException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC: Access denied reading program file: {Error}", ex.Message);
            }
            return DosExecResult.Failed(DosErrorCode.AccessDenied);
        }

        // Determine parent PSP
        ushort parentPspSegment = _pspTracker.GetCurrentPspSegment();
        if (parentPspSegment == 0) {
            // If no current PSP, use COMMAND.COM as parent
            parentPspSegment = _commandCom.PspSegment;
        }

        // For the first program, we use the original loading approach that gives the program
        // ALL remaining conventional memory (NextSegment = LastFreeSegment). This is how real DOS 
        // works and ensures programs that resize their memory block via INT 21h 4Ah have room to grow.
        // For child processes, we use proper MCB-based allocation.
        bool isFirstProgram = _pspTracker.PspCount == 0;

        // Create environment block using MCB
        byte[] envBlockData = CreateEnvironmentBlock(programPath);
        ushort envSegment = environmentSegment;
        if (envSegment == 0) {
            // Allocate new environment block
            envSegment = _memoryManager.AllocateEnvironmentBlock(envBlockData, parentPspSegment);
            if (envSegment == 0) {
                return DosExecResult.Failed(DosErrorCode.InsufficientMemory);
            }
        }

        // Allocate memory for the program and create PSP
        DosExecResult result = isFirstProgram 
            ? LoadFirstProgram(fileBytes, hostPath, arguments, parentPspSegment, envSegment, loadType)
            : LoadProgram(fileBytes, hostPath, arguments, parentPspSegment, envSegment, loadType);

        if (!result.Success) {
            // Free the environment block if we allocated it
            if (environmentSegment == 0 && envSegment != 0) {
                _memoryManager.FreeMemoryBlock((ushort)(envSegment - 1));
            }
        }

        return result;
    }

    /// <summary>
    /// Loads an overlay using DOS EXEC semantics (INT 21h, AH=4Bh, AL=03h).
    /// This loads program code at a specified segment without creating a PSP.
    /// </summary>
    /// <param name="programPath">The DOS path to the overlay file.</param>
    /// <param name="loadSegment">The segment at which to load the overlay.</param>
    /// <param name="relocationFactor">The relocation factor for EXE overlays.</param>
    /// <returns>The result of the EXEC operation.</returns>
    /// <remarks>
    /// Overlay loading is used by programs that manage their own code overlays.
    /// No PSP is created and no environment is set up.
    /// </remarks>
    public DosExecResult ExecOverlay(string programPath, ushort loadSegment, ushort relocationFactor) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "EXEC OVERLAY: Loading '{Program}' at segment {Segment:X4}, reloc={Reloc:X4}",
                programPath, loadSegment, relocationFactor);
        }

        // Resolve the program path to a host file path
        string? hostPath = ResolveToHostPath(programPath);
        if (hostPath is null || !File.Exists(hostPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC OVERLAY: File not found: {Program}", programPath);
            }
            return DosExecResult.Failed(DosErrorCode.FileNotFound);
        }

        // Read the program file
        byte[] fileBytes;
        try {
            fileBytes = ReadFile(hostPath);
        } catch (IOException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC OVERLAY: IO error: {Error}", ex.Message);
            }
            return DosExecResult.Failed(DosErrorCode.AccessDenied);
        } catch (UnauthorizedAccessException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC OVERLAY: Access denied: {Error}", ex.Message);
            }
            return DosExecResult.Failed(DosErrorCode.AccessDenied);
        }

        // Determine if this is an EXE or COM file
        bool isExe = false;
        DosExeFile? exeFile = null;

        if (fileBytes.Length >= DosExeFile.MinExeSize) {
            exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            isExe = exeFile.IsValid;
        }

        // Load the overlay at the specified segment
        if (isExe && exeFile is not null) {
            LoadExeOverlay(exeFile, loadSegment, relocationFactor);
        } else {
            LoadComOverlay(fileBytes, loadSegment);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug(
                "EXEC OVERLAY: Loaded {Size} bytes at segment {Segment:X4}",
                fileBytes.Length, loadSegment);
        }

        return DosExecResult.Succeeded();
    }

    /// <summary>
    /// Loads a COM file as an overlay at the specified segment.
    /// </summary>
    private void LoadComOverlay(byte[] comData, ushort loadSegment) {
        uint physicalAddress = MemoryUtils.ToPhysicalAddress(loadSegment, 0);
        _memory.LoadData(physicalAddress, comData);
    }

    /// <summary>
    /// Loads an EXE file as an overlay at the specified segment with relocations.
    /// </summary>
    private void LoadExeOverlay(DosExeFile exeFile, ushort loadSegment, ushort relocationFactor) {
        uint physicalAddress = MemoryUtils.ToPhysicalAddress(loadSegment, 0);
        _memory.LoadData(physicalAddress, exeFile.ProgramImage, (int)exeFile.ProgramSize);

        // Apply relocations using the relocation factor
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset)
                + physicalAddress;
            _memory.UInt16[addressToEdit] += relocationFactor;
        }
    }

    /// <summary>
    /// Resolves a DOS path to a host file path.
    /// </summary>
    private string? ResolveToHostPath(string dosPath) {
        // Try to resolve through the file manager
        try {
            return _fileManager.GetHostPath(dosPath);
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        }
    }

    /// <summary>
    /// Size of memory allocation for COM files in paragraphs (~64KB).
    /// COM files are loaded at CS:0100h and have a maximum size of 64KB - 256 bytes (for PSP).
    /// This value (0xFFF paragraphs = 65,520 bytes) provides sufficient space for maximum COM file size.
    /// </summary>
    private const ushort ComFileMemoryParagraphs = 0xFFF;

    /// <summary>
    /// Loads the program into memory and sets up the PSP.
    /// </summary>
    private DosExecResult LoadProgram(byte[] fileBytes, string hostPath, string? arguments,
        ushort parentPspSegment, ushort envSegment, DosExecLoadType loadType) {
        
        // Determine if this is an EXE or COM file
        bool isExe = false;
        DosExeFile? exeFile = null;
        
        if (fileBytes.Length >= DosExeFile.MinExeSize) {
            exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            isExe = exeFile.IsValid;
        }

        // Allocate memory for the program using MCB-based allocation
        // For the first program, we use InitialPspSegment; for child processes, we use MCB allocation
        ushort pspSegment;
        DosMemoryControlBlock? memBlock;
        
        if (_pspTracker.PspCount == 0) {
            // First program - use the configured initial PSP segment
            if (isExe && exeFile is not null) {
                memBlock = _memoryManager.ReserveSpaceForExe(exeFile, _pspTracker.InitialPspSegment);
            } else {
                memBlock = _memoryManager.AllocateMemoryBlock(ComFileMemoryParagraphs);
            }
            pspSegment = _pspTracker.InitialPspSegment;
        } else {
            // Child process - use MCB allocation to find free memory
            if (isExe && exeFile is not null) {
                // Pass 0 to let memory manager find the best available block
                memBlock = _memoryManager.ReserveSpaceForExe(exeFile, 0);
            } else {
                memBlock = _memoryManager.AllocateMemoryBlock(ComFileMemoryParagraphs);
            }
            
            if (memBlock is null) {
                return DosExecResult.Failed(DosErrorCode.InsufficientMemory);
            }
            pspSegment = memBlock.DataBlockSegment;
        }

        if (memBlock is null) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Failed to allocate memory for program at segment {Segment:X4}", pspSegment);
            }
            return DosExecResult.Failed(DosErrorCode.InsufficientMemory);
        }

        // Create and register the PSP
        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(pspSegment);

        // Initialize PSP
        InitializePsp(psp, parentPspSegment, envSegment, arguments);

        // Set the disk transfer area address
        _fileManager.SetDiskTransferAreaAddress(pspSegment, DosCommandTail.OffsetInPspSegment);

        // Load the program
        ushort cs, ip, ss, sp;
        
        if (isExe && exeFile is not null) {
            // For EXE files, memory was already reserved by ReserveSpaceForExe
            // Load directly without re-reserving
            LoadExeFileIntoReservedMemory(exeFile, memBlock, out cs, out ip, out ss, out sp);
        } else {
            LoadComFileInternal(fileBytes, out cs, out ip, out ss, out sp);
        }

        if (loadType == DosExecLoadType.LoadAndExecute) {
            // Set up CPU state for execution
            _state.DS = pspSegment;
            _state.ES = pspSegment;
            _state.SS = ss;
            _state.SP = sp;
            SetEntryPoint(cs, ip);
            _state.InterruptFlag = true;
            
            return DosExecResult.Succeeded();
        } else if (loadType == DosExecLoadType.LoadOnly) {
            // Return entry point info without executing
            return DosExecResult.Succeeded(pspSegment, cs, ip, ss, sp);
        }

        return DosExecResult.Succeeded();
    }

    /// <summary>
    /// Loads the first program using the original approach that gives it ALL remaining memory.
    /// </summary>
    /// <remarks>
    /// This is the approach used in the original code and in real DOS. The first program gets
    /// all remaining conventional memory (NextSegment = LastFreeSegment), which ensures that
    /// programs that resize their memory block via INT 21h 4Ah have room to grow.
    /// This is simpler and more compatible than MCB-based allocation for the initial program.
    /// </remarks>
    private DosExecResult LoadFirstProgram(byte[] fileBytes, string hostPath, string? arguments,
        ushort parentPspSegment, ushort envSegment, DosExecLoadType loadType) {
        
        ushort pspSegment = _pspTracker.InitialPspSegment;
        
        // Create and register the PSP
        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(pspSegment);

        // Initialize PSP - this sets NextSegment = LastFreeSegment giving the program ALL memory
        InitializePsp(psp, parentPspSegment, envSegment, arguments);

        // Set the disk transfer area address
        _fileManager.SetDiskTransferAreaAddress(pspSegment, DosCommandTail.OffsetInPspSegment);

        // Determine if this is an EXE or COM file
        bool isExe = false;
        DosExeFile? exeFile = null;

        if (fileBytes.Length >= DosExeFile.MinExeSize) {
            exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            isExe = exeFile.IsValid;
        }

        // Load the program
        ushort cs, ip, ss, sp;
        
        if (isExe && exeFile is not null) {
            // For EXE files, calculate entry point based on PSP segment
            // The program entry point is immediately after the PSP (16 paragraphs = 256 bytes)
            ushort programEntryPointSegment = (ushort)(pspSegment + 0x10);
            
            LoadExeFileInMemoryAndApplyRelocations(exeFile, programEntryPointSegment);
            
            cs = (ushort)(exeFile.InitCS + programEntryPointSegment);
            ip = exeFile.InitIP;
            ss = (ushort)(exeFile.InitSS + programEntryPointSegment);
            sp = exeFile.InitSP;
        } else {
            LoadComFileInternal(fileBytes, out cs, out ip, out ss, out sp);
        }

        if (loadType == DosExecLoadType.LoadAndExecute) {
            // Set up CPU state for execution
            _state.DS = pspSegment;
            _state.ES = pspSegment;
            _state.SS = ss;
            _state.SP = sp;
            SetEntryPoint(cs, ip);
            _state.InterruptFlag = true;
            
            return DosExecResult.Succeeded();
        } else if (loadType == DosExecLoadType.LoadOnly) {
            // Return entry point info without executing
            return DosExecResult.Succeeded(pspSegment, cs, ip, ss, sp);
        }

        return DosExecResult.Succeeded();
    }

    /// <summary>
    /// Loads the first program using pre-allocated memory block.
    /// </summary>
    /// <remarks>
    /// This is used for the first program where memory was reserved BEFORE allocating the environment
    /// block to prevent the environment from taking the memory at InitialPspSegment.
    /// </remarks>
    private DosExecResult LoadProgramWithPreallocatedMemory(byte[] fileBytes, string hostPath, string? arguments,
        ushort parentPspSegment, ushort envSegment, DosExecLoadType loadType, 
        DosMemoryControlBlock memBlock, DosExeFile? exeFile, bool isExe) {
        
        ushort pspSegment = _pspTracker.InitialPspSegment;
        
        // Create and register the PSP
        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(pspSegment);

        // Initialize PSP
        InitializePsp(psp, parentPspSegment, envSegment, arguments);

        // Set the disk transfer area address
        _fileManager.SetDiskTransferAreaAddress(pspSegment, DosCommandTail.OffsetInPspSegment);

        // Load the program
        ushort cs, ip, ss, sp;
        
        if (isExe && exeFile is not null) {
            // For EXE files, memory was already reserved
            LoadExeFileIntoReservedMemory(exeFile, memBlock, out cs, out ip, out ss, out sp);
        } else {
            LoadComFileInternal(fileBytes, out cs, out ip, out ss, out sp);
        }

        if (loadType == DosExecLoadType.LoadAndExecute) {
            // Set up CPU state for execution
            _state.DS = pspSegment;
            _state.ES = pspSegment;
            _state.SS = ss;
            _state.SP = sp;
            SetEntryPoint(cs, ip);
            _state.InterruptFlag = true;
            
            return DosExecResult.Succeeded();
        } else if (loadType == DosExecLoadType.LoadOnly) {
            // Return entry point info without executing
            return DosExecResult.Succeeded(pspSegment, cs, ip, ss, sp);
        }

        return DosExecResult.Succeeded();
    }

    /// <summary>
    /// Loads an EXE file into already-reserved memory and returns entry point information.
    /// </summary>
    /// <remarks>
    /// This method is used when memory has already been reserved by ReserveSpaceForExe.
    /// It avoids the double-reservation issue that occurs when LoadExeFileInternal
    /// is called with a pre-determined PSP segment.
    /// </remarks>
    private void LoadExeFileIntoReservedMemory(DosExeFile exeFile, DosMemoryControlBlock block,
        out ushort cs, out ushort ip, out ushort ss, out ushort sp) {
        
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Loading EXE into reserved memory: {Header}", exeFile);
        }

        ushort programEntryPointSegment = (ushort)(block.DataBlockSegment + 0x10);
        
        if (exeFile.MinAlloc == 0 && exeFile.MaxAlloc == 0) {
            ushort programEntryPointOffset = (ushort)(block.Size - exeFile.ProgramSizeInParagraphsPerHeader);
            programEntryPointSegment = (ushort)(block.DataBlockSegment + programEntryPointOffset);
        }

        LoadExeFileInMemoryAndApplyRelocations(exeFile, programEntryPointSegment);

        cs = (ushort)(exeFile.InitCS + programEntryPointSegment);
        ip = exeFile.InitIP;
        ss = (ushort)(exeFile.InitSS + programEntryPointSegment);
        sp = exeFile.InitSP;
    }

    /// <summary>
    /// Initializes a PSP with the given parameters.
    /// </summary>
    private void InitializePsp(DosProgramSegmentPrefix psp, ushort parentPspSegment, 
        ushort envSegment, string? arguments) {
        
        // Set the PSP's first 2 bytes to INT 20h
        psp.Exit[0] = 0xCD;
        psp.Exit[1] = 0x20;

        psp.NextSegment = DosMemoryManager.LastFreeSegment;
        psp.ParentProgramSegmentPrefix = parentPspSegment;
        psp.EnvironmentTableSegment = envSegment;

        // Load command-line arguments
        byte[] commandLineBytes = ArgumentsToDosBytes(arguments);
        byte length = commandLineBytes[0];
        string asciiCommandLine = Encoding.ASCII.GetString(commandLineBytes, 1, length);
        psp.DosCommandTail.Length = (byte)(asciiCommandLine.Length + 1);
        psp.DosCommandTail.Command = asciiCommandLine;
    }

    /// <summary>
    /// Loads a COM file and returns entry point information.
    /// </summary>
    private void LoadComFileInternal(byte[] com, out ushort cs, out ushort ip, out ushort ss, out ushort sp) {
        ushort programEntryPointSegment = _pspTracker.GetProgramEntryPointSegment();
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(programEntryPointSegment, ComOffset);
        _memory.LoadData(physicalStartAddress, com);

        cs = programEntryPointSegment;
        ip = ComOffset;
        ss = programEntryPointSegment;
        sp = 0xFFFE; // Standard COM file stack
    }

    /// <summary>
    /// Converts the specified command-line arguments string into the format used by DOS.
    /// </summary>
    private static byte[] ArgumentsToDosBytes(string? arguments) {
        byte[] res = new byte[128];
        string correctLengthArguments = "";
        if (!string.IsNullOrWhiteSpace(arguments)) {
            correctLengthArguments = arguments.Length > 127 ? arguments[..127] : arguments;
        }

        res[0] = (byte)correctLengthArguments.Length;
        byte[] argumentsBytes = Encoding.ASCII.GetBytes(correctLengthArguments);

        int index = 0;
        for (; index < correctLengthArguments.Length; index++) {
            res[index + 1] = argumentsBytes[index];
        }

        res[index + 1] = 0x0D;
        int endIndex = index + 2;  // Include the carriage return byte
        return res[0..endIndex];
    }

    /// <summary>
    /// Legacy LoadFile implementation - used by ProgramExecutor for initial program loading.
    /// This accepts a host path and loads the program via the EXEC API, simulating
    /// how COMMAND.COM would launch a program.
    /// </summary>
    /// <remarks>
    /// This method converts the host path to a DOS path and calls the internal EXEC
    /// implementation. The program is launched as a child of COMMAND.COM, properly
    /// establishing the PSP chain.
    /// </remarks>
    public override byte[] LoadFile(string hostPath, string? arguments) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "LoadFile: COMMAND.COM launching program from host path '{HostPath}' with args '{Args}'",
                hostPath, arguments ?? "");
        }

        // Convert host path to DOS path for the EXEC call
        string dosPath = _fileManager.GetDosProgramPath(hostPath);
        
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("LoadFile: Resolved DOS path: {DosPath}", dosPath);
        }

        // Call the EXEC API - this is how COMMAND.COM launches programs
        // The EXEC method handles all the PSP setup, environment block allocation,
        // and program loading internally
        DosExecResult result = Exec(dosPath, arguments, DosExecLoadType.LoadAndExecute, environmentSegment: 0);
        
        if (!result.Success) {
            throw new UnrecoverableException(
                $"COMMAND.COM: Failed to launch program '{dosPath}': {result.ErrorCode}");
        }

        // Return file bytes for checksum verification
        return ReadFile(hostPath);
    }

    /// <summary>
    /// Creates a DOS environment block from the current environment variables.
    /// </summary>
    /// <remarks>
    /// The environment block structure is:
    /// - Environment variables as "KEY=VALUE\0" strings
    /// - Double null (\0\0) to terminate the list  
    /// - A WORD (16-bit little-endian) containing count of additional strings (usually 1)
    /// - The full program path as an ASCIZ string (used by programs to find their own executable)
    /// </remarks>
    private byte[] CreateEnvironmentBlock(string programPath) {
        using MemoryStream ms = new();

        foreach (KeyValuePair<string, string> envVar in _environmentVariables) {
            string envString = $"{envVar.Key}={envVar.Value}";
            byte[] envBytes = Encoding.ASCII.GetBytes(envString);
            ms.Write(envBytes, 0, envBytes.Length);
            ms.WriteByte(0);
        }

        ms.WriteByte(0);  // Extra null to create double-null terminator
        ms.WriteByte(1);  // WORD count = 1 (little-endian)
        ms.WriteByte(0);

        // programPath is already a full DOS path from Exec(), so we use it directly
        // The path must be the full absolute DOS path (e.g., "C:\GAMES\MYGAME.EXE")
        // so programs can find their runtime by extracting the directory from their path.
        // This is the same as what FreeDOS and MS-DOS do with truename() in task.c
        string normalizedPath = programPath.Replace('/', '\\').ToUpperInvariant();
        byte[] programPathBytes = Encoding.ASCII.GetBytes(normalizedPath);
        ms.Write(programPathBytes, 0, programPathBytes.Length);
        ms.WriteByte(0);

        return ms.ToArray();
    }

    /// <summary>
    /// Loads the program image and applies any necessary relocations.
    /// </summary>
    private void LoadExeFileInMemoryAndApplyRelocations(DosExeFile exeFile, ushort startSegment) {
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(startSegment, 0);
        _memory.LoadData(physicalStartAddress, exeFile.ProgramImage, (int)exeFile.ProgramSize);
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset)
                + physicalStartAddress;
            _memory.UInt16[addressToEdit] += startSegment;
        }
    }
}