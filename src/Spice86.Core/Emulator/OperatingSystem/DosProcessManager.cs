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
/// </summary>
public class DosProcessManager : DosFileLoader {
    private const byte FarCallOpcode = 0x9A;
    private const byte IntOpcode = 0xCD;
    private const byte Int21Number = 0x21;
    private const byte RetfOpcode = 0xCB;
    private const ushort FakeCpmSegment = 0xDEAD;
    private const ushort FakeCpmOffset = 0xFFFF;
    private const uint NoPreviousPsp = 0xFFFFFFFF;
    private const byte DefaultDosVersionMajor = 5;
    private const byte DefaultDosVersionMinor = 0;
    private const ushort FileTableOffset = 0x18;
    private const byte DefaultMaxOpenFiles = 20;
    private const byte UnusedFileHandle = 0xFF;
    private const int FcbSize = 16;

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

        foreach (KeyValuePair<string, string> envVar in envVars) {
            _environmentVariables.Add(envVar.Key, envVar.Value);
        }
    }

    public override byte[] LoadFile(string file, string? arguments) {
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

        // Initialize file handle table
        // Standard DOS file handles: 0=STDIN, 1=STDOUT, 2=STDERR, 3=STDAUX, 4=STDPRN
        // These should point to the corresponding SFT entries
        for (int i = 0; i < DefaultMaxOpenFiles; i++) {
            if (i < 5 && i < _fileManager.OpenFiles.Length && _fileManager.OpenFiles[i] != null) {
                // Map standard handles to their SFT entries
                psp.Files[i] = (byte)i;
            } else {
                // Mark unused handles
                psp.Files[i] = UnusedFileHandle;
            }
        }

        // Load the command-line arguments into the PSP's command tail.
        psp.DosCommandTail.Command = DosCommandTail.PrepareCommandlineString(arguments);

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

        return LoadExeOrComFile(file, pspSegment);
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
        ushort pspSegment = _pspTracker.GetCurrentPspSegment();
        uint physicalLoadAddress = MemoryUtils.ToPhysicalAddress(pspSegment, DosProgramSegmentPrefix.PspSize);
        _memory.LoadData(physicalLoadAddress, com);

        // Make SS, DS and ES point to the PSP
        _state.DS = pspSegment;
        _state.ES = pspSegment;
        _state.SS = pspSegment;
        _state.SP = 0xFFFE; // Standard COM file stack
        SetEntryPoint(pspSegment, DosProgramSegmentPrefix.PspSize);
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
        LoadExeFileInMemoryAndApplyRelocations(exeFile, loadImageSegment);
        SetupCpuForExe(exeFile, loadImageSegment, pspSegment);
    }

    private byte[] LoadExeOrComFile(string file, ushort pspSegment) {
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

        return fileBytes;
    }

    /// <summary>
    /// Loads the program image and applies any necessary relocations to it.
    /// </summary>
    /// <param name="exeFile">The EXE file to load.</param>
    /// <param name="loadImageSegment">The load segment for the program.</param>
    private void LoadExeFileInMemoryAndApplyRelocations(DosExeFile exeFile, ushort loadImageSegment) {
        uint physicalLoadAddress = MemoryUtils.ToPhysicalAddress(loadImageSegment, 0);
        _memory.LoadData(physicalLoadAddress, exeFile.ProgramImage, (int)exeFile.ProgramSize);
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            // Read value from memory, add the start segment offset and write back
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset)
                + physicalLoadAddress;
            _memory.UInt16[addressToEdit] += loadImageSegment;
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

    /// <summary>
    /// Creates a far pointer (segment:offset) as a 32-bit value.
    /// </summary>
    /// <param name="segment">The segment value.</param>
    /// <param name="offset">The offset value.</param>
    /// <returns>A 32-bit far pointer with segment in high word and offset in low word.</returns>
    public static uint MakeFarPointer(ushort segment, ushort offset) {
        return ((uint)segment << 16) | offset;
    }

    /// <summary>
    /// Initializes common PSP fields (INT 20h and parent PSP).
    /// </summary>
    /// <param name="psp">The PSP to initialize.</param>
    /// <param name="parentPspSegment">The parent PSP segment.</param>
    private static void InitializeCommonPspFields(DosProgramSegmentPrefix psp, ushort parentPspSegment) {
        // Set the PSP's first 2 bytes to INT 20h (CP/M-style exit)
        psp.Exit[0] = IntOpcode;
        psp.Exit[1] = 0x20;
        
        // Set parent PSP segment
        psp.ParentProgramSegmentPrefix = parentPspSegment;
    }

    /// <summary>
    /// Saves the current interrupt vectors (22h, 23h, 24h) into the PSP.
    /// </summary>
    /// <param name="psp">The PSP to update.</param>
    /// <param name="ivt">The interrupt vector table.</param>
    private static void SaveInterruptVectors(DosProgramSegmentPrefix psp, InterruptVectorTable ivt) {
        // INT 22h - Terminate address
        SegmentedAddress int22 = ivt[0x22];
        psp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);
        
        // INT 23h - Break address  
        SegmentedAddress int23 = ivt[0x23];
        psp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);
        
        // INT 24h - Critical error address
        SegmentedAddress int24 = ivt[0x24];
        psp.CriticalErrorAddress = MakeFarPointer(int24.Segment, int24.Offset);
    }

    /// <summary>
    /// Copies file handle table from parent PSP to child PSP, respecting the no-inherit flag.
    /// Files opened with the no-inherit flag (bit 7 set) are not copied to the child.
    /// </summary>
    /// <remarks>
    /// Based on DOSBox DOS_PSP::CopyFileTable() behavior when createchildpsp is true.
    /// Files marked with <see cref="FileAccessMode.Private"/> in their Flags property will not be
    /// inherited by the child process - they get 0xFF (unused) instead.
    /// </remarks>
    /// <param name="childPsp">The child PSP to populate.</param>
    /// <param name="parentPsp">The parent PSP to copy from.</param>
    private void CopyFileTableFromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
        for (int i = 0; i < DefaultMaxOpenFiles; i++) {
            byte parentHandle = parentPsp.Files[i];
            
            // If handle is unused, keep it unused in child
            if (parentHandle == UnusedFileHandle) {
                childPsp.Files[i] = UnusedFileHandle;
                continue;
            }
            
            // Check if the file was opened with the no-inherit flag (FileAccessMode.Private)
            if (parentHandle < _fileManager.OpenFiles.Length) {
                VirtualFileBase? file = _fileManager.OpenFiles[parentHandle];
                if (file is DosFile dosFile && (dosFile.Flags & (byte)FileAccessMode.Private) != 0) {
                    // File has no-inherit flag set, don't copy to child
                    childPsp.Files[i] = UnusedFileHandle;
                    continue;
                }
            }
            
            // File can be inherited, copy the handle
            childPsp.Files[i] = parentHandle;
        }
    }

    /// <summary>
    /// Copies the command tail from parent PSP (offset 0x80) to child PSP.
    /// </summary>
    /// <param name="childPsp">The child PSP to populate.</param>
    /// <param name="parentPsp">The parent PSP to copy from.</param>
    private static void CopyCommandTailFromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
        childPsp.DosCommandTail.Command = parentPsp.DosCommandTail.Command;
    }

    /// <summary>
    /// Copies FCB1 from parent PSP (offset 0x5C) to child PSP.
    /// </summary>
    /// <param name="childPsp">The child PSP to populate.</param>
    /// <param name="parentPsp">The parent PSP to copy from.</param>
    private static void CopyFcb1FromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
        for (int i = 0; i < FcbSize; i++) {
            childPsp.FirstFileControlBlock[i] = parentPsp.FirstFileControlBlock[i];
        }
    }

    /// <summary>
    /// Copies FCB2 from parent PSP (offset 0x6C) to child PSP.
    /// </summary>
    /// <param name="childPsp">The child PSP to populate.</param>
    /// <param name="parentPsp">The parent PSP to copy from.</param>
    private static void CopyFcb2FromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
        for (int i = 0; i < FcbSize; i++) {
            childPsp.SecondFileControlBlock[i] = parentPsp.SecondFileControlBlock[i];
        }
    }

    /// <summary>
    /// Initializes a child PSP with basic DOS structures.
    /// Based on DOSBox DOS_PSP::MakeNew() implementation.
    /// </summary>
    /// <param name="psp">The PSP to initialize.</param>
    /// <param name="pspSegment">The segment address of the PSP.</param>
    /// <param name="parentPspSegment">The parent PSP segment.</param>
    /// <param name="sizeInParagraphs">The size in paragraphs (16-byte units).</param>
    /// <param name="interruptVectorTable">The interrupt vector table.</param>
    private void InitializeChildPsp(DosProgramSegmentPrefix psp, ushort pspSegment, 
        ushort parentPspSegment, ushort sizeInParagraphs, InterruptVectorTable interruptVectorTable) {
        // Clear the PSP area first (256 bytes)
        for (int i = 0; i < DosProgramSegmentPrefix.MaxLength; i++) {
            _memory.UInt8[psp.BaseAddress + (uint)i] = 0;
        }
        
        // Initialize common PSP fields (INT 20h and parent PSP)
        InitializeCommonPspFields(psp, parentPspSegment);
        
        // Set size (next_seg = psp_segment + size)
        psp.NextSegment = (ushort)(pspSegment + sizeInParagraphs);
        
        // CALL FAR opcode (for far call to DOS INT 21h dispatcher at PSP offset 0x05)
        psp.FarCall = FarCallOpcode;
        
        // CPM entry point - faked address
        psp.CpmServiceRequestAddress = MakeFarPointer(FakeCpmSegment, FakeCpmOffset);
        
        // INT 21h / RETF at offset 0x50
        psp.Service[0] = IntOpcode;
        psp.Service[1] = Int21Number;
        psp.Service[2] = RetfOpcode;
        
        // Previous PSP set to indicate no previous PSP
        psp.PreviousPspAddress = NoPreviousPsp;
        
        // Set DOS version
        psp.DosVersionMajor = DefaultDosVersionMajor;
        psp.DosVersionMinor = DefaultDosVersionMinor;
        
        // Save current interrupt vectors 22h, 23h, 24h into the PSP
        SaveInterruptVectors(psp, interruptVectorTable);
        
        // Initialize file table pointer to point to internal file table
        psp.FileTableAddress = MakeFarPointer(pspSegment, FileTableOffset);
        psp.MaximumOpenFiles = DefaultMaxOpenFiles;
        
        // Initialize file handles to unused
        for (int i = 0; i < DefaultMaxOpenFiles; i++) {
            psp.Files[i] = UnusedFileHandle;
        }
    }

    /// <summary>
    /// Creates a new PSP by copying the current PSP to a specified segment.
    /// Implements INT 21h, AH=26h - Create New PSP.
    /// </summary>
    /// <param name="newPspSegment">The segment address where the new PSP will be created.</param>
    /// <param name="interruptVectorTable">The interrupt vector table for updating PSP vectors.</param>
    public void CreateNewPsp(ushort newPspSegment, InterruptVectorTable interruptVectorTable) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "CreateNewPsp: Copying current PSP to segment {NewPspSegment:X4}",
                newPspSegment);
        }

        // Get the current PSP segment
        ushort currentPspSegment = _pspTracker.GetCurrentPspSegment();
        
        // Get addresses for source and destination PSPs
        uint currentPspAddress = MemoryUtils.ToPhysicalAddress(currentPspSegment, 0);
        uint newPspAddress = MemoryUtils.ToPhysicalAddress(newPspSegment, 0);
        
        // Copy the entire PSP (256 bytes) from current PSP to new PSP
        // Use ReadRam/LoadData for efficient bulk copy
        byte[] pspData = _memory.ReadRam(DosProgramSegmentPrefix.MaxLength, currentPspAddress);
        _memory.LoadData(newPspAddress, pspData);
        
        // Create a PSP wrapper for the new PSP to update fields
        DosProgramSegmentPrefix newPsp = new(_memory, newPspAddress);
        
        // Update interrupt vectors in the new PSP from the interrupt vector table
        // INT 22h - Terminate address
        SegmentedAddress int22 = interruptVectorTable[0x22];
        newPsp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);
        
        // INT 23h - Break address (Ctrl-C handler)
        SegmentedAddress int23 = interruptVectorTable[0x23];
        newPsp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);
        
        // INT 24h - Critical error address
        SegmentedAddress int24 = interruptVectorTable[0x24];
        newPsp.CriticalErrorAddress = MakeFarPointer(int24.Segment, int24.Offset);
        
        // Set DOS version to return on INT 21h AH=30h
        // Use the default DOS version (5.0)
        newPsp.DosVersionMajor = DefaultDosVersionMajor;
        newPsp.DosVersionMinor = DefaultDosVersionMinor;
        
        // Note: We do NOT zero out the parent PSP segment (ps_parent) because
        // this breaks some programs. FreeDOS leaves it as-is (from the copy).
        // This is contrary to what RBIL documents.
        
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug(
                "CreateNewPsp: Created PSP at {NewPspSegment:X4} from {CurrentPspSegment:X4}, Parent={Parent:X4}",
                newPspSegment, currentPspSegment, newPsp.ParentProgramSegmentPrefix);
        }
    }

    /// <summary>
    /// Creates a child PSP (Program Segment Prefix) at the specified segment.
    /// Implements INT 21h, AH=55h - Create Child PSP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Based on DOSBox staging implementation and DOS 4.0 EXEC.ASM behavior:
    /// <list type="bullet">
    /// <item>Creates a new PSP at the specified segment</item>
    /// <item>Sets the parent PSP to the current PSP</item>
    /// <item>Copies the file handle table from the parent</item>
    /// <item>Copies command tail from parent PSP (offset 0x80)</item>
    /// <item>Copies FCB1 from parent (offset 0x5C)</item>
    /// <item>Copies FCB2 from parent (offset 0x6C)</item>
    /// <item>Inherits environment from parent</item>
    /// <item>Inherits stack pointer from parent</item>
    /// <item>Sets the PSP size</item>
    /// </list>
    /// </para>
    /// <para>
    /// This function is used by programs like debuggers or overlay managers
    /// that need to create a child process context without actually loading a program.
    /// </para>
    /// </remarks>
    /// <param name="childSegment">The segment address where the child PSP will be created.</param>
    /// <param name="sizeInParagraphs">The size of the memory block in paragraphs (16-byte units).</param>
    /// <param name="interruptVectorTable">The interrupt vector table for saving current vectors.</param>
    public void CreateChildPsp(ushort childSegment, ushort sizeInParagraphs, InterruptVectorTable interruptVectorTable) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "CreateChildPsp: Creating child PSP at segment {ChildSegment:X4}, size {Size} paragraphs",
                childSegment, sizeInParagraphs);
        }

        // Get the parent PSP segment (current PSP)
        ushort parentPspSegment = _pspTracker.GetCurrentPspSegment();
        
        // Create the new PSP at the specified segment
        uint childPspAddress = MemoryUtils.ToPhysicalAddress(childSegment, 0);
        DosProgramSegmentPrefix childPsp = new(_memory, childPspAddress);
        
        // Initialize the child PSP with MakeNew-style initialization
        InitializeChildPsp(childPsp, childSegment, parentPspSegment, sizeInParagraphs, interruptVectorTable);
        
        // Get the parent PSP to copy data from
        uint parentPspAddress = MemoryUtils.ToPhysicalAddress(parentPspSegment, 0);
        DosProgramSegmentPrefix parentPsp = new(_memory, parentPspAddress);
        
        // Copy file handle table from parent
        CopyFileTableFromParent(childPsp, parentPsp);
        
        // Copy command tail from parent (offset 0x80)
        CopyCommandTailFromParent(childPsp, parentPsp);
        
        // Copy FCB1 from parent (offset 0x5C)
        CopyFcb1FromParent(childPsp, parentPsp);
        
        // Copy FCB2 from parent (offset 0x6C)
        CopyFcb2FromParent(childPsp, parentPsp);
        
        // Inherit environment from parent
        childPsp.EnvironmentTableSegment = parentPsp.EnvironmentTableSegment;
        
        // Inherit stack pointer from parent
        childPsp.StackPointer = parentPsp.StackPointer;
        
        // Note: We intentionally do NOT register this child PSP with _pspTracker.PushPspSegment().
        // INT 21h/55h is used by debuggers and overlay managers that manage their own PSP tracking.
        // The INT 21h handler (DosInt21Handler.CreateChildPsp) will call SetCurrentPspSegment() to 
        // update the SDA's current PSP, but the PSP is not added to the tracker's internal list. 
        // This matches DOSBox behavior where DOS_ChildPSP() creates the PSP but the caller is 
        // responsible for managing PSP tracking.
        
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug(
                "CreateChildPsp: Parent={Parent:X4}, Env={Env:X4}, NextSeg={Next:X4}",
                parentPspSegment, childPsp.EnvironmentTableSegment, childPsp.NextSegment);
        }
    }
}