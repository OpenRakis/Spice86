namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Text;

/// <summary>
/// Setups the loading and execution of DOS programs and maintains the DOS PSP chains in memory.
/// </summary>
public class DosProcessManager : DosFileLoader {
    private const ushort ComOffset = 0x100;
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
    private const ushort CommandTailDataOffset = 0x81;
    private const int MaxCommandTailLength = 127;
    private const int FcbSize = 16;
    private const ushort ComFileMemoryParagraphs = 0xFFF;

    private readonly DosProgramSegmentPrefixTracker _pspTracker;
    private readonly DosMemoryManager _memoryManager;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;
    private readonly CommandCom _commandCom;
    private readonly EnvironmentVariables _environmentVariables;
    private ushort _lastChildReturnCode;

    public ushort LastChildReturnCode {
        get => _lastChildReturnCode;
        set => _lastChildReturnCode = value;
    }

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
        _commandCom = new CommandCom(memory, loggerService);

        string pathValue = $"{_driveManager.CurrentDrive.DosVolume}{DosPathResolver.DirectorySeparatorChar}";
        if (!envVars.ContainsKey("PATH")) {
            envVars.Add("PATH", pathValue);
        }

        foreach (KeyValuePair<string, string> envVar in envVars) {
            _environmentVariables.Add(envVar.Key, envVar.Value);
        }
    }

    public override byte[] LoadFile(string file, string? arguments) {
        DosExecResult result = Exec(file, arguments, DosExecLoadType.LoadAndExecute);
        return ReadFile(ResolveToHostPath(file) ?? file);
    }

    public DosExecResult Exec(string programPath, string? arguments,
        DosExecLoadType loadType = DosExecLoadType.LoadAndExecute,
        ushort environmentSegment = 0, SegmentedAddress? firstFcbPointer = null,
        SegmentedAddress? secondFcbPointer = null) {
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information(
                "EXEC: Loading program '{Program}' with args '{Args}', type={LoadType}",
                programPath, arguments ?? string.Empty, loadType);
        }

        string? hostPath = ResolveToHostPath(programPath);
        if (hostPath is null || !File.Exists(hostPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC: Program file not found: {Program}", programPath);
            }
            return DosExecResult.Failed(DosErrorCode.FileNotFound);
        }

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

        ushort parentPspSegment = _pspTracker.GetCurrentPspSegment();
        if (_pspTracker.PspCount == 0 || parentPspSegment == 0) {
            parentPspSegment = _commandCom.PspSegment;
        }
        uint parentPspAddress = MemoryUtils.ToPhysicalAddress(parentPspSegment, 0);
        if (parentPspAddress >= _memory.Ram.Size) {
            parentPspSegment = _commandCom.PspSegment;
        }
        DosProgramSegmentPrefix parentPsp = new(_memory, MemoryUtils.ToPhysicalAddress(parentPspSegment, 0));

        byte[]? envBlockData = null;
        ushort envSegment = environmentSegment;
        if (envSegment == 0) {
            if (parentPsp.EnvironmentTableSegment != 0) {
                envSegment = parentPsp.EnvironmentTableSegment;
            } else {
                envBlockData = CreateEnvironmentBlock(programPath);
                DosMemoryControlBlock? envBlock = _memoryManager.AllocateMemoryBlock(
                    (ushort)((envBlockData.Length + 15) / 16));
                if (envBlock is null) {
                    return DosExecResult.Failed(DosErrorCode.InsufficientMemory);
                }
                envSegment = envBlock.DataBlockSegment;
                uint envAddress = MemoryUtils.ToPhysicalAddress(envSegment, 0);
                _memory.LoadData(envAddress, envBlockData);
            }
        }

        return LoadProgram(fileBytes, hostPath, arguments, parentPspSegment, envSegment, loadType,
            firstFcbPointer, secondFcbPointer);
    }

    public DosExecResult ExecOverlay(string programPath, ushort loadSegment, ushort relocationFactor) {
        string? hostPath = ResolveToHostPath(programPath);
        if (hostPath is null || !File.Exists(hostPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC OVERLAY: Program file not found: {Program}", programPath);
            }
            return DosExecResult.Failed(DosErrorCode.FileNotFound);
        }

        byte[] fileBytes;
        try {
            fileBytes = ReadFile(hostPath);
        } catch (IOException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC OVERLAY: Failed to read program file: {Error}", ex.Message);
            }
            return DosExecResult.Failed(DosErrorCode.AccessDenied);
        } catch (UnauthorizedAccessException ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("EXEC OVERLAY: Access denied reading program file: {Error}", ex.Message);
            }
            return DosExecResult.Failed(DosErrorCode.AccessDenied);
        }

        bool isExe = fileBytes.Length >= DosExeFile.MinExeSize;
        if (!isExe) {
            return DosExecResult.Failed(DosErrorCode.FormatInvalid);
        }

        DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
        if (!exeFile.IsValid) {
            return DosExecResult.Failed(DosErrorCode.FormatInvalid);
        }

        LoadExeFileInMemoryAndApplyRelocations(exeFile, loadSegment);
        return DosExecResult.Succeeded();
    }

    public bool TerminateProcess(byte exitCode, DosTerminationType terminationType,
        InterruptVectorTable interruptVectorTable) {
        LastChildReturnCode = (ushort)(((ushort)terminationType << 8) | exitCode);

        DosProgramSegmentPrefix? currentPsp = _pspTracker.GetCurrentPsp();
        if (currentPsp is null) {
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

        bool isRootProcess = currentPspSegment == parentPspSegment ||
                             parentPspSegment == CommandCom.CommandComSegment;
        bool hasParentToReturnTo = !isRootProcess && _pspTracker.PspCount > 1;

        _fileManager.CloseAllNonStandardFileHandles();

        uint terminateAddr = currentPsp.TerminateAddress;
        uint breakAddr = currentPsp.BreakAddress;
        uint criticalErrorAddr = currentPsp.CriticalErrorAddress;

        _memoryManager.FreeProcessMemory(currentPspSegment);

        RestoreInterruptVector(0x22, terminateAddr, interruptVectorTable);
        RestoreInterruptVector(0x23, breakAddr, interruptVectorTable);
        RestoreInterruptVector(0x24, criticalErrorAddr, interruptVectorTable);

        _pspTracker.PopCurrentPspSegment();

        if (hasParentToReturnTo) {
            _state.DS = parentPspSegment;
            _state.ES = parentPspSegment;

            SegmentedAddress returnAddress = interruptVectorTable[0x22];
            _state.CS = returnAddress.Segment;
            _state.IP = returnAddress.Offset;
            return true;
        }

        return false;
    }

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

        DosProgramSegmentPrefix newPsp = new(_memory, newPspAddress);

        SegmentedAddress int22 = interruptVectorTable[0x22];
        newPsp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);

        SegmentedAddress int23 = interruptVectorTable[0x23];
        newPsp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);

        SegmentedAddress int24 = interruptVectorTable[0x24];
        newPsp.CriticalErrorAddress = MakeFarPointer(int24.Segment, int24.Offset);

        newPsp.DosVersionMajor = DefaultDosVersionMajor;
        newPsp.DosVersionMinor = DefaultDosVersionMinor;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug(
                "CreateNewPsp: Created PSP at {NewPspSegment:X4} from {CurrentPspSegment:X4}, Parent={Parent:X4}",
                newPspSegment, currentPspSegment, newPsp.ParentProgramSegmentPrefix);
        }
    }

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

    private DosExecResult LoadProgram(byte[] fileBytes, string hostPath, string? arguments,
        ushort parentPspSegment, ushort envSegment, DosExecLoadType loadType, SegmentedAddress? firstFcbPointer,
        SegmentedAddress? secondFcbPointer) {
        bool isExe = false;
        DosExeFile? exeFile = null;

        if (fileBytes.Length >= DosExeFile.MinExeSize) {
            exeFile = new DosExeFile(new ByteArrayReaderWriter(fileBytes));
            isExe = exeFile.IsValid;
        }

        ushort pspSegment;
        DosMemoryControlBlock? memBlock;

        if (isExe && exeFile is not null) {
            memBlock = _memoryManager.ReserveSpaceForExe(exeFile, 0);
        } else {
            memBlock = _memoryManager.AllocateMemoryBlock(ComFileMemoryParagraphs);
        }

        if (memBlock is null) {
            return DosExecResult.Failed(DosErrorCode.InsufficientMemory);
        }
        pspSegment = memBlock.DataBlockSegment;

        if (memBlock is null) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Failed to allocate memory for program at segment {Segment:X4}", pspSegment);
            }
            return DosExecResult.Failed(DosErrorCode.InsufficientMemory);
        }

        DosProgramSegmentPrefix psp = _pspTracker.PushPspSegment(pspSegment);

        ushort nextSegment = memBlock is not null
            ? (ushort)(memBlock.DataBlockSegment + memBlock.Size)
            : DosMemoryManager.LastFreeSegment;
        InitializePsp(psp, parentPspSegment, envSegment, arguments, nextSegment, firstFcbPointer, secondFcbPointer);
        _fileManager.SetDiskTransferAreaAddress(pspSegment, DosCommandTail.OffsetInPspSegment);

        ushort cs;
        ushort ip;
        ushort ss;
        ushort sp;

        if (isExe && exeFile is not null) {
            if (memBlock is null) {
                return DosExecResult.Failed(DosErrorCode.InsufficientMemory);
            }
            LoadExeFileIntoReservedMemory(exeFile, memBlock, out cs, out ip, out ss, out sp);
        } else {
            LoadComFileInternal(fileBytes, out cs, out ip, out ss, out sp);
        }

        if (loadType == DosExecLoadType.LoadAndExecute) {
            _state.DS = pspSegment;
            _state.ES = pspSegment;
            _state.SS = ss;
            _state.SP = sp;
            SetEntryPoint(cs, ip);
            _state.InterruptFlag = true;

            return DosExecResult.Succeeded();
        }
        if (loadType == DosExecLoadType.LoadOnly) {
            return DosExecResult.Succeeded(pspSegment, cs, ip, ss, sp);
        }

        return DosExecResult.Succeeded();
    }

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

    private static void InitializeCommonPspFields(DosProgramSegmentPrefix psp, ushort parentPspSegment) {
        psp.Exit[0] = IntOpcode;
        psp.Exit[1] = 0x20;
        psp.ParentProgramSegmentPrefix = parentPspSegment;
    }

    private void InitializePsp(DosProgramSegmentPrefix psp, ushort parentPspSegment,
        ushort envSegment, string? arguments, ushort nextSegment, SegmentedAddress? firstFcbPointer,
        SegmentedAddress? secondFcbPointer) {
        InitializeCommonPspFields(psp, parentPspSegment);

        psp.NextSegment = nextSegment;
        psp.FarCall = FarCallOpcode;
        psp.CpmServiceRequestAddress = MakeFarPointer(FakeCpmSegment, FakeCpmOffset);
        psp.Service[0] = IntOpcode;
        psp.Service[1] = Int21Number;
        psp.Service[2] = RetfOpcode;
        psp.PreviousPspAddress = NoPreviousPsp;
        psp.EnvironmentTableSegment = envSegment;
        psp.DosVersionMajor = DefaultDosVersionMajor;
        psp.DosVersionMinor = DefaultDosVersionMinor;
        psp.FileTableAddress = MakeFarPointer(_pspTracker.InitialPspSegment, FileTableOffset);
        psp.MaximumOpenFiles = DefaultMaxOpenFiles;

        for (int i = 0; i < DefaultMaxOpenFiles; i++) {
            psp.Files[i] = UnusedFileHandle;
        }

        psp.DosCommandTail.Command = DosCommandTail.PrepareCommandlineString(arguments);

        if (firstFcbPointer.HasValue && firstFcbPointer.Value.Segment != ushort.MaxValue) {
            CopyFcb(firstFcbPointer.Value, psp.FirstFileControlBlock);
        }
        if (secondFcbPointer.HasValue && secondFcbPointer.Value.Segment != ushort.MaxValue) {
            CopyFcb(secondFcbPointer.Value, psp.SecondFileControlBlock);
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

    private void LoadComFileInternal(byte[] com, out ushort cs, out ushort ip, out ushort ss, out ushort sp) {
        ushort pspSegment = _pspTracker.GetCurrentPspSegment();
        uint physicalLoadAddress = MemoryUtils.ToPhysicalAddress(pspSegment, DosProgramSegmentPrefix.PspSize);
        _memory.LoadData(physicalLoadAddress, com);

        _state.DS = pspSegment;
        _state.ES = pspSegment;
        _state.SS = pspSegment;
        _state.SP = 0xFFFE;

        cs = pspSegment;
        ip = ComOffset;
        ss = pspSegment;
        sp = 0xFFFE;
    }

    private void CopyFcb(SegmentedAddress sourceFcbPointer, UInt8Array destinationFcb) {
        uint sourceAddress = MemoryUtils.ToPhysicalAddress(sourceFcbPointer.Segment, sourceFcbPointer.Offset);
        byte[] fcbData = _memory.ReadRam(FcbSize, sourceAddress);
        for (int i = 0; i < FcbSize; i++) {
            destinationFcb[i] = fcbData[i];
        }
    }

    private string? ResolveToHostPath(string dosPath) => _fileManager.TryGetFullHostPathFromDos(dosPath);

    private byte[] CreateEnvironmentBlock(string programPath) {
        using MemoryStream ms = new();

        foreach (KeyValuePair<string, string> envVar in _environmentVariables) {
            string envString = $"{envVar.Key}={envVar.Value}";
            byte[] envBytes = Encoding.ASCII.GetBytes(envString);
            ms.Write(envBytes, 0, envBytes.Length);
            ms.WriteByte(0);
        }

        ms.WriteByte(0);
        ms.WriteByte(1);
        ms.WriteByte(0);

        string normalizedPath = programPath.Replace('/', '\\').ToUpperInvariant();
        byte[] programPathBytes = Encoding.ASCII.GetBytes(normalizedPath);
        ms.Write(programPathBytes, 0, programPathBytes.Length);
        ms.WriteByte(0);

        return ms.ToArray();
    }

    private void LoadExeFileInMemoryAndApplyRelocations(DosExeFile exeFile, ushort startSegment) {
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(startSegment, 0);
        _memory.LoadData(physicalStartAddress, exeFile.ProgramImage, (int)exeFile.ProgramSize);
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset)
                + physicalStartAddress;
            _memory.UInt16[addressToEdit] += startSegment;
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

    public static uint MakeFarPointer(ushort segment, ushort offset) {
        return ((uint)segment << 16) | offset;
    }

    private void SaveInterruptVectors(DosProgramSegmentPrefix psp, InterruptVectorTable ivt) {
        SegmentedAddress int22 = ivt[0x22];
        psp.TerminateAddress = MakeFarPointer(int22.Segment, int22.Offset);

        SegmentedAddress int23 = ivt[0x23];
        psp.BreakAddress = MakeFarPointer(int23.Segment, int23.Offset);

        SegmentedAddress int24 = ivt[0x24];
        psp.CriticalErrorAddress = MakeFarPointer(int24.Segment, int24.Offset);
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

    private void CopyCommandTailFromParent(DosProgramSegmentPrefix childPsp, DosProgramSegmentPrefix parentPsp) {
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
}
