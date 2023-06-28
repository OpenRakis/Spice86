namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Reimplementation of INT21
/// </summary>
public class DosInt21Handler : InterruptHandler {
    private readonly Encoding _cp850CharSet;

    private readonly DosMemoryManager _dosMemoryManager;
    private bool _isCtrlCFlag;

    // dosbox
    private byte _defaultDrive = 2;

    private StringBuilder _displayOutputBuilder = new();
    private readonly DosFileManager _dosFileManager;
    private readonly List<IVirtualDevice> _devices;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="dos">The DOS kernel.</param>
    public DosInt21Handler(Machine machine, ILoggerService loggerService, Dos dos) : base(machine, loggerService) {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _cp850CharSet = Encoding.GetEncoding("ibm850");
        _dosMemoryManager = dos.MemoryManager;
        _dosFileManager = dos.FileManager;
        _devices = dos.Devices;
        FillDispatchTable();
    }
    
    private void FillDispatchTable() {
        _dispatchTable.Add(0x02, new Callback(0x02, DisplayOutput));
        _dispatchTable.Add(0x06, new Callback(0x06, () => DirectConsoleIo(true)));
        _dispatchTable.Add(0x09, new Callback(0x09, PrintString));
        _dispatchTable.Add(0x0C, new Callback(0x0C, ClearKeyboardBufferAndInvokeKeyboardFunction));
        _dispatchTable.Add(0x0D, new Callback(0x0D, DiskReset));
        _dispatchTable.Add(0x0E, new Callback(0x0E, SelectDefaultDrive));
        _dispatchTable.Add(0x1A, new Callback(0x1A, SetDiskTransferAddress));
        _dispatchTable.Add(0x1B, new Callback(0x1B, GetAllocationInfoForDefaultDrive));
        _dispatchTable.Add(0x19, new Callback(0x19, GetCurrentDefaultDrive));
        _dispatchTable.Add(0x25, new Callback(0x25, SetInterruptVector));
        _dispatchTable.Add(0x2A, new Callback(0x2A, GetDate));
        _dispatchTable.Add(0x2C, new Callback(0x2C, GetTime));
        _dispatchTable.Add(0x2F, new Callback(0x2F, GetDiskTransferAddress));
        _dispatchTable.Add(0x30, new Callback(0x30, GetDosVersion));
        _dispatchTable.Add(0x33, new Callback(0x33, GetSetControlBreak));
        _dispatchTable.Add(0x35, new Callback(0x35, GetInterruptVector));
        _dispatchTable.Add(0x36, new Callback(0x36, GetFreeDiskSpace));
        _dispatchTable.Add(0x38, new Callback(0x38, () => SetCountryCode(true)));
        _dispatchTable.Add(0x39, new Callback(0x39, () => CreateDirectory(true)));
        _dispatchTable.Add(0x3B, new Callback(0x3B, () => ChangeCurrentDirectory(true)));
        _dispatchTable.Add(0x3C, new Callback(0x3C, () => CreateFileUsingHandle(true)));
        _dispatchTable.Add(0x3D, new Callback(0x3D, () => OpenFile(true)));
        _dispatchTable.Add(0x3E, new Callback(0x3E, () => CloseFile(true)));
        _dispatchTable.Add(0x3F, new Callback(0x3F, () => ReadFile(true)));
        _dispatchTable.Add(0x40, new Callback(0x40, () => WriteFileUsingHandle(true)));
        _dispatchTable.Add(0x43, new Callback(0x43, () => GetSetFileAttributes(true)));
        _dispatchTable.Add(0x44, new Callback(0x44, () => IoControl(true)));
        _dispatchTable.Add(0x42, new Callback(0x42, () => MoveFilePointerUsingHandle(true)));
        _dispatchTable.Add(0x45, new Callback(0x45, () => DuplicateFileHandle(true)));
        _dispatchTable.Add(0x47, new Callback(0x47, () => GetCurrentDirectory(true)));
        _dispatchTable.Add(0x48, new Callback(0x48, () => AllocateMemoryBlock(true)));
        _dispatchTable.Add(0x49, new Callback(0x49, () => FreeMemoryBlock(true)));
        _dispatchTable.Add(0x4A, new Callback(0x4A, () => ModifyMemoryBlock(true)));
        _dispatchTable.Add(0x4C, new Callback(0x4C, QuitWithExitCode));
        _dispatchTable.Add(0x4E, new Callback(0x4E, () => FindFirstMatchingFile(true)));
        _dispatchTable.Add(0x4F, new Callback(0x4F, () => FindNextMatchingFile(true)));
        _dispatchTable.Add(0x51, new Callback(0x51, GetPspAddress));
        _dispatchTable.Add(0x62, new Callback(0x62, GetPspAddress));
    }

