namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

public class DosProcessManager : DosFileLoader {
    private const ushort ComOffset = 0x100;
    private readonly ushort _startSegment;
    private readonly ushort _pspSegment;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;
    private readonly DosMemoryManager _memoryManager;

    /// <summary>
    /// The master environment block that all DOS PSPs inherit.
    /// </summary>
    /// <remarks>
    /// Not stored in emulated memory, so no one can modify it.
    /// </remarks>
    private readonly EnvironmentVariables _environmentVariables;

    public const ushort LastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 1;

    public ushort PspSegment => _pspSegment;

    public DosProgramSegmentPrefix CurrentPsp { get; set; }

    public DosProcessManager(Configuration configuration, IMemory memory,
        State state, DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        DosMemoryManager dosMemoryManager,
        IDictionary<string, string> envVars, ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _fileManager = dosFileManager;
        _memoryManager = dosMemoryManager;
        _driveManager = dosDriveManager;
        _environmentVariables = new();
        _startSegment = configuration.ProgramEntryPointSegment;
        _pspSegment = (ushort)(_startSegment - 0x10);
        CurrentPsp = new(memory, MemoryUtils.ToPhysicalAddress(_pspSegment, 0));

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
        DosProgramSegmentPrefix psp = CurrentPsp;

        // Set the PSP's first 2 bytes to INT 20h.
        psp.Exit[0] = 0xCD;
        psp.Exit[1] = 0x20;

        psp.NextSegment = LastFreeSegment;

        // Load the command-line arguments into the PSP's command tail.
        byte[] commandLineBytes = ArgumentsToDosBytes(arguments);
        byte length = commandLineBytes[0];
        string asciiCommandLine = Encoding.ASCII.GetString(commandLineBytes, 1, length);
        psp.DosCommandTail.Length = (byte)(asciiCommandLine.Length + 1);
        psp.DosCommandTail.Command = asciiCommandLine;

        byte[] environmentBlock = CreateEnvironmentBlock(file);

        // In the PSP, the Environment Block Segment field (defined at offset 0x2C) is a word, and is a pointer.
        int envBlockPointer = PspSegment + 1;
        SegmentedAddress envBlockSegmentAddress = new SegmentedAddress((ushort)envBlockPointer, 0);

        // Copy the environment block to memory in a separated segment.
        _memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlockSegmentAddress.Segment,
            envBlockSegmentAddress.Offset), environmentBlock);

        // Point the PSP's environment segment to the environment block.
        psp.EnvironmentTableSegment = envBlockSegmentAddress.Segment;

        // Set the disk transfer area address to the command-line offset in the PSP.
        _fileManager.SetDiskTransferAreaAddress(PspSegment, DosCommandTail.OffsetInPspSegment);

        return Path.GetExtension(file).ToUpperInvariant() switch {
            ".COM" => LoadComFile(file),
            ".EXE" => LoadExeFile(file, PspSegment),
            _ => throw new UnrecoverableException($"Unsupported file type for DOS: {file}"),
        };

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



    private byte[] LoadComFile(string file) {
        byte[] com = ReadFile(file);
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(_startSegment, ComOffset);
        _memory.LoadData(physicalStartAddress, com);

        // Make DS and ES point to the PSP
        _state.DS = _startSegment;
        _state.ES = _startSegment;
        SetEntryPoint(_startSegment, ComOffset);
        _state.InterruptFlag = true;
        return com;
    }

    private byte[] LoadExeFile(string file, ushort pspSegment) {
        byte[] exe = ReadFile(file);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Exe size: {ExeSize}", exe.Length);
        }
        DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(exe));
        if (!exeFile.IsValid) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Invalid EXE file {File}", file);
            }
            throw new UnrecoverableException($"Invalid EXE file {file}");
        }
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Read header: {ReadHeader}", exeFile);
        }

        LoadExeFileInMemoryAndApplyRelocations(exeFile, _startSegment);
        SetupCpuForExe(exeFile, _startSegment, pspSegment);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Initial CPU State: {CpuState}", _state);
        }
        return exe;
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

    internal bool TryExecute(string dosFilePath, DosExecParameterBlock execParameterBlock, ushort flags,
        [NotNullWhen(false)] out ErrorCode? errorCode) {
        try {
            // Open the file
            DosFileOperationResult openFileResult = _fileManager.OpenFile(dosFilePath, FileAccessMode.ReadOnly);
            if (openFileResult.IsError) {
                errorCode = ErrorCode.FileNotFound;
                return false;
            }
            ushort fileHandle = (ushort)openFileResult.Value!.Value;

            // Remove loadhigh flag if present
            if ((flags & 0x80) > 0) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Using loadhigh flag - dropping it");
                }
                flags &= 0x7f;
            }

            // Convert flags to operation type
            DosExecOperation operation = (DosExecOperation)flags;

            try {
                // Read file header to determine if COM or EXE
                bool isComFile = true;
                uint headerSize = 0;
                uint imageSize = 0;
                DosExeFile? exeFile = null;

                // Use a temporary buffer for reading the header
                byte[] headerBuffer = new byte[0x1C]; // EXE header size

                // Read the first bytes to detect file type
                ushort readLength = 0x1C;

                // Create a temporary buffer in memory using PSP's memory area 
                uint tempBufferAddress = MemoryUtils.ToPhysicalAddress(_pspSegment, 0x100);
                DosFileOperationResult readResult = _fileManager.ReadFile(fileHandle, readLength, tempBufferAddress);

                if (readResult.IsError || !readResult.Value.HasValue) {
                    errorCode = ErrorCode.AccessDenied;
                    return false;
                }

                // Check for zero-byte files
                if (readResult.Value.Value == 0) {
                    errorCode = ErrorCode.AccessDenied;
                    return false;
                }

                // Copy header from memory to our buffer
                for (int i = 0; i < readResult.Value.Value; i++) {
                    headerBuffer[i] = _memory.UInt8[tempBufferAddress + (uint)i];
                }

                // Check if it's an EXE by signature
                if (headerBuffer.Length >= 2 &&
                    ((headerBuffer[0] == 'M' && headerBuffer[1] == 'Z') ||
                     (headerBuffer[0] == 'Z' && headerBuffer[1] == 'M'))) {
                    isComFile = false;
                    exeFile = new DosExeFile(new ByteArrayReaderWriter(headerBuffer));

                    // Only verify the signature is valid when working with just the header
                    if (exeFile.Signature is not ("MZ" or "ZM")) {
                        errorCode = ErrorCode.DataInvalid;
                        return false;
                    }

                    headerSize = exeFile.HeaderSizeInBytes;
                    imageSize = exeFile.ProgramSize;
                }

                // Variables for memory allocation
                ushort pspseg = 0, envseg = 0, loadseg = 0, memsize = 0;

                if (operation != DosExecOperation.LoadOverlay) {
                    // Create environment block
                    envseg = execParameterBlock.EnvironmentSegment;
                    if (envseg == 0) {
                        // Create from parent environment
                        var psp = new DosProgramSegmentPrefix(_memory, MemoryUtils.ToPhysicalAddress(_state.DS, 0));
                        envseg = psp.EnvironmentTableSegment;

                        // Create a copy of the environment block
                        byte[] environmentBlock = CreateEnvironmentBlock(dosFilePath);

                        // Allocate memory for environment
                        ushort envSize = (ushort)((environmentBlock.Length + 15) / 16 + 1); // Round up to paragraphs
                        if (!AllocateMemory(ref envseg, envSize)) {
                            errorCode = ErrorCode.InsufficientMemory;
                            return false;
                        }

                        // Copy environment block to allocated memory
                        _memory.LoadData(MemoryUtils.ToPhysicalAddress(envseg, 0), environmentBlock);
                    }

                    // Determine memory requirements
                    ushort minsize, maxsize;

                    // Get maximum available memory
                    DosMemoryControlBlock largestFreeBlock = _memoryManager.FindLargestFree();
                    ushort maxfree = largestFreeBlock.Size;

                    if (isComFile) {
                        minsize = 0x1000; // 64K minimum for COM files
                        maxsize = 0xFFFF; // Maximum value
                    } else {
                        // Calculate from EXE header
                        minsize = (ushort)((imageSize + (exeFile!.MinAlloc << 4) + 256 + 15) / 16);
                        if (exeFile.MaxAlloc != 0) {
                            maxsize = (ushort)((imageSize + (exeFile.MaxAlloc << 4) + 256 + 15) / 16);
                        } else {
                            maxsize = 0xFFFF;
                        }
                    }

                    // Check if we have enough memory
                    if (maxfree < minsize) {
                        if (isComFile) {
                            // For COM files, try to reduce memory requirements
                            _fileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, fileHandle, 0);
                            byte[] comBuffer = new byte[0xF800];

                            // Read directly from file to get actual size
                            readResult = _fileManager.ReadFile(fileHandle, 0xF800, tempBufferAddress);

                            if (!readResult.IsError && readResult.Value.HasValue && readResult.Value.Value < 0xF800) {
                                minsize = (ushort)(((readResult.Value.Value + 0x10) >> 4) + 0x20);
                            }
                        }

                        if (maxfree < minsize) {
                            errorCode = ErrorCode.InsufficientMemory;
                            return false;
                        }
                    }

                    // Allocate memory (use smaller of maxfree or maxsize)
                    memsize = maxfree < maxsize ? maxfree : maxsize;

                    if (!AllocateMemory(ref pspseg, memsize)) {
                        errorCode = ErrorCode.InsufficientMemory;
                        return false;
                    }

                    // Set PSP fields and copy file handles
                    DosProgramSegmentPrefix pspStruct = SetupPsp(pspseg, memsize, envseg);
                    CopyFileHandles(pspStruct);
                    SetupCommandLine(pspStruct, execParameterBlock);

                    // Set load segment (PSP + 16 paragraphs for COM/EXE)
                    loadseg = (ushort)(pspseg + 16);

                    // For EXE files with no memory requirements, load at high end
                    if (!isComFile && exeFile!.MinAlloc == 0 && exeFile.MaxAlloc == 0) {
                        loadseg = (ushort)(((pspseg + memsize) * 16 - imageSize) / 16);
                    }
                } else {
                    // Overlay mode - use the specified segment directly
                    loadseg = execParameterBlock.LoadSegment;
                }

                // Load the executable
                uint loadAddress = MemoryUtils.ToPhysicalAddress(loadseg, 0);

                if (isComFile) {
                    // Load COM file
                    _fileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, fileHandle, 0);

                    // Read in chunks of 8K to avoid large buffer allocations
                    uint currentOffset = 0;
                    byte[] readBuffer = new byte[0x2000]; // 8K buffer

                    while (true) {
                        // Read directly from file to memory
                        readResult = _fileManager.ReadFile(fileHandle, (ushort)readBuffer.Length, tempBufferAddress);

                        if (readResult.IsError || !readResult.Value.HasValue || readResult.Value.Value == 0) {
                            break;
                        }

                        // Copy from temp buffer to the actual load address
                        ushort bytesRead = (ushort)readResult.Value.Value;

                        // Read bytes from temp buffer
                        for (int i = 0; i < bytesRead; i++) {
                            readBuffer[i] = _memory.UInt8[tempBufferAddress + (uint)i];
                        }

                        // Write to final destination
                        _memory.LoadData(loadAddress + currentOffset, readBuffer, bytesRead);
                        currentOffset += bytesRead;

                        if (bytesRead < readBuffer.Length) {
                            break; // End of file or error
                        }
                    }
                } else {
                    // Load EXE file
                    _fileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, fileHandle, headerSize);

                    // Read the program image in chunks
                    uint currentOffset = 0;
                    byte[] readBuffer = new byte[0x2000]; // 8K buffer

                    while (currentOffset < imageSize) {
                        uint chunkSize = Math.Min(0x2000, imageSize - currentOffset);

                        // Read directly from file to memory
                        readResult = _fileManager.ReadFile(fileHandle, (ushort)chunkSize, tempBufferAddress);

                        if (readResult.IsError || !readResult.Value.HasValue || readResult.Value.Value == 0) {
                            break;
                        }

                        // Copy from temp buffer to the actual load address
                        ushort bytesRead = (ushort)readResult.Value.Value;

                        // Read bytes from temp buffer
                        for (int i = 0; i < bytesRead; i++) {
                            readBuffer[i] = _memory.UInt8[tempBufferAddress + (uint)i];
                        }

                        // Write to final destination
                        _memory.LoadData(loadAddress + currentOffset, readBuffer, bytesRead);
                        currentOffset += bytesRead;

                        if (bytesRead < chunkSize) {
                            break; // End of file or error
                        }
                    }

                    // Apply relocations - for overlay mode, use specified relocation value
                    ushort relocate = (operation == DosExecOperation.LoadOverlay)
                        ? execParameterBlock.RelocationSegment
                        : loadseg;

                    // Seek to relocation table
                    _fileManager.MoveFilePointerUsingHandle(SeekOrigin.Begin, fileHandle, exeFile!.RelocTableOffset);

                    // Apply each relocation
                    for (int i = 0; i < exeFile.RelocItems; i++) {
                        // Read relocation entry
                        readResult = _fileManager.ReadFile(fileHandle, 4, tempBufferAddress);

                        if (!readResult.IsError && readResult.Value.HasValue && readResult.Value.Value == 4) {
                            ushort segment = _memory.UInt16[tempBufferAddress];
                            ushort offset = _memory.UInt16[tempBufferAddress + 2];

                            uint addressToEdit = MemoryUtils.ToPhysicalAddress(
                                (ushort)(segment + loadseg), offset);

                            _memory.UInt16[addressToEdit] += relocate;
                        }
                    }
                }

                // For overlay mode, just set AX and DX to 0 and return
                if (operation == DosExecOperation.LoadOverlay) {
                    _state.AX = 0;
                    _state.DX = 0;
                    errorCode = null;
                    return true;
                }

                // Setup entry point addresses
                uint csip, sssp;

                if (isComFile) {
                    // COM file entry point is at offset 0x100
                    csip = MemoryUtils.ToPhysicalAddress(pspseg, 0x100);

                    // COM files get nearly the full segment for stack
                    if (memsize < 0x1000) {
                        // Small memory block
                        sssp = MemoryUtils.ToPhysicalAddress(pspseg, (ushort)((memsize << 4) - 2));
                    } else {
                        // Normal COM file
                        sssp = MemoryUtils.ToPhysicalAddress(pspseg, 0xFFFE);
                    }

                    // Initialize stack with 0
                    _memory.UInt16[sssp] = 0;
                } else {
                    // EXE file entry point from header
                    csip = MemoryUtils.ToPhysicalAddress(
                        (ushort)(loadseg + exeFile!.InitCS),
                        exeFile.InitIP);

                    sssp = MemoryUtils.ToPhysicalAddress(
                        (ushort)(loadseg + exeFile.InitSS),
                        exeFile.InitSP);
                }

                // For LOAD operation, setup the parameter block with entry points
                if (operation == DosExecOperation.LoadOnly) {
                    // First word on stack should be BX value for startup
                    _memory.UInt16[sssp - 2] = _state.BX;

                    // Return values required by DOS - in the AX and BX registers
                    _state.BX = memsize;
                    _state.DX = 0;

                    errorCode = null;
                    return true;


                }

                errorCode = null;
                return true;
            } finally {
                _fileManager.CloseFile(fileHandle);
            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "Error executing file {FilePath}", dosFilePath);
            errorCode = ErrorCode.DataInvalid;
            return false;
        }
    }

    // Helper methods to match DOSBox implementation
    private bool AllocateMemory(ref ushort segment, ushort size) {
        DosMemoryControlBlock? mcb = _memoryManager.AllocateMemoryBlock(size);
        if (mcb == null) {
            return false;
        }
        segment = mcb.UsableSpaceSegment;
        return true;
    }

    private DosProgramSegmentPrefix SetupPsp(ushort pspseg, ushort memsize, ushort envseg) {
        // Fix MCB for PSP and environment
        DosMemoryControlBlock mcb = _memoryManager.GetDosMemoryControlBlockFromSegment((ushort)(pspseg - 1));
        mcb.PspSegment = pspseg;

        // For environment MCB if it exists
        if (mcb.IsValid) {
            DosMemoryControlBlock envMcb = _memoryManager.GetDosMemoryControlBlockFromSegment((ushort)(envseg - 1));
            envMcb.PspSegment = pspseg;
        }

        // Setup PSP
        var psp = new DosProgramSegmentPrefix(_memory, MemoryUtils.ToPhysicalAddress(pspseg, 0));

        // Initialize PSP
        psp.Exit[0] = 0xCD; // INT instruction
        psp.Exit[1] = 0x20; // INT 20h (program terminate)
        psp.Service[0] = 0xCD; // INT instruction
        psp.Service[1] = 0x21; // INT 21h (DOS function)
        psp.Service[2] = 0xCB; // RETF

        // Set up PSP fields
        psp.ParentProgramSegmentPrefix = _pspSegment;
        psp.EnvironmentTableSegment = envseg;
        psp.NextSegment = (ushort)(pspseg + memsize);

        // Save vectors
        psp.TerminateAddress = GetInterruptVector(0x22);
        psp.BreakAddress = GetInterruptVector(0x23);
        psp.CriticalErrorAddress = GetInterruptVector(0x24);

        return psp;
    }

    private void CopyFileHandles(DosProgramSegmentPrefix childPsp) {
        // Get the parent PSP
        DosProgramSegmentPrefix parentPsp = CurrentPsp;

        // Copy file handles from parent to child, respecting inheritance flags
        // In DOS, there are typically 20 file handles in a standard PSP
        for (ushort i = 0; i < 20; i++) {
            // Get file handle from parent PSP
            byte handle = parentPsp.Files[i];

            // Skip inherited handles that shouldn't be inherited (marked with DOS_NOT_INHERIT flag)
            // or handles that are invalid (0xFF)
            if (handle != 0xFF) {
                // TODO: In a more complete implementation, check if the file has DOS_NOT_INHERIT flag

                // Copy the handle to the child PSP
                childPsp.Files[i] = handle;
            } else {
                // Set invalid handle
                childPsp.Files[i] = 0xFF;
            }
        }
    }

    private void SetupCommandLine(DosProgramSegmentPrefix psp, DosExecParameterBlock block) {
        if (block.CommandTailSegment != 0) {
            // Get the command tail from the parameter block
            DosCommandTail commandTail = block.CommandTail;

            // Copy the command tail to the PSP
            psp.DosCommandTail.Length = commandTail.Length;
            psp.DosCommandTail.Command = commandTail.Command;
        } else {
            // If no command tail is provided, create an empty one
            psp.DosCommandTail.Length = 0;
            psp.DosCommandTail.Command = string.Empty;
        }
    }

    private uint GetInterruptVector(byte interruptNumber) {
        uint ivtAddress = (uint)(interruptNumber * 4);
        return _memory.UInt32[ivtAddress];
    }
}