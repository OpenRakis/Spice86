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

using System.IO;
using System.Text;

/// <summary>
/// Setups the loading and execution of DOS programs and maintains the DOS PSP chains in memory.
/// </summary>
public class DosProcessManager {
    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryManager _memoryManager;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly ILoggerService _loggerService;
    private const ushort CommandComSegment = 0x0160;
    private readonly InterruptVectorTable _interruptVectorTable;

    /// <summary>
    /// The master environment block that all DOS PSPs inherit.
    /// </summary>
    /// <remarks>
    /// Not stored in emulated memory, so no one can modify it.
    /// </remarks>
    private readonly EnvironmentVariables _environmentVariables;

    /// <summary>
    /// The last child process exit code. Used by INT 21h AH=4Dh.
    /// </summary>
    private ushort _lastChildExitCode = 0;

    public DosProcessManager(IMemory memory, State state,
        DosProgramSegmentPrefixTracker dosPspTracker, DosMemoryManager dosMemoryManager,
        DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        IDictionary<string, string> envVars, ILoggerService loggerService) {
        _memory = memory;
        _pspTracker = dosPspTracker;
        _memoryManager = dosMemoryManager;
        _fileManager = dosFileManager;
        _driveManager = dosDriveManager;
        _state = state;
        _loggerService = loggerService;
        _environmentVariables = new();
        _interruptVectorTable = new(_memory);

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
    /// In MS-DOS, this value is only valid for one read after EXEC returns - subsequent
    /// reads return 0.
    /// </remarks>
    public ushort LastChildReturnCode { get; private set; }


    public bool TerminateProcess(byte exitCode, DosTerminationType terminationType,
         InterruptVectorTable interruptVectorTable) {

        // Store the return code for parent to retrieve via INT 21h AH=4Dh
        // Format: AH = termination type, AL = exit code
        LastChildReturnCode = (ushort)(((ushort)terminationType << 8) | exitCode);
        _lastChildExitCode = LastChildReturnCode;

        DosProgramSegmentPrefix? currentPsp = _pspTracker.GetCurrentPsp();
        if (currentPsp is null) {
            // No PSP means we're terminating before any program was loaded
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("TerminateProcess called with no current PSP");
            }
            return false;
        }

        ushort currentPspSegment = _pspTracker.GetCurrentPspSegment();
        ushort parentPspSegment = currentPsp.ParentProgramSegmentPrefix;

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "Terminating process at PSP {CurrentPsp:X4}, exit code {ExitCode:X2}, type {Type}, parent PSP {ParentPsp:X4}",
                currentPspSegment, exitCode, terminationType, parentPspSegment);
        }

        // Check if this is the root process (current PSP = parent PSP, which means this IS the shell itself terminating)
        // In that case, there's no parent to return to
        bool isRootProcess = currentPspSegment == parentPspSegment;

        // If this is a child process (not the main program), we have a parent to return to
        bool hasParentToReturnTo = !isRootProcess && _pspTracker.PspCount > 1;

        // Close all non-standard file handles (5+) opened by this process
        // Standard handles 0-4 (stdin, stdout, stderr, stdaux, stdprn) are inherited and not closed
        _fileManager.CloseAllNonStandardFileHandles();

        // Cache interrupt vectors from child PSP before freeing memory
        // INT 22h = Terminate address, INT 23h = Ctrl-C, INT 24h = Critical error
        // Must read these BEFORE freeing the PSP memory to avoid accessing freed memory
        uint terminateAddr = currentPsp.TerminateAddress;
        uint breakAddr = currentPsp.BreakAddress;
        uint criticalErrorAddr = currentPsp.CriticalErrorAddress;

        // Free all memory blocks owned by this process (including environment block)
        // This follows FreeDOS kernel FreeProcessMem() pattern
        // TSR (term_type == 3) does NOT free memory - it keeps the program resident
        if (terminationType != DosTerminationType.TSR) {
            _memoryManager.FreeProcessMemory(currentPspSegment);
        }

        // Restore interrupt vectors from cached values
        RestoreInterruptVector(0x22, terminateAddr, interruptVectorTable);
        RestoreInterruptVector(0x23, breakAddr, interruptVectorTable);
        RestoreInterruptVector(0x24, criticalErrorAddr, interruptVectorTable);

        // Remove the PSP from the tracker
        _pspTracker.PopCurrentPspSegment();

        if (hasParentToReturnTo) {
            // Get the terminate address from the interrupt vector table
            // The INT 22h vector was just restored from the PSP above, so it now
            // contains the return address for the parent process
            SegmentedAddress returnAddress = interruptVectorTable[0x22];

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information(
                    "Returning to parent at {Segment:X4}:{Offset:X4} from child PSP {ChildPsp:X4}, parent PSP {ParentPsp:X4}",
                    returnAddress.Segment, returnAddress.Offset, currentPspSegment, parentPspSegment);
            }

            // Set up CPU to continue at the return address stored in PSP offset 0x0A.
            //
            // Reference: FreeDOS kernel/task.c return_user() (line ~420):
            // https://github.com/FDOS/kernel/blob/master/kernel/task.c
            //   irp->CS = FP_SEG(p->ps_isv22);
            //   irp->IP = FP_OFF(p->ps_isv22);
            // FreeDOS reads the terminate address from ps_isv22 (PSP offset 0x0A) and
            // sets CS:IP to return to the parent process.
            //
            // Reference: MS-DOS 4.0 EXEC.ASM exec_set_return: (line ~650):
            // https://github.com/microsoft/MS-DOS/blob/main/v4.0/src/DOS/EXEC.ASM
            //   POP DS:[addr_int_terminate]
            //   POP DS:[addr_int_terminate+2]
            // MS-DOS pops the return address from stack into INT 22h vector.
            //
            // The CfgCpu callback node (Grp4Callback) detects when the callback handler
            // changes CS:IP and will NOT add the instruction length in that case.
            _state.CS = returnAddress.Segment;
            _state.IP = returnAddress.Offset;

            return true; // Continue execution at parent
        }

        // No parent to return to - this is the main program terminating
        return false;
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
    public ushort GetLastChildExitCode() {
        return _lastChildExitCode;
    }

    public DosExecResult LoadOrLoadAndExecute(string programName, DosExecParameterBlock paramBlock,
        string commandTail, DosExecLoadType loadType, ushort environmentSegment, InterruptVectorTable interruptVectorTable) {
        // Read the return address from the interrupt stack. When INT21h AH=4Bh is called,
        // the CPU pushes FLAGS, CS, IP onto the stack. The stack pointer currently points
        // to the pushed IP (the return address).
        // Stack layout after INT instruction:
        //   [SP+0] = IP (return offset)
        //   [SP+2] = CS (return segment)  
        //   [SP+4] = FLAGS
        // This matches FreeDOS where user_r contains the saved register state with CS:IP
        // being the address to return to after the child terminates.
        ushort callerIP = _memory.UInt16[_state.StackPhysicalAddress];
        ushort callerCS = _memory.UInt16[_state.StackPhysicalAddress + 2];
        
        string? hostPath = _fileManager.TryGetFullHostPathFromDos(programName) ?? programName;
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath)) {
            return DosExecResult.Fail(DosErrorCode.FileNotFound);
        }

        byte[] fileBytes = ReadFileBytes(hostPath);

        string upperCaseExtension = Path.GetExtension(hostPath).ToUpperInvariant();
        bool isExeCandidate = fileBytes.Length >= DosExeFile.MinExeSize && upperCaseExtension == ".EXE";
        ushort parentPspSegment = _pspTracker.GetCurrentPspSegment();
        bool updateCpuState = loadType == DosExecLoadType.LoadAndExecute;

        // Save parent's current SS:SP BEFORE any CPU state changes (FreeDOS: q->ps_stack = user_r)
        // This captures the parent's stack context before the child modifies anything
        uint parentStackPointer = ((uint)_state.SS << 16) | _state.SP;

        // Save CPU state for LoadOnly operations so we can restore it after loading
        ushort savedCS = 0, savedIP = 0, savedSS = 0, savedSP = 0, savedDS = 0, savedES = 0;
        if (!updateCpuState) {
            savedCS = _state.CS;
            savedIP = _state.IP;
            savedSS = _state.SS;
            savedSP = _state.SP;
            savedDS = _state.DS;
            savedES = _state.ES;
        }

        // Try to load as EXE first if it looks like an EXE file
        if (isExeCandidate) {
            DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            if (exeFile.IsValid) {
                DosMemoryControlBlock? block = _memoryManager.ReserveSpaceForExe(exeFile);
                if (block is null) {
                    return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
                }

                InitializePsp(block.DataBlockSegment, hostPath, commandTail, environmentSegment, interruptVectorTable, parentPspSegment, parentStackPointer, callerCS, callerIP);

                LoadExeFile(exeFile, block.DataBlockSegment, block, updateCpuState);

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

                if (!updateCpuState) {
                    _pspTracker.SetCurrentPspSegment(parentPspSegment);
                    // Restore caller's CPU state for LoadOnly
                    _state.CS = savedCS;
                    _state.IP = savedIP;
                    _state.SS = savedSS;
                    _state.SP = savedSP;
                    _state.DS = savedDS;
                    _state.ES = savedES;
                }

                // For AL=01 (Load Only), DOS fills the EPB with initial CS:IP and SS:SP.
                if (loadType == DosExecLoadType.LoadOnly && result.Success) {
                    paramBlock.InitialCS = result.InitialCS;
                    paramBlock.InitialIP = result.InitialIP;
                    paramBlock.InitialSS = result.InitialSS;
                    paramBlock.InitialSP = result.InitialSP;
                }

                return result;
            }
            // If file has .EXE extension but isn't a valid EXE, fall through to try COM loading
            // This matches FreeDOS behavior where DosExec falls back to DosComLoader if DosExeLoader fails
        }

        // Load as COM file (either explicitly .COM or invalid .EXE that we'll try as COM)
        ushort paragraphsNeeded = (ushort)((DosProgramSegmentPrefix.PspSize + fileBytes.Length + 15) / 16);
        paragraphsNeeded = (ushort)(paragraphsNeeded == 0 ? 1 : paragraphsNeeded);
        DosMemoryControlBlock? comBlock = _memoryManager.AllocateMemoryBlock(paragraphsNeeded);
        if (comBlock is null) {
            return DosExecResult.Fail(DosErrorCode.InsufficientMemory);
        }

        InitializePsp(comBlock.DataBlockSegment, hostPath, commandTail, environmentSegment, interruptVectorTable, parentPspSegment, parentStackPointer, callerCS, callerIP);

        LoadComFile(fileBytes, comBlock.DataBlockSegment, updateCpuState);

        DosExecResult comResult = loadType == DosExecLoadType.LoadOnly
            ? DosExecResult.SuccessLoadOnly(comBlock.DataBlockSegment, DosProgramSegmentPrefix.PspSize,
                comBlock.DataBlockSegment, 0xFFFE)
            : DosExecResult.SuccessExecute(comBlock.DataBlockSegment, DosProgramSegmentPrefix.PspSize,
                comBlock.DataBlockSegment, 0xFFFE);

        if (!updateCpuState) {
            _pspTracker.SetCurrentPspSegment(parentPspSegment);
            // Restore caller's CPU state for LoadOnly
            _state.CS = savedCS;
            _state.IP = savedIP;
            _state.SS = savedSS;
            _state.SP = savedSP;
            _state.DS = savedDS;
            _state.ES = savedES;
        }

        // For AL=01 (Load Only), DOS fills the EPB with initial CS:IP and SS:SP.
        if (loadType == DosExecLoadType.LoadOnly && comResult.Success) {
            paramBlock.InitialCS = comResult.InitialCS;
            paramBlock.InitialIP = comResult.InitialIP;
            paramBlock.InitialSS = comResult.InitialSS;
            paramBlock.InitialSP = comResult.InitialSP;
        }

        return comResult;
    }

    public DosExecResult LoadOverlay(string programName, ushort loadSegment, ushort relocationFactor) {
        string? hostPath = _fileManager.TryGetFullHostPathFromDos(programName) ?? programName;
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath)) {
            return DosExecResult.Fail(DosErrorCode.FileNotFound);
        }

        byte[] fileBytes = ReadFileBytes(hostPath);

        if (fileBytes.Length < DosExeFile.MinExeSize) {
            return DosExecResult.Fail(DosErrorCode.FormatInvalid);
        }

        DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
        if (!exeFile.IsValid) {
            return DosExecResult.Fail(DosErrorCode.FormatInvalid);
        }

        LoadExeFileInMemoryAndApplyRelocations(exeFile, loadSegment);
        return DosExecResult.SuccessLoadOnly((ushort)(exeFile.InitCS + loadSegment), exeFile.InitIP,
            (ushort)(exeFile.InitSS + loadSegment), exeFile.InitSP);
    }

    private void InitializePsp(ushort pspSegment, string programHostPath, string? arguments, ushort environmentSegment, InterruptVectorTable interruptVectorTable, ushort parentPspSegment, uint parentStackPointer, ushort callerCS, ushort callerIP) {
        // Establish parent-child PSP relationship and create the new PSP
        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(pspSegment);

        psp.Exit[0] = 0xCD;
        psp.Exit[1] = 0x20;
        psp.NextSegment = DosMemoryManager.LastFreeSegment;

        // FreeDOS task.c setvec(0x22, MK_FP(user_r->CS, user_r->IP)) at line 626
        // This sets INT 22h to point to the CALLER'S return address BEFORE child_psp() creates the child PSP.
        // The child PSP then inherits this as its terminate address via new_psp() -> getvec(0x22).
        // When the child terminates, return_user() restores CS:IP from p->ps_isv22 (the child PSP's INT 22h).
        // We must save the caller's CS:IP (read from the interrupt stack) as the child's terminate address
        // so the parent can resume execution at the correct location.
        psp.TerminateAddress = ((uint)callerCS << 16) | callerIP;
        
        // Save current interrupt vectors for Ctrl-C and Critical Error in child PSP
        SegmentedAddress breakVector = interruptVectorTable[0x23];
        SegmentedAddress criticalErrorVector = interruptVectorTable[0x24];
        psp.BreakAddress = ((uint)breakVector.Segment << 16) | breakVector.Offset;
        psp.CriticalErrorAddress = ((uint)criticalErrorVector.Segment << 16) | criticalErrorVector.Offset;
        
        // Save parent's stack pointer in the PARENT PSP's StackPointer field (offset 0x2E)
        // This mirrors FreeDOS task.c load_transfer() line 366: q->ps_stack = (BYTE FAR *)user_r;
        // where q is the parent PSP. When the child terminates, return_user() reads this
        // from the parent PSP (line 614) to restore the parent's execution context.
        DosProgramSegmentPrefix parentPsp = new(_memory, MemoryUtils.ToPhysicalAddress(parentPspSegment, 0));
        parentPsp.StackPointer = parentStackPointer;

        // Link to parent PSP and initialize file table
        psp.ParentProgramSegmentPrefix = parentPspSegment;
        psp.MaximumOpenFiles = DosFileManager.MaxOpenFilesPerProcess;
        // ps_filetab points to JFT at offset 0x18 inside this PSP
        psp.FileTableAddress = ((uint)pspSegment << 16) | 0x18;
        // Always set previous PSP pointer
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

        // Honor caller-provided environment segment (EPB). If non-zero, use it; otherwise create a new block.
        if (environmentSegment != 0) {
            psp.EnvironmentTableSegment = environmentSegment;
        } else {
            byte[] environmentBlock = CreateEnvironmentBlock(programHostPath);
            ushort paragraphsNeeded = (ushort)((environmentBlock.Length + 15) / 16);
            paragraphsNeeded = paragraphsNeeded == 0 ? (ushort)1 : paragraphsNeeded;
            DosMemoryControlBlock? envBlock = _memoryManager.AllocateMemoryBlock(paragraphsNeeded);

            if (envBlock != null) {
                _memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlock.DataBlockSegment, 0), environmentBlock);
                psp.EnvironmentTableSegment = envBlock.DataBlockSegment;
            }
        }

        // Set the disk transfer area address to the command-line offset in the PSP.
        _fileManager.SetDiskTransferAreaAddress(
            pspSegment, DosCommandTail.OffsetInPspSegment);
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

    private void LoadComFile(byte[] com, ushort pspSegment, bool updateCpuState) {
        uint physicalLoadAddress = MemoryUtils.ToPhysicalAddress(pspSegment, DosProgramSegmentPrefix.PspSize);
        _memory.LoadData(physicalLoadAddress, com);

        if (updateCpuState) {
            _state.CS = pspSegment;
            _state.IP = DosProgramSegmentPrefix.PspSize;
            _state.DS = pspSegment;
            _state.ES = pspSegment;
            _state.SS = pspSegment;
            _state.SP = 0xFFFE; // Standard COM file stack
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
        LoadExeFileInMemoryAndApplyRelocations(exeFile, loadImageSegment);
        if (updateCpuState) {
            SetupCpuForExe(exeFile, loadImageSegment, pspSegment);
        }
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

    private void SetEntryPoint(ushort cs, ushort ip) {
        _state.CS = cs;
        _state.IP = ip;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Program entry point is {ProgramEntry}", ConvertUtils.ToSegmentedAddressRepresentation(cs, ip));
        }
    }

}