    /// <summary>
    /// Creates a directory.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    private void CreateDirectory(bool calledFromVm) {
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CreateDirectory(GetStringAtDsDx());
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public void GetAllocationInfoForDefaultDrive() {
        // Bytes per sector
        _state.CX = 0x200;
        // Sectors per cluster
        _state.AX = 1;
        // Total clusters
        _state.DX = 0xEA0;
        // Media Id
        _state.DS = 0x8010;
        // From DOSBox source code...
        _state.BX = ConvertUtils.Uint16((ushort) (0x8010 + _defaultDrive * 9)) ;
        _state.AH = 0;
    }

    public void SetCountryCode(bool calledFromVm) {
        switch (_state.AL) {
            case 0: //Get country specific information
                uint dest = MemoryUtils.ToPhysicalAddress(_state.DS, _state.DX);
                _memory.LoadData(dest, BitConverter.GetBytes((ushort)_machine.Dos.DosTables.CountryInfo.Country));
                _state.AX = (ushort) (_state.BX + 1);
                SetCarryFlag(false, calledFromVm);
                break;
            default: //Set country code
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("{MethodName}: subFunction is unsupported", nameof(SetCountryCode));
                }
                _state.AX = 0;
                SetCarryFlag(false, calledFromVm);
                break;
        }
    }
    
    public void AllocateMemoryBlock(bool calledFromVm) {
        ushort requestedSize = _state.BX;
        _loggerService.Verbose("ALLOCATE MEMORY BLOCK {RequestedSize}", requestedSize);
        SetCarryFlag(false, calledFromVm);
        DosMemoryControlBlock? res = _dosMemoryManager.AllocateMemoryBlock(requestedSize);
        if (res == null) {
            LogDosError(calledFromVm);
            // did not find something good, error
            SetCarryFlag(true, calledFromVm);
            DosMemoryControlBlock largest = _dosMemoryManager.FindLargestFree();
            // INSUFFICIENT MEMORY
            _state.AX = 0x08;
            _state.BX = largest.Size;
            return;
        }
        _state.AX = res.UsableSpaceSegment;
    }

    public void ChangeCurrentDirectory(bool calledFromVm) {
        string newDirectory = GetStringAtDsDx();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SET CURRENT DIRECTORY: {NewDirectory}", newDirectory);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.SetCurrentDir(newDirectory);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public void ClearKeyboardBufferAndInvokeKeyboardFunction() {
        byte operation = _state.AL;
        _loggerService.Debug("CLEAR KEYBOARD AND CALL INT 21 {Operation}", operation);
        Run(operation);
    }

    public void CloseFile(bool calledFromVm) {
        ushort fileHandle = _state.BX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("CLOSE FILE handle {FileHandle}", ConvertUtils.ToHex(fileHandle));
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CloseFile(fileHandle);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public void CreateFileUsingHandle(bool calledFromVm) {
        string fileName = GetStringAtDsDx();
        ushort fileAttribute = _state.CX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("CREATE FILE USING HANDLE: {FileName} with attribute {FileAttribute}", fileName, fileAttribute);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CreateFileUsingHandle(fileName, fileAttribute);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public void DirectConsoleIo(bool calledFromVm) {
        byte character = _state.DL;
        if (character == 0xFF) {
            _loggerService.Debug("DIRECT CONSOLE IO, INPUT REQUESTED");
            // Read from STDIN, not implemented, return no character ready
            ushort? scancode = _machine.KeyboardInt16Handler.GetNextKeyCode();
            if (scancode == null) {
                SetZeroFlag(true, calledFromVm);
                _state.AL = 0;
            } else {
                byte ascii = (byte)scancode.Value;
                SetZeroFlag(false, calledFromVm);
                _state.AL = ascii;
            }
        } else {
            // Output
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("DIRECT CONSOLE IO, {Character}, {Ascii}", character, ConvertUtils.ToChar(character));
            }
        }
    }

    public void DiskReset() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("DISK RESET (Nothing to do...)");
        }
    }

    public void DisplayOutput() {
        byte characterByte = _state.DL;
        string character = ConvertSingleDosChar(characterByte);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PRINT CHR: {CharacterByte} ({Character})", ConvertUtils.ToHex8(characterByte), character);
        }
        if (characterByte == '\r') {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("PRINT CHR LINE BREAK: {DisplayOutputBuilder}", _displayOutputBuilder);
            }
            _displayOutputBuilder = new StringBuilder();
        } else if (characterByte != '\n') {
            _displayOutputBuilder.Append(character);
        }
    }

    public void DuplicateFileHandle(bool calledFromVm) {
        ushort fileHandle = _state.BX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("DUPLICATE FILE HANDLE. {FileHandle}", fileHandle);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.DuplicateFileHandle(fileHandle);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public void FindFirstMatchingFile(bool calledFromVm) {
        ushort attributes = _state.CX;
        string fileSpec = GetStringAtDsDx();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FIND FIRST MATCHING FILE {Attributes}, {FileSpec}", ConvertUtils.ToHex16(attributes), fileSpec);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.FindFirstMatchingFile(fileSpec);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public void FindNextMatchingFile(bool calledFromVm) {
        ushort attributes = _state.CX;
        string fileSpec = GetStringAtDsDx();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FIND NEXT MATCHING FILE {Attributes}, {FileSpec}", ConvertUtils.ToHex16(attributes), fileSpec);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.FindNextMatchingFile();
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public void FreeMemoryBlock(bool calledFromVm) {
        ushort blockSegment = _state.ES;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("FREE ALLOCATED MEMORY {BlockSegment}", blockSegment);
        }
        SetCarryFlag(false, calledFromVm);
        if (!_dosMemoryManager.FreeMemoryBlock((ushort)(blockSegment - 1))) {
            LogDosError(calledFromVm);
            SetCarryFlag(true, calledFromVm);
            // INVALID MEMORY BLOCK ADDRESS
            _state.AX = 0x09;
        }
    }

    public void GetCurrentDefaultDrive() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET CURRENT DEFAULT DRIVE");
        }
        _state.AL = _defaultDrive;
    }

    public void GetDate() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET DATE");
        }
        DateTime now = DateTime.Now;
        _state.AL = (byte)now.DayOfWeek;
        _state.CX = (ushort)now.Year;
        _state.DH = (byte)now.Month;
        _state.DL = (byte)now.Day;
    }

    public void GetDiskTransferAddress() {
        _state.ES = _dosFileManager.DiskTransferAreaAddressSegment;
        _state.BX = _dosFileManager.DiskTransferAreaAddressOffset;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET DTA (DISK TRANSFER ADDRESS) DS:DX {DsDx}",
                ConvertUtils.ToSegmentedAddressRepresentation(_state.ES, _state.BX));
        }
    }

    public void GetDosVersion() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET DOS VERSION");
        }
        // 5.0
        _state.AL = 0x05;
        _state.AH = 0x00;
        // FF => MS DOS
        _state.BH = 0xFF;
        // DOS OEM KEY 0x00000
        _state.BL = 0x00;
        _state.CX = 0x00;
    }

    public void GetFreeDiskSpace() {
        byte driveNumber = _state.DL;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET FREE DISK SPACE FOR DRIVE {DriveNumber}", driveNumber);
        }
        // 127 sectors per cluster
        _state.AX = 0x7F;
        // 512 bytes per sector
        _state.CX = 0x200;
        // 4031 clusters available (~250MB)
        _state.BX = 0xFBF;
        // 16383 total clusters on disk (~1000MB)
        _state.DX = 0x3FFF;
    }

    /// <inheritdoc/>
    public override byte Index => 0x21;

    /// <summary>
    /// Function 35H returns the address stored in the interrupt vector table for the handler associated with the specified interrupt. <br/>
    /// To call:
    /// <ul>
    ///   <li>AH = 35H</li>
    ///   <li>AL = interrupt number</li>
    /// </ul>
    /// <returns>ES:BX = segment:offset of handler for interrupt specified in AL</returns>
    /// <remarks>Interrupt vectors should always be read with function 35H and set with function 25H (SetInterruptVector). <br/>
    /// Programs should never attempt to read or change interrupt vectors directly in memory.</remarks>
    /// </summary>
    public void GetInterruptVector() {
        byte vectorNumber = _state.AL;
        ushort segment = _memory.GetUint16((uint)((4 * vectorNumber) + 2));
        ushort offset = _memory.GetUint16((uint)(4 * vectorNumber));
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET INTERRUPT VECTOR INT {VectorInt}, got {SegmentedAddress}", ConvertUtils.ToHex8(vectorNumber),
                ConvertUtils.ToSegmentedAddressRepresentation(segment, offset));
        }
        _state.ES = segment;
        _state.BX = offset;
    }

    public void GetPspAddress() {
        ushort pspSegment = _dosMemoryManager.PspSegment;
        _state.BX = pspSegment;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET PSP ADDRESS {PspSegment}", ConvertUtils.ToHex16(pspSegment));
        }
    }

    public void GetSetControlBreak() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET/SET CTRL-C FLAG");
        }
        byte op = _state.AL;
        if (op == 0) {
            // GET
            _state.DL = _isCtrlCFlag ? (byte)1 : (byte)0;
        } else if (op is 1 or 2) {
            // SET
            _isCtrlCFlag = _state.DL == 1;
        } else {
            throw new UnhandledOperationException(_machine, "Ctrl-C get/set operation unhandled: " + op);
        }
    }

    public void GetTime() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET TIME");
        }
        DateTime now = DateTime.Now;
        _state.CH = (byte)now.Hour;
        _state.CL = (byte)now.Minute;
        _state.DH = (byte)now.Second;
        _state.DL = (byte)now.Millisecond;
    }

    public void ModifyMemoryBlock(bool calledFromVm) {
        ushort requestedSize = _state.BX;
        ushort blockSegment = _state.ES;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("MODIFY MEMORY BLOCK {Size}, {BlockSegment}", requestedSize, blockSegment);
        }
        SetCarryFlag(false, calledFromVm);
        if (!_dosMemoryManager.ModifyBlock((ushort)(blockSegment - 1), requestedSize)) {
            LogDosError(calledFromVm);
            // An error occurred. Report it as not enough memory.
            SetCarryFlag(true, calledFromVm);
            _state.AX = 0x08;
            _state.BX = 0;
        }
    }

    public void MoveFilePointerUsingHandle(bool calledFromVm) {
        byte originOfMove = _state.AL;
        ushort fileHandle = _state.BX;
        uint offset = (uint)(_state.CX << 16 | _state.DX);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("MOVE FILE POINTER USING HANDLE. {OriginOfMove}, {FileHandle}, {Offset}", originOfMove, fileHandle,
            offset);
        }

        DosFileOperationResult dosFileOperationResult =
            _dosFileManager.MoveFilePointerUsingHandle(originOfMove, fileHandle, offset);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public void OpenFile(bool calledFromVm) {
        string fileName = GetStringAtDsDx();
        byte accessMode = _state.AL;
        byte rwAccessMode = (byte)(accessMode & 0b111);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("OPEN FILE {FileName} with mode {AccessMod} (rwAccessMode:{RwAccessMode})", fileName, ConvertUtils.ToHex8(accessMode),
                ConvertUtils.ToHex8(rwAccessMode));
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.OpenFile(fileName, rwAccessMode);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public void PrintString() {
        ushort segment = _state.DS;
        ushort offset = _state.DX;
        string str = GetDosString(_memory, segment, offset, '$');

        _machine.VgaFunctions.WriteString(str);
        
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PRINT STRING: {String}", str);
        }
    }

    public void QuitWithExitCode() {
        byte exitCode = _state.AL;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("QUIT WITH EXIT CODE {ExitCode}", ConvertUtils.ToHex8(exitCode));
        }
        _cpu.IsRunning = false;
    }

    public void ReadFile(bool calledFromVm) {
        ushort fileHandle = _state.BX;
        ushort readLength = _state.CX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("READ FROM FILE handle {FileHandle} length {ReadLength} to {DsDx}", fileHandle, readLength,
                ConvertUtils.ToSegmentedAddressRepresentation(_state.DS, _state.DX));
        }
        uint targetMemory = MemoryUtils.ToPhysicalAddress(_state.DS, _state.DX);
        DosFileOperationResult dosFileOperationResult = _dosFileManager.ReadFile(fileHandle, readLength, targetMemory);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    public void SelectDefaultDrive() {
        _defaultDrive = _state.DL;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SELECT DEFAULT DRIVE {DefaultDrive}", _defaultDrive);
        }
        // Number of valid drive letters
        _state.AL = 26;
    }

    public void SetDiskTransferAddress() {
        ushort segment = _state.DS;
        ushort offset = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SET DTA (DISK TRANSFER ADDRESS) DS:DX {DsDxSegmentOffset}",
                ConvertUtils.ToSegmentedAddressRepresentation(segment, offset));
        }
        _dosFileManager.SetDiskTransferAreaAddress(segment, offset);
    }

    public void SetInterruptVector() {
        byte vectorNumber = _state.AL;
        ushort segment = _state.DS;
        ushort offset = _state.DX;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SET INTERRUPT VECTOR FOR INT {VectorNumber} at address {SegmentOffset}", ConvertUtils.ToHex(vectorNumber),
                ConvertUtils.ToSegmentedAddressRepresentation(segment, offset));
        }
        SetInterruptVector(vectorNumber, segment, offset);
    }

    public void SetInterruptVector(byte vectorNumber, ushort segment, ushort offset) {
        _memory.SetUint16((ushort)((4 * vectorNumber) + 2), segment);
        _memory.SetUint16((ushort)(4 * vectorNumber), offset);
    }

    public void WriteFileUsingHandle(bool calledFromVm) {
        ushort fileHandle = _state.BX;
        ushort writeLength = _state.CX;
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(_state.DS, _state.DX);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("WRITE TO FILE handle {FileHandle} length {WriteLength} from {DsDx}", ConvertUtils.ToHex(fileHandle),
                ConvertUtils.ToHex(writeLength), ConvertUtils.ToSegmentedAddressRepresentation(_state.DS, _state.DX));
        }
        DosFileOperationResult dosFileOperationResult =
            _dosFileManager.WriteFileUsingHandle(fileHandle, writeLength, bufferAddress);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    private string ConvertSingleDosChar(byte characterByte) {
        ReadOnlySpan<byte> sourceAsArray = stackalloc byte[] {characterByte};
        return _cp850CharSet.GetString(sourceAsArray);
    }
    
    private string ConvertDosChars(IEnumerable<byte> characterBytes) {
        return _cp850CharSet.GetString(characterBytes.ToArray());
    }
    
    public void GetCurrentDirectory(bool calledFromVm) {
        SetCarryFlag(false, calledFromVm);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET CURRENT DIRECTORY {ResponseAddress}",
                ConvertUtils.ToSegmentedAddressRepresentation(_state.DS, _state.SI));
        }
        uint responseAddress = MemoryUtils.ToPhysicalAddress(_state.DS, _state.SI);
        // Fake that we are always at the root of the drive (empty String)
        _memory.SetUint8(responseAddress, 0);
    }

    private string GetDosString(Memory memory, ushort segment, ushort offset, char end) {
        uint stringStart = MemoryUtils.ToPhysicalAddress(segment, offset);
        StringBuilder stringBuilder = new();
        List<byte> sourceArray = new();
        while (memory.GetUint8(stringStart) != end) {
            sourceArray.Add(memory.GetUint8(stringStart++));
        }
        string convertedString = ConvertDosChars(sourceArray);
        stringBuilder.Append(convertedString);
        return stringBuilder.ToString();
    }

    public void GetSetFileAttributes(bool calledFromVm) {
        byte op = _state.AL;
        string dosFileName = GetStringAtDsDx();
        string? fileName = _dosFileManager.ToHostCaseSensitiveFileName(dosFileName, false);
        if (!File.Exists(fileName)) {
            LogDosError(calledFromVm);
            SetCarryFlag(true, calledFromVm);
            // File not found
            _state.AX = 0x2;
            return;
        }
        SetCarryFlag(false, calledFromVm);
        switch (op) {
            case 0: {
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("GET FILE ATTRIBUTE {FilneName}", fileName);
                    }
                    FileAttributes attributes = File.GetAttributes(fileName);
                    // let's always return the file is read / write
                    bool canWrite = (attributes & FileAttributes.ReadOnly) != FileAttributes.ReadOnly;
                    _state.CX = canWrite ? (byte)0 : (byte)1;
                    break;
                }
            case 1: {
                    ushort attribute = _state.CX;
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("SET FILE ATTRIBUTE {FileName}, {Attribute}", fileName, attribute);
                    }
                    break;
                }
            default: throw new UnhandledOperationException(_machine, "getSetFileAttribute operation unhandled: " + op);
        }
    }

    private string GetStringAtDsDx() {
        return GetDosString(_memory, _state.DS, _state.DX, '\0');
    }

    private void IoControl(bool calledFromVm) {
        byte op = _state.AL;
        ushort handle = _state.BX;

        SetCarryFlag(false, calledFromVm);

        switch (op) {
            case 0: {
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("GET DEVICE INFORMATION for handle {Handle}", handle);
                    }

                    if (handle < _devices.Count) {
                        IVirtualDevice device = _devices[handle];
                        // @TODO: use the device and it's attributes to fill the response
                    }
                    _state.DX = handle < _devices.Count ? (ushort)0x80D3 : (ushort)0x0002;
                    break;
                }
            case 1: {
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("SET DEVICE INFORMATION for handle {Handle} (unimplemented)", handle);
                    }
                    break;
                }
            case 0xE: {
                    ushort driveNumber = _state.BL;
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("GET LOGICAL DRIVE FOR PHYSICAL DRIVE {DriveNumber}", driveNumber);
                    }
                    // Only one drive
                    _state.AL = 0;
                    break;
                }
            default: throw new UnhandledOperationException(_machine, $"IO Control operation unhandled: {op}");
        }
    }

    private void LogDosError(bool calledFromVm) {
        string returnMessage = "";
        if (calledFromVm) {
            returnMessage = $"Int will return to {_machine.PeekReturn()}. ";
        }
        if (_loggerService.IsEnabled(LogEventLevel.Error)) {
            _loggerService.Error("DOS operation failed with an error. {ReturnMessage}. State is {State}", returnMessage, _state.ToString());
        }
    }
    
    private void SetStateFromDosFileOperationResult(bool calledFromVm, DosFileOperationResult dosFileOperationResult) {
        if (dosFileOperationResult.IsError) {
            LogDosError(calledFromVm);
            SetCarryFlag(true, calledFromVm);
        } else {
            SetCarryFlag(false, calledFromVm);
        }
        uint? value = dosFileOperationResult.Value;
        if (value == null) {
            return;
        }
        _state.AX = (ushort)value.Value;
        if (dosFileOperationResult.IsValueIsUint32) {
            _state.DX = (ushort)(value >> 16);
        }
    }
}
