namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
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
    private InitialHostFileAndArgs? _initialHostFileAndArgs = null;

    private record InitialHostFileAndArgs(string HostFilePath, string? Args);

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

        foreach (KeyValuePair<string, string> envVar in envVars) {
            _environmentVariables.Add(envVar.Key, envVar.Value);
        }
    }

    /// <summary>
    /// Converts the specified command-line arguments string into the format used by DOS.
    /// </summary>
    /// <param name="arguments">The command-line arguments string.</param>
    /// <returns>The command-line arguments in the format used by DOS.</returns>
    private static byte[] ArgumentsToDosBytes(string? arguments) {
        byte[] res = new byte[128];
        string correctLengthArguments = "";
        if (string.IsNullOrWhiteSpace(arguments) == false) {
            // Cut strings longer than 127 characters.
            correctLengthArguments = arguments.Length > 127 ? arguments[..127] : arguments;
        }

        // Set the command line size.
        res[0] = (byte)correctLengthArguments.Length;

        byte[] argumentsBytes = Encoding.ASCII.GetBytes(correctLengthArguments);

        // Copy the actual characters.
        int index = 0;
        for (; index < correctLengthArguments.Length; index++) {
            res[index + 1] = argumentsBytes[index];
        }

        res[index + 1] = 0x0D; // Carriage return.
        int endIndex = index + 1;
        return res[0..endIndex];
    }

    public override byte[] LoadFile(string file, string? arguments) {
        _initialHostFileAndArgs = new(file, arguments);
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
    
        // Write a word with value 1 after the environment variables
        // This is required by DOS
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

    private void LoadComFile(byte[] com) {
        ushort programEntryPointSegment = _pspTracker.GetProgramEntryPointSegment();
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(programEntryPointSegment, ComOffset);
        _memory.LoadData(physicalStartAddress, com);

        // Make DS and ES point to the PSP
        _state.DS = programEntryPointSegment;
        _state.ES = programEntryPointSegment;
        SetEntryPoint(programEntryPointSegment, ComOffset);
        _state.InterruptFlag = true;
    }

    private void LoadExeFile(DosExeFile exeFile, ushort pspSegment) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Read header: {ReadHeader}", exeFile);
        }

        DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile, pspSegment);
        if (block is null) {
            throw new UnrecoverableException($"Failed to reserve space for EXE file at {pspSegment}");
        }
        // The program image is typically loaded immediately above the PSP, which is the start of
        // the memory block that we just allocated. Seek 16 paragraphs into the allocated block to
        // get our starting point.
        ushort programEntryPointSegment = (ushort)(block.DataBlockSegment + 0x10);
        // There is one special case that we need to account for: if the EXE doesn't have any extra
        // allocations, we need to load it as high as possible in the memory block rather than
        // immediately after the PSP like we normally do. This will give the program extra space
        // between the PSP and the start of the program image that it can use however it wants.
        if (exeFile.MinAlloc == 0 && exeFile.MaxAlloc == 0) {
            ushort programEntryPointOffset = (ushort)(block.Size - exeFile.ProgramSizeInParagraphsPerHeader);
            programEntryPointSegment = (ushort)(block.DataBlockSegment + programEntryPointOffset);
        }

        LoadExeFileInMemoryAndApplyRelocations(exeFile, programEntryPointSegment);
        SetupCpuForExe(exeFile, programEntryPointSegment, pspSegment);
    }

    private void LoadExeOrComFile(string file, ushort pspSegment) {
        byte[] fileBytes = ReadFile(file);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Executable file size: {Size}", fileBytes.Length);
        }

        // Check if file size is at least EXE header size
        if (fileBytes.Length >= DosExeFile.MinExeSize) {
            // Try to read it as exe
            DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            if (exeFile.IsValid) {
                LoadExeFile(exeFile, pspSegment);
            } else {
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("File {File} does not have a valid EXE header. Considering it a COM file.", file);
                }

                LoadComFile(fileBytes);
            }
        } else {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("File {File} size is {Size} bytes, which is less than minimum allowed. Consider it a COM file.",
                    file, fileBytes.Length);
            }
            LoadComFile(fileBytes);
        }
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Initial CPU State: {CpuState}", _state);
        }
    }

    /// <summary>
    /// Loads the program image and applies any necessary relocations to it.
    /// </summary>
    /// <param name="exeFile">The EXE file to load.</param>
    /// <param name="startSegment">The starting segment for the program.</param>
    private void LoadExeFileInMemoryAndApplyRelocations(DosExeFile exeFile, ushort startSegment) {
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(startSegment, 0);
        _memory.LoadData(physicalStartAddress, exeFile.ProgramImage, (int)exeFile.ProgramSize);
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            // Read value from memory, add the start segment offset and write back
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset)
                + physicalStartAddress;
            _memory.UInt16[addressToEdit] += startSegment;
        }
    }

    /// <summary>
    /// Sets up the CPU to execute the loaded program.
    /// </summary>
    /// <param name="exeFile">The EXE file that was loaded.</param>
    /// <param name="startSegment">The starting segment address of the program.</param>
    /// <param name="pspSegment">The segment address of the program's PSP (Program Segment Prefix).</param>
    private void SetupCpuForExe(DosExeFile exeFile, ushort startSegment, ushort pspSegment) {
        // MS-DOS uses the values in the file header to set the SP and SS registers and
        // adjusts the initial value of the SS register by adding the start-segment
        // address to it.
        _state.SS = (ushort)(exeFile.InitSS + startSegment);
        _state.SP = exeFile.InitSP;

        // Make DS and ES point to the PSP
        _state.DS = pspSegment;
        _state.ES = pspSegment;

        _state.InterruptFlag = true;

        // Finally, MS-DOS reads the initial CS and IP values from the program's file
        // header, adjusts the CS register value by adding the start-segment address to
        // it, and transfers control to the program at the adjusted address.
        SetEntryPoint((ushort)(exeFile.InitCS + startSegment), exeFile.InitIP);
    }

    /// <summary>
    /// Implements DOS INT 21h AH=4Bh (EXEC - Load and/or Execute Program).
    /// </summary>
    /// <param name="programName">The path to the program to execute.</param>
    /// <param name="dosExecParameterBlock">The EXEC parameter block containing environment and FCB pointers.</param>
    /// <param name="dosExecFunction">The EXEC function to perform (Load and Execute, Load, or Load Overlay).</param>
    /// <exception cref="UnrecoverableException">Thrown if the program cannot be loaded or memory allocation fails.</exception>
    public void LoadAndOrExecute(string programName,
        DosExecParameterBlock dosExecParameterBlock, DosExecFunction dosExecFunction) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("LoadAndOrExecute: Program={Program}, Function={Function}", 
                programName, dosExecFunction);
        }

        switch (dosExecFunction) {
            case DosExecFunction.LoadAndExecute:
                LoadAndExecuteProgram(programName, dosExecParameterBlock);
                break;
            case DosExecFunction.Load:
                LoadProgramWithoutExecuting(programName, dosExecParameterBlock);
                break;
            case DosExecFunction.LoadOverlay:
                LoadOverlay(programName, dosExecParameterBlock);
                break;
            default:
                throw new UnrecoverableException($"Unsupported EXEC function: {dosExecFunction}");
        }
    }

    /// <summary>
    /// Loads and executes a program (EXEC function 0x00).
    /// Creates a new PSP, loads the program, and transfers control to it.
    /// </summary>
    private void LoadAndExecuteProgram(string programName, DosExecParameterBlock execBlock) {
        // Resolve the program path
        string? resolvedPath = _fileManager.TryGetFullHostPathFromDos(programName);
        if (resolvedPath is null || !File.Exists(resolvedPath)) {
            throw new UnrecoverableException($"Program not found: {programName}");
        }

        // Read the program file
        byte[] fileBytes = File.ReadAllBytes(resolvedPath);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Loading program: {Program}, Size: {Size} bytes", programName, fileBytes.Length);
        }

        // Allocate memory for PSP and program
        // Allocate maximum available memory for the program
        DosMemoryControlBlock? block = _memoryManager.AllocateMemoryBlock(0xFFFF);
        if (block is null) {
            throw new UnrecoverableException($"Failed to allocate memory for program: {programName}");
        }

        // Create new PSP at the allocated block
        ushort pspSegment = block.DataBlockSegment;
        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(pspSegment);

        // Initialize PSP
        InitializePsp(psp, pspSegment, execBlock, programName, resolvedPath);

        // Load the program (EXE or COM)
        LoadExeOrComFile(resolvedPath, pspSegment);

        // Copy FCBs from parent PSP to child PSP
        CopyFcbsToChildPsp(psp, execBlock);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Program loaded and ready to execute: CS:IP={CS:X4}:{IP:X4}, SS:SP={SS:X4}:{SP:X4}",
                _state.CS, _state.IP, _state.SS, _state.SP);
        }
    }

    /// <summary>
    /// Loads a program without executing it (EXEC function 0x01).
    /// Returns the entry point and initial stack in the parameter block.
    /// </summary>
    private void LoadProgramWithoutExecuting(string programName, DosExecParameterBlock execBlock) {
        // Resolve the program path
        string? resolvedPath = _fileManager.TryGetFullHostPathFromDos(programName);
        if (resolvedPath is null || !File.Exists(resolvedPath)) {
            throw new UnrecoverableException($"Program not found: {programName}");
        }

        byte[] fileBytes = File.ReadAllBytes(resolvedPath);
        
        // Allocate memory for the program
        DosMemoryControlBlock? block = _memoryManager.AllocateMemoryBlock(0xFFFF);
        if (block is null) {
            throw new UnrecoverableException($"Failed to allocate memory for program: {programName}");
        }

        ushort loadSegment = block.DataBlockSegment;

        // Try to parse as EXE
        if (fileBytes.Length >= DosExeFile.MinExeSize) {
            DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            if (exeFile.IsValid) {
                // Load EXE but don't set CPU state
                ushort programEntryPointSegment = (ushort)(loadSegment + 0x10);
                LoadExeFileInMemoryAndApplyRelocations(exeFile, programEntryPointSegment);
                
                // Return entry point and stack in the parameter block
                execBlock.ReturnedEntryPoint = new SegmentedAddress(
                    (ushort)(exeFile.InitCS + programEntryPointSegment),
                    exeFile.InitIP);
                execBlock.ReturnedInitialStack = new SegmentedAddress(
                    (ushort)(exeFile.InitSS + programEntryPointSegment),
                    exeFile.InitSP);
                
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("Loaded EXE without executing: Entry={EntryPoint}, Stack={Stack}",
                        execBlock.ReturnedEntryPoint, execBlock.ReturnedInitialStack);
                }
                return;
            }
        }

        // Load as COM
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(loadSegment, ComOffset);
        _memory.LoadData(physicalStartAddress, fileBytes);
        
        // COM files start at 0x100 with a simple stack
        execBlock.ReturnedEntryPoint = new SegmentedAddress(loadSegment, ComOffset);
        execBlock.ReturnedInitialStack = new SegmentedAddress(loadSegment, 0xFFFE);
        
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Loaded COM without executing: Entry={EntryPoint}",
                execBlock.ReturnedEntryPoint);
        }
    }

    /// <summary>
    /// Loads an overlay (EXEC function 0x03).
    /// Loads program image at specified segment with relocation.
    /// </summary>
    private void LoadOverlay(string programName, DosExecParameterBlock execBlock) {
        // For overlay loading, we need to use DosExecOverlayBlock instead
        // This is a simplified implementation - overlays use a different parameter block structure
        throw new NotImplementedException("Overlay loading (EXEC function 0x03) not yet implemented");
    }

    /// <summary>
    /// Initializes a DOS Program Segment Prefix for a child process.
    /// </summary>
    private void InitializePsp(DosProgramSegmentPrefix psp, ushort pspSegment, 
        DosExecParameterBlock execBlock, string programName, string resolvedPath) {
        // Set INT 20h at offset 0
        psp.Exit[0] = 0xCD;
        psp.Exit[1] = 0x20;

        // Set top of memory
        psp.NextSegment = DosMemoryManager.LastFreeSegment;

        // Handle environment block
        if (execBlock.EnvironmentSegment == 0) {
            // Inherit parent's environment or create new one
            byte[] environmentBlock = CreateEnvironmentBlock(resolvedPath);
            ushort envSegment = (ushort)(pspSegment + 1);
            _memory.LoadData(MemoryUtils.ToPhysicalAddress(envSegment, 0), environmentBlock);
            psp.EnvironmentTableSegment = envSegment;
        } else {
            // Use provided environment segment
            psp.EnvironmentTableSegment = execBlock.EnvironmentSegment;
        }

        // Copy command tail from parent PSP to child PSP
        SegmentedAddress cmdTailPtr = execBlock.CommandTailPointer;
        if (cmdTailPtr.Segment != 0 || cmdTailPtr.Offset != 0) {
            uint cmdTailAddress = MemoryUtils.ToPhysicalAddress(cmdTailPtr.Segment, cmdTailPtr.Offset);
            byte length = _memory.UInt8[cmdTailAddress];
            psp.DosCommandTail.Length = length;
            
            // Copy command tail characters
            StringBuilder cmdLine = new StringBuilder();
            for (int i = 0; i < length; i++) {
                char c = (char)_memory.UInt8[cmdTailAddress + 1 + (uint)i];
                cmdLine.Append(c);
            }
            psp.DosCommandTail.Command = cmdLine.ToString();
        }

        // Set DTA to command tail
        _fileManager.SetDiskTransferAreaAddress(pspSegment, DosCommandTail.OffsetInPspSegment);
    }

    /// <summary>
    /// Copies FCBs from the parent PSP to the child PSP.
    /// </summary>
    private void CopyFcbsToChildPsp(DosProgramSegmentPrefix childPsp, DosExecParameterBlock execBlock) {
        // Copy first FCB (at 0x5C in PSP)
        SegmentedAddress fcb1Ptr = execBlock.Fcb1Pointer;
        if (fcb1Ptr.Segment != 0 || fcb1Ptr.Offset != 0) {
            uint fcb1SourceAddr = MemoryUtils.ToPhysicalAddress(fcb1Ptr.Segment, fcb1Ptr.Offset);
            for (int i = 0; i < 16; i++) {
                childPsp.FirstFileControlBlock[i] = _memory.UInt8[fcb1SourceAddr + (uint)i];
            }
        }

        // Copy second FCB (at 0x6C in PSP)
        SegmentedAddress fcb2Ptr = execBlock.Fcb2Pointer;
        if (fcb2Ptr.Segment != 0 || fcb2Ptr.Offset != 0) {
            uint fcb2SourceAddr = MemoryUtils.ToPhysicalAddress(fcb2Ptr.Segment, fcb2Ptr.Offset);
            for (int i = 0; i < 16; i++) {
                childPsp.SecondFileControlBlock[i] = _memory.UInt8[fcb2SourceAddr + (uint)i];
            }
        }
    }

    internal void SetupInitialProgram() {
        if(_initialHostFileAndArgs is null) {
            throw new InvalidOperationException("DOS File loader wasn't initialized. Did you call LoadFile ?");
        }
        string? arguments = _initialHostFileAndArgs.Args;
        string file = _initialHostFileAndArgs.HostFilePath;
        // TODO: We should be asking DosMemoryManager for a new block for the PSP, program, its
        // stack, and its requested extra space first. We shouldn't always assume that this is the
        // first program to be loaded and that we have enough space for it like we do right now.
        // This will need to be fixed for DOS program load/exec support.
        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(_pspTracker.InitialPspSegment);
        ushort pspSegment = MemoryUtils.ToSegment(psp.BaseAddress);

        // Set the PSP's first 2 bytes to INT 20h.
        psp.Exit[0] = 0xCD;
        psp.Exit[1] = 0x20;

        psp.NextSegment = DosMemoryManager.LastFreeSegment;

        // Load the command-line arguments into the PSP's command tail.
        byte[] commandLineBytes = ArgumentsToDosBytes(arguments);
        byte length = commandLineBytes[0];
        string asciiCommandLine = Encoding.ASCII.GetString(commandLineBytes, 1, length);
        psp.DosCommandTail.Length = (byte)(asciiCommandLine.Length + 1);
        psp.DosCommandTail.Command = asciiCommandLine;

        byte[] environmentBlock = CreateEnvironmentBlock(file);

        // In the PSP, the Environment Block Segment field (defined at offset 0x2C) is a word, and is a pointer.
        ushort envBlockPointer = (ushort)(pspSegment + 1);
        SegmentedAddress envBlockSegmentAddress = new SegmentedAddress(envBlockPointer, 0);

        // Copy the environment block to memory in a separated segment.
        _memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlockSegmentAddress.Segment,
            envBlockSegmentAddress.Offset), environmentBlock);

        // Point the PSP's environment segment to the environment block.
        psp.EnvironmentTableSegment = envBlockSegmentAddress.Segment;

        // Set the disk transfer area address to the command-line offset in the PSP.
        _fileManager.SetDiskTransferAreaAddress(
            pspSegment, DosCommandTail.OffsetInPspSegment);

        LoadExeOrComFile(file, pspSegment);
    }
}