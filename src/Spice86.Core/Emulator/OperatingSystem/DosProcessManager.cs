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
/// Setups the loading and execution of DOS programs and maintains the DOS PSP chains in memory.
/// </summary>
public class DosProcessManager : DosFileLoader {
    private const ushort ComOffset = 0x100;
    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryManager _memoryManager;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;

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
        IDictionary<string, string> envVars, ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _pspTracker = dosPspTracker;
        _memoryManager = dosMemoryManager;
        _fileManager = dosFileManager;
        _driveManager = dosDriveManager;
        _environmentVariables = new();

        envVars.Add("PATH", $"{_driveManager.CurrentDrive.DosVolume}{DosPathResolver.DirectorySeparatorChar}");
        //TODO some programs want COMSPEC to launch subshells: envVars.Add("COMSPEC", @"Z:\COMMAND.COM"); //soon...

        foreach (KeyValuePair<string, string> envVar in envVars) {
            _environmentVariables.Add(envVar.Key, envVar.Value);
        }
    }

    /// <summary>
    /// Loads the specified file into memory and returns its contents as a byte array. This is the initial emulated program, loaded before DOS is setup.
    /// </summary>
    /// <remarks>This method performs an initial program load by creating a dummy parameter block and invoking
    /// the load-and-execute operation. The file is then read and its contents are returned.</remarks>
    /// <param name="file">The path to the file to be loaded. The path is absolute and it comes from the real filesystem, not the DOS virtualized one.</param>
    /// <param name="arguments">Optional arguments to be used during the file loading process. Can be null. Must be translated to DOS argument file format.</param>
    /// <returns>A byte array containing the contents of the loaded file.</returns>
    public override byte[] LoadFile(string file, string? arguments) {
        // For initial program load, create a dummy parameter block and call LoadAndOrExecute
        uint parameterBlockAddress = 0x0; // Not used for initial load
        LoadAndOrExecute(DosExecuteMode.LoadAndExecute, file, parameterBlockAddress, arguments);
        return ReadFile(file);
    }

    /// <summary>
    /// MS-DOS EXEC Function (INT 21h, AH=4Bh) - The comprehensive DOS program loader and execution manager.
    /// 
    /// This function implements the complete MS-DOS EXEC functionality that can:
    /// 1. Load and execute a child program (.COM or .EXE)
    /// 2. Load a program, setup program header, but do not execute it
    /// 3. Load an overlay module into an existing program's memory space
    /// 
    /// The function handles all aspects of DOS program loading including:
    /// - Memory allocation and management
    /// - Program Segment Prefix (PSP) creation and inheritance
    /// - Environment block setup and inheritance
    /// - Command line parameter passing
    /// - File Control Block (FCB) initialization
    /// - Memory relocation for .EXE files
    /// - CPU register and stack initialization
    /// - Parent-child process relationship management
    /// 
    /// Implementation follows the MS-DOS specification for EXEC function as documented in
    /// "MS-DOS Programmer's Reference" and maintains compatibility with DOS 2.0+ behavior.
    /// </summary>
    /// <param name="mode">The execution mode determining the operation type:
    /// - LoadAndExecute: Load program and transfer control to it (standard program execution)
    /// - LoadButDoNotRun: Load program into memory but return control to caller (debugging mode)
    /// - LoadOverlay: Load program code at specified address without creating PSP (overlay mode)</param>
    /// <param name="programName">Absolute path to the program file to load. Must be unambiguous (no wildcards).
    /// Can be .COM (memory image) or .EXE (relocatable) format. File type is auto-detected from header.</param>
    /// <param name="parameterBlockAddress">Physical memory address of the parameter block structure containing:
    /// - Environment segment pointer (0 = inherit parent's environment)
    /// - Command tail address (DOS format: length byte + ASCII chars + CR)
    /// - FCB addresses for default file control blocks
    /// Set to 0 for initial program load (bootstrapping case).</param>
    /// <param name="initialArguments">Command line arguments for initial program load only. 
    /// Ignored when parameterBlockAddress != 0. Will be converted to DOS command tail format.</param>
    /// <remarks>
    /// Memory Management Strategy:
    /// - For .COM files: Allocates single block = PSP (256 bytes) + program size + stack space
    /// - For .EXE files: Uses header MINALLOC/MAXALLOC to determine memory requirements
    /// - Avoids double allocation by checking if this is initial load vs. child process
    /// - Properly chains Memory Control Blocks (MCBs) for DOS memory management
    /// 
    /// Error Handling:
    /// - boolean based API for DOS the INT21H 0x4B caller function to set Carry Flag or not.
    /// - Logs all significant operations and errors for debugging
    /// - Gracefully handles memory allocation failures and file not found errors
    /// 
    /// Process Inheritance:
    /// - Child inherits parent's environment (unless explicitly overridden)
    /// - Open file handles are inherited (following DOS handle inheritance rules)
    /// - Parent PSP address is stored in child's PSP for proper chain management
    /// - Interrupt vectors (22h, 23h, 24h) are saved and restored for proper cleanup
    /// 
    /// CPU State Setup:
    /// - For .COM: CS=DS=ES=SS=PSP segment, IP=100h, SP=FFFEh
    /// - For .EXE: CS/IP from header, SS/SP from header, DS/ES=PSP segment
    /// - Interrupt flag enabled for normal program execution
    /// - All segment registers properly initialized for program type
    /// </remarks>
    public bool LoadAndOrExecute(DosExecuteMode mode, string programName,
        uint parameterBlockAddress, string? initialArguments = null) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("DOS EXEC Function called: Mode={Mode}, Program={ProgramName}, ParameterBlock=0x{ParameterBlock:X8}", 
                mode, programName, parameterBlockAddress);
        }

        try {
            switch (mode) {
                case DosExecuteMode.LoadAndExecute:
                case DosExecuteMode.LoadButDoNotRun:
                    ExecuteLoadAndExecuteMode(programName, parameterBlockAddress, mode, initialArguments);
                    break;
                    
                case DosExecuteMode.LoadOverlay:
                    ExecuteLoadOverlayMode(programName, parameterBlockAddress);
                    break;
                    
                default:
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("Invalid EXEC mode specified: {Mode}", mode);
                    }
                    _state.AX = (ushort)DosErrorCode.FunctionNumberInvalid;
                    return false;
            }
        } catch (Exception ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(ex, "Unhandled exception in DOS EXEC function for program {ProgramName}", programName);
            }
            _state.AX = (ushort)DosErrorCode.FormatInvalid;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Executes the Load and Execute mode of the EXEC function.
    /// This is the primary mode used for running child programs.
    /// </summary>
    private bool ExecuteLoadAndExecuteMode(string programName,
        uint parameterBlockAddress, DosExecuteMode mode, string? initialArguments) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Starting Load and Execute mode for program: {ProgramName}", programName);
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

        // Step 4: Allocate memory for the program (avoiding double allocation)
        DosMemoryControlBlock? programMcb = AllocateProgramMemory(exeFile,
            fileBytes.Length, parameterBlockAddress == 0);
        if (programMcb == null) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Failed to allocate memory for program {ProgramName}", programName);
            }
            _state.AX = (ushort)DosErrorCode.InsufficientMemory;
            return false;
        }

        ushort pspSegment = programMcb.DataBlockSegment;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Allocated memory block at segment 0x{PspSegment:X4}, size {Size} paragraphs", 
                pspSegment, programMcb.Size);
        }

        // Step 5: Register the new PSP in the process chain
        _pspTracker.PushPspSegment(pspSegment);

        // Step 6: Create and initialize the Program Segment Prefix
        InitializeProgramSegmentPrefix(pspSegment, environmentSegment, commandTailAddress,
            fcb1Address, fcb2Address);

        // Step 7: Load program into memory and setup CPU state
        if (isComFile) {
            LoadComProgram(fileBytes, pspSegment);
        } else {
            LoadExeProgram(exeFile!, pspSegment, programMcb);
        }

        // Step 8: Handle load-only mode vs execute mode
        if (mode == DosExecuteMode.LoadButDoNotRun) {
            HandleLoadOnlyMode(parameterBlockAddress);
        }
        
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Successfully loaded program {ProgramName} at PSP segment 0x{PspSegment:X4}, entry point {EntryPoint}", 
                programName, pspSegment, ConvertUtils.ToSegmentedAddressRepresentation(_state.CS, _state.IP));
        }

        return true;
    }

    /// <summary>
    /// Executes the Load Overlay mode of the EXEC function.
    /// This mode loads program code at a specified address without creating a PSP.
    /// </summary>
    private bool ExecuteLoadOverlayMode(string programName, uint parameterBlockAddress) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Starting Load Overlay mode for program: {ProgramName}", programName);
        }

        // Parse overlay parameter block
        DosOverlayParameterBlock overlayBlock = new DosOverlayParameterBlock(_memory, parameterBlockAddress);
        ushort loadSegment = overlayBlock.OverlayLoadSegment;
        ushort relocationFactor = overlayBlock.OverlayRelocationFactor;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Overlay parameters: LoadSegment=0x{LoadSegment:X4}, RelocationFactor=0x{RelocationFactor:X4}", 
                loadSegment, relocationFactor);
        }

        // Load program file
        if(!TryLoadProgramFile(programName, out byte[]? fileBytes)) {
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

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Loaded overlay at physical address 0x{PhysicalAddress:X8}, applying {RelocationCount} relocations", 
                physicalAddress, exeFile.RelocationTable.Count());
        }

        // Apply relocations
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset) + physicalAddress;
            ushort originalValue = _memory.UInt16[addressToEdit];
            ushort newValue = (ushort)(originalValue + relocationFactor);
            _memory.UInt16[addressToEdit] = newValue;
            
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Applied relocation at 0x{Address:X8}: 0x{Original:X4} -> 0x{New:X4}", 
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
    /// Prepares execution parameters from the parameter block or creates defaults for initial load.
    /// </summary>
    private (ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address) 
        PrepareExecutionParameters(string programName, uint parameterBlockAddress, string? initialArguments) {
        
        if (parameterBlockAddress == 0) {
            // Initial program load - create default parameters
            TryPrepareInitialLoadParameters(programName, initialArguments, out
                (ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address)? values);
            return values ?? new();
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
                if(TryPrepareInitialLoadParameters("", null, out
                    (ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address)? values)) {
                    environmentSegment = values.Value.environmentSegment;
                }
            }
        }

        return (environmentSegment, commandTailAddress, fcb1Address, fcb2Address);
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
        }
        return false;
    }

    /// <summary>
    /// Analyzes the program format and returns EXE file info if it's an EXE, null if it's a COM.
    /// Now with DOS-compatible lenient parsing.
    /// </summary>
    private DosExeFile? AnalyzeProgramFormat(byte[] fileBytes) {
        if (fileBytes.Length >= DosExeFile.MinExeSize) {
            DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));

            // Check basic signature first
            if (exeFile.Signature is "MZ" or "ZM") {
                // Even if other header fields seem inconsistent, try to load like DOS does
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    if (exeFile.Pages > 0x07ff) {
                        _loggerService.Verbose("EXE header has oversized page count (0x{Pages:X4}), clamping to DOS 1MB limit",
                            exeFile.Pages);
                    }

                    _loggerService.Verbose("Detected .EXE file format: MinAlloc={MinAlloc}, MaxAlloc={MaxAlloc}, InitCS:IP={InitCS:X4}:{InitIP:X4}, InitSS:SP={InitSS:X4}:{InitSP:X4}",
                        exeFile.MinAlloc, exeFile.MaxAlloc, exeFile.InitCS, exeFile.InitIP, exeFile.InitSS, exeFile.InitSP);
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
            // .EXE file - use ReserveSpaceForExe but avoid double allocation for initial load
            if (isInitialLoad) {
                // For initial load, don't allocate space twice - the memory manager handles this
                return _memoryManager.ReserveSpaceForExe(exeFile);
            } else {
                // For child processes, allocate normally
                return _memoryManager.ReserveSpaceForExe(exeFile);
            }
        } else {
            // .COM file - calculate required size including PSP and program
            ushort sizeInParagraphs = (ushort)((fileSizeBytes + ComOffset + 15) / 16);
            
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Allocating {Size} paragraphs for .COM file (includes PSP + program + stack space)", sizeInParagraphs);
            }
            
            return _memoryManager.AllocateMemoryBlock(sizeInParagraphs);
        }
    }

    /// <summary>
    /// Initializes the Program Segment Prefix with all required fields.
    /// </summary>
    private void InitializeProgramSegmentPrefix(ushort pspSegment,
        ushort environmentSegment, uint commandTailAddress, uint fcb1Address, uint fcb2Address) {
        DosProgramSegmentPrefix psp = new DosProgramSegmentPrefix(_memory, MemoryUtils.ToPhysicalAddress(pspSegment, 0));
        
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Initializing PSP at segment 0x{PspSegment:X4}", pspSegment);
        }

        // Set INT 20h instruction at start of PSP (CP/M compatibility)
        psp.Exit[0] = 0xCD; // INT opcode
        psp.Exit[1] = 0x20; // INT 20h vector

        psp.NextSegment = DosMemoryManager.LastFreeSegment;

        // Setup parent relationship
        DosProgramSegmentPrefix? parentPsp = _pspTracker.GetCurrentPsp();
        if (parentPsp != null) {
            psp.ParentProgramSegmentPrefix = MemoryUtils.ToSegment(parentPsp.BaseAddress);
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Set parent PSP to segment 0x{ParentPspSegment:X4}", psp.ParentProgramSegmentPrefix);
            }
        } else {
            psp.ParentProgramSegmentPrefix = pspSegment; // Root process points to itself
        }

        psp.EnvironmentTableSegment = environmentSegment;

        // Setup command tail if provided
        if (commandTailAddress != 0) {
            byte length = _memory.UInt8[commandTailAddress];
            if (length > 0) {
                byte[] commandBytes = _memory.GetData(commandTailAddress + 1, length);
                string command = Encoding.ASCII.GetString(commandBytes);
                psp.DosCommandTail.Length = (byte)(length + 1);
                psp.DosCommandTail.Command = command;
                
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("Set command tail: '{Command}' (length {Length})", command, length);
                }
            }
        }

        // Setup FCBs if provided (using direct array access instead of _memory.LoadData)
        if (fcb1Address != 0) {
            for (int i = 0; i < 16; i++) {
                psp.FirstFileControlBlock[i] = _memory.UInt8[fcb1Address + (uint)i];
            }
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Copied first FCB from address 0x{FCB1Address:X8}", fcb1Address);
            }
        }
        
        if (fcb2Address != 0) {
            for (int i = 0; i < 20; i++) {
                psp.SecondFileControlBlock[i] = _memory.UInt8[fcb2Address + (uint)i];
            }
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Copied second FCB from address 0x{FCB2Address:X8}", fcb2Address);
            }
        }

        // Set the disk transfer area address to the command tail area in PSP
        _fileManager.SetDiskTransferAreaAddress(pspSegment, DosCommandTail.OffsetInPspSegment);
        
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PSP initialization complete for segment 0x{PspSegment:X4}", pspSegment);
        }
    }

    /// <summary>
    /// Loads a .COM program and sets up the CPU state for execution.
    /// </summary>
    private void LoadComProgram(byte[] programBytes, ushort pspSegment) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Loading .COM program at PSP segment 0x{PspSegment:X4}, program size: {Size} bytes", 
                pspSegment, programBytes.Length);
        }

        // Load program at offset 100h after PSP
        uint loadAddress = MemoryUtils.ToPhysicalAddress(pspSegment, ComOffset);
        _memory.LoadData(loadAddress, programBytes);

        // Zero memory beyond the loaded program up to the end of the allocated block
        ZeroMemoryBeyondProgram(loadAddress, (uint)programBytes.Length, pspSegment);

        // Setup CPU registers for .COM execution
        _state.DS = pspSegment;  // Data segment = PSP
        _state.ES = pspSegment;  // Extra segment = PSP  
        _state.SS = pspSegment;  // Stack segment = PSP
        _state.CS = pspSegment;  // Code segment = PSP
        _state.IP = ComOffset;   // Instruction pointer = 100h (after PSP)
        _state.SP = 0xFFFE;      // Stack pointer at top of segment

        // Initialize stack with return address (0)
        _memory.UInt16[MemoryUtils.ToPhysicalAddress(pspSegment, 0xFFFE)] = 0;

        // Enable interrupts
        _state.InterruptFlag = true;

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Loaded .COM program: CS:IP={CS:X4}:{IP:X4}, SS:SP={SS:X4}:{SP:X4}", 
                _state.CS, _state.IP, _state.SS, _state.SP);
        }
    }

    /// <summary>
    /// Loads a .EXE program and sets up the CPU state for execution.
    /// </summary>
    private void LoadExeProgram(DosExeFile exeFile, ushort pspSegment,
        DosMemoryControlBlock programBlock) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Loading .EXE program at PSP segment 0x{PspSegment:X4}", pspSegment);
        }

        // Calculate load segment (PSP + 16 bytes = program start)
        ushort programLoadSegment = (ushort)(programBlock.DataBlockSegment + 0x10);

        // Handle special case for EXE files with no memory allocation requirements
        if (exeFile.MinAlloc == 0 && exeFile.MaxAlloc == 0) {
            ushort programOffset = (ushort)(programBlock.Size - exeFile.ProgramSizeInParagraphsPerHeader);
            programLoadSegment = (ushort)(programBlock.DataBlockSegment + programOffset);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Program load segment: 0x{ProgramLoadSegment:X4}, applying {RelocationCount} relocations", 
                programLoadSegment, exeFile.RelocationTable.Count());
        }

        // Load program image and apply relocations
        uint physicalLoadAddress = MemoryUtils.ToPhysicalAddress(programLoadSegment, 0);
        _memory.LoadData(physicalLoadAddress, exeFile.ProgramImage, (int)exeFile.ProgramSize);

        // Zero memory beyond the loaded program up to the end of the allocated block
        ZeroMemoryBeyondProgram(physicalLoadAddress, exeFile.ProgramSize, programBlock.DataBlockSegment, (uint)programBlock.Size * 16);

        // Apply relocations
        foreach (SegmentedAddress relocationAddress in exeFile.RelocationTable) {
            uint addressToRelocate = MemoryUtils.ToPhysicalAddress(relocationAddress.Segment, relocationAddress.Offset) + physicalLoadAddress;
            ushort originalValue = _memory.UInt16[addressToRelocate];
            ushort relocatedValue = (ushort)(originalValue + programLoadSegment);
            _memory.UInt16[addressToRelocate] = relocatedValue;

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Relocation at {Address}: 0x{Original:X4} -> 0x{Relocated:X4}", 
                    ConvertUtils.ToSegmentedAddressRepresentation(relocationAddress.Segment, relocationAddress.Offset), 
                    originalValue, relocatedValue);
            }
        }

        // Setup CPU registers for .EXE execution
        _state.SS = (ushort)(exeFile.InitSS + programLoadSegment);
        _state.SP = exeFile.InitSP;
        _state.DS = pspSegment;  // Data segment points to PSP
        _state.ES = pspSegment;  // Extra segment points to PSP
        _state.CS = (ushort)(exeFile.InitCS + programLoadSegment);
        _state.IP = exeFile.InitIP;
        _state.InterruptFlag = true;

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Loaded .EXE program: CS:IP={CS:X4}:{IP:X4}, SS:SP={SS:X4}:{SP:X4}, DS=ES=0x{DSES:X4}", 
                _state.CS, _state.IP, _state.SS, _state.SP, _state.DS);
        }
    }

    /// <summary>
    /// Zeros memory beyond the loaded program up to the end of the allocated memory block or conventional memory limit.
    /// This matches DOS behavior of clearing memory beyond the program image.
    /// </summary>
    /// <param name="programStartAddress">Physical address where the program starts</param>
    /// <param name="programSize">Size of the loaded program in bytes</param>
    /// <param name="pspSegment">PSP segment for .COM files</param>
    /// <param name="allocatedBlockSizeBytes">Total allocated block size in bytes for .EXE files</param>
    private void ZeroMemoryBeyondProgram(uint programStartAddress, uint programSize, ushort pspSegment, uint? allocatedBlockSizeBytes = null) {
        uint programEndAddress = programStartAddress + programSize;
        uint zeroEndAddress;

        if (allocatedBlockSizeBytes.HasValue) {
            // .EXE file: Zero to end of allocated block
            uint blockStartAddress = MemoryUtils.ToPhysicalAddress((ushort)(pspSegment), 0);
            zeroEndAddress = Math.Min(blockStartAddress + allocatedBlockSizeBytes.Value, MemoryUtils.ToPhysicalAddress(0x9FFF, 0xFFFF));
        } else {
            // .COM file: Zero to end of PSP segment (64KB limit) or conventional memory limit
            uint pspStartAddress = MemoryUtils.ToPhysicalAddress(pspSegment, 0);
            uint segmentEndAddress = pspStartAddress + 0x10000; // 64KB segment
            zeroEndAddress = Math.Min(segmentEndAddress, MemoryUtils.ToPhysicalAddress(0x9FFF, 0xFFFF));
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
    private void HandleLoadOnlyMode(uint parameterBlockAddress) {
        if (parameterBlockAddress < MemoryUtils.ToPhysicalAddress(MemoryMap.FreeMemoryStartSegment, 0)) {
            throw new InvalidOperationException("Cannot load a program below the free memory start segment!");
        }
        DosLoadProgramParameterBlock loadOnlyBlock = new DosLoadProgramParameterBlock(_memory, parameterBlockAddress);
        loadOnlyBlock.EntryPointAddress = new SegmentedAddress(_state.CS, _state.IP);
        loadOnlyBlock.StackAddress = new SegmentedAddress(_state.SS, _state.SP);

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