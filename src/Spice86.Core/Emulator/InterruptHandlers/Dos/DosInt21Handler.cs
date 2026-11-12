using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;

using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using static Spice86.Core.Emulator.InterruptHandlers.Common.Contracts.CpuValues;

/// <summary>
/// Implementation of the DOS INT21H services.
/// </summary>
public class DosInt21Handler : InterruptHandler {
    private readonly DosMemoryManager _dosMemoryManager;
    private readonly DosDriveManager _dosDriveManager;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly DosFileManager _dosFileManager;
    private readonly KeyboardInt16Handler _keyboardInt16Handler;
    private readonly DosCodePageState _dosCodePageState;
    private readonly DosStringDecoder _dosStringDecoder;
    private readonly CountryInfo _countryInfo;
    private readonly DosProcessManager _dosProcessManager;
    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly DosTables _dosTables;
    private readonly DosSwappableDataArea _sda;
    private readonly DosFcbManager _dosFcbManager;
    private readonly DosSysVars _dosSysVars;
    private readonly DosJobFileTable _jobFileTable;
    private readonly IEmulatedClock _emulatedClock;
    private uint _stdinReadyFlagLinearAddress;
    private SegmentedAddress? _stdinWaitLoopAddress;
    private bool _isCtrlCFlag;

    private const ushort OffsetMask = 0x0F;
    private const byte ExpectedValueOfALInCreateChildPsp = 0xF0;
    private const ushort DefaultBytesPerSector = 0x200;
    private const byte DefaultSectorsPerCluster = 0x20;
    private const ushort DefaultTotalClusters = 0x7FFD;
    private const ushort MaximumJobFileTableSize = 0xFF;
    private const ushort PspJobFileTableOffset = 0x18;
    private const byte ClearInputBufferAndReadFunction = 0x0C;
    private const byte SetCurrentPspFunction = 0x50;
    private const byte GetCurrentPspFunction = 0x51;
    private const byte GetPspAddressFunction = 0x62;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The emulator memory.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="keyboardInt16Handler">The keyboard interrupt handler.</param>
    /// <param name="countryInfo">The DOS kernel's global region settings.</param>
    /// <param name="dosCodePageState">The DOS kernel's shared code page state.</param>
    /// <param name="dosStringDecoder">The helper class used to encode/decode DOS strings.</param>
    /// <param name="dosMemoryManager">The DOS class used to manage DOS MCBs.</param>
    /// <param name="dosFileManager">The DOS class responsible for DOS file access.</param>
    /// <param name="dosDriveManager">The DOS class responsible for DOS volumes.</param>
    /// <param name="dosProcessManager">The DOS class responsible for program loading and execution.</param>
    /// <param name="ioPortDispatcher">The I/O port dispatcher for accessing hardware ports (e.g., CMOS).</param>
    /// <param name="dosTables">The DOS tables structure containing CDS and DBCS information.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="dosFcbManager">FCB manager shared with the process manager for cleanup.</param>
    /// <param name="emulatedClock">The emulated clock read by the DOS time service.</param>
    public DosInt21Handler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        KeyboardInt16Handler keyboardInt16Handler, CountryInfo countryInfo,
        DosCodePageState dosCodePageState, DosStringDecoder dosStringDecoder, DosMemoryManager dosMemoryManager,
        DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        DosProcessManager dosProcessManager,
        IOPortDispatcher ioPortDispatcher, DosTables dosTables, DosSysVars dosSysVars, ILogger loggerService, DosFcbManager dosFcbManager,
        IEmulatedClock emulatedClock)
            : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _sda = new(memory, MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));
        _countryInfo = countryInfo;
        _dosCodePageState = dosCodePageState;
        _dosStringDecoder = dosStringDecoder;
        _keyboardInt16Handler = keyboardInt16Handler;
        _dosMemoryManager = dosMemoryManager;
        _dosFileManager = dosFileManager;
        _dosDriveManager = dosDriveManager;
        _dosProcessManager = dosProcessManager;
        _ioPortDispatcher = ioPortDispatcher;
        _dosTables = dosTables;
        _dosSysVars = dosSysVars;
        _interruptVectorTable = new InterruptVectorTable(memory);
        _dosFcbManager = dosFcbManager;
        _emulatedClock = emulatedClock;
        _jobFileTable = new DosJobFileTable(memory);
    }

    /// <summary>
    /// Gets the DOS process manager for the <see cref="DosProgramLoader"/>
    /// </summary>
    internal DosProcessManager ProcessManager => _dosProcessManager;

    /// <summary>
    /// Gets the DOS file manager for the <see cref="DosProgramLoader"/>
    /// </summary>
    internal DosFileManager FileManager => _dosFileManager;

    /// <summary>
    ///     Emits an INT 21h handler stub into guest RAM that dispatches all AH values to the
    ///     C# handler via <c>callback(Run)</c>. For AH=07h, 08h, and 0Ah the stub first
    ///     polls STDIN readiness with INT 28h idle calls before dispatching.
    ///     AH=0Ch goes through L_DEFAULT: the C# handler flushes the input, then for AL=01h, 07h, 08h
    ///     and 0Ah moves CS:IP to L_WAIT_FOR_STDIN, so that function waits for a key typed after the flush.
    ///     AH=01h is NOT polled here — it goes through L_DEFAULT to the C# handler which
    ///     reads via ConsoleDevice → INT 16h (which waits with interrupts enabled for IRQ 1).
    /// </summary>
    /// <remarks><![CDATA[
    ///     A flag byte is reserved in the handler segment, before the entry point.
    ///     The CheckStdinReady callback writes to this byte (1 = ready, 0 = not ready).
    ///     ASM reads it with CMP byte CS:[flagOffset], 0 — no CPU state is modified by the
    ///     readiness callback.
    ///
    ///     Assembled control flow (labels for readability):
    ///
    ///     stdin_ready_flag: DB 0            ; private flag byte in handler segment
    ///
    ///     L_HANDLER:
    ///       CMP AH, 07h
    ///       JZ  L_WAIT_FOR_STDIN
    ///       CMP AH, 08h
    ///       JZ  L_WAIT_FOR_STDIN
    ///       CMP AH, 0Ah
    ///       JZ  L_WAIT_FOR_STDIN
    ///
    ///     L_DEFAULT:
    ///       callback(Run)                    ; dispatch to C# for all AH values
    ///       IRET
    ///
    ///     L_WAIT_FOR_STDIN:
    ///       callback(CheckStdinReady)        ; writes flag byte
    ///       CMP byte CS:[stdin_ready_flag], 0
    ///       JNZ L_DISPATCH
    ///       INT 28h                          ; DOS idle
    ///       JMP short L_WAIT_FOR_STDIN
    ///
    ///     L_DISPATCH:
    ///       callback(DispatchStdinInput)     ; runs AH, or for AH=0Ch the input function in AL
    ///       IRET
    /// ]]></remarks>
    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Reserve a flag byte in the handler segment for callback→ASM communication.
        // The CheckStdinReady callback writes to this byte; ASM reads with CS:[flagOffset].
        SegmentedAddress flagAddress = memoryAsmWriter.CurrentAddress;
        _stdinReadyFlagLinearAddress = MemoryUtils.ToPhysicalAddress(flagAddress.Segment, flagAddress.Offset);
        ushort flagOffset = flagAddress.Offset;
        memoryAsmWriter.WriteUInt8(0x00);

        // Block sizes for jump offset calculation:
        // L_DEFAULT:          callback(4) + IRET(1) = 5 bytes
        // L_WAIT_FOR_STDIN:   STI(1) + callback(4) + CMP(6) + JNZ(2) + INT(2) + JMP(2) = 17 bytes
        // L_DISPATCH:         callback(4) + IRET(1) = 5 bytes

        SegmentedAddress handlerAddress = memoryAsmWriter.CurrentAddress;

        // CMP AH, 07h → JZ L_WAIT_FOR_STDIN
        memoryAsmWriter.WriteCmpAh(0x07);
        // skip: CMP 08h(5) + CMP 0Ah(5) + L_DEFAULT(5) = 15
        memoryAsmWriter.WriteJz(15);

        // CMP AH, 08h → JZ L_WAIT_FOR_STDIN
        memoryAsmWriter.WriteCmpAh(0x08);
        // skip: CMP 0Ah(5) + L_DEFAULT(5) = 10
        memoryAsmWriter.WriteJz(10);

        // CMP AH, 0Ah → JZ L_WAIT_FOR_STDIN
        memoryAsmWriter.WriteCmpAh(0x0A);
        // skip: L_DEFAULT(5) = 5
        memoryAsmWriter.WriteJz(5);

        // L_DEFAULT: callback to C# Run() then IRET
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, Run);
        memoryAsmWriter.WriteIret();

        // L_WAIT_FOR_STDIN: poll STDIN readiness in a loop with INT 28h idle
        // STI — re-enable hardware interrupts so IRQ1 (keyboard) can fire.
        // INT 21h clears IF per x86 spec; without STI the polling loop would
        // spin forever because keyboard scancodes never reach BiosKeyboardBuffer.
        // This matches FreeDOS (INT 21h starts with STI) and DOSBox (idle uses STI+HLT).
        _stdinWaitLoopAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.WriteSti();
        // callback(CheckStdinReady) — writes flag byte, 4 bytes
        memoryAsmWriter.RegisterAndWriteCallback(CallbackNumbers.DosInt21CheckStdinReady, CallbackCheckStdinReady);
        // CMP byte CS:[flagOffset], 0 — 6 bytes
        memoryAsmWriter.WriteUInt8(0x2E); // CS: segment override
        memoryAsmWriter.WriteUInt8(0x80); // CMP Eb, Ib
        memoryAsmWriter.WriteUInt8(0x3E); // ModR/M: [disp16], /7 (CMP)
        memoryAsmWriter.WriteUInt16(flagOffset);
        memoryAsmWriter.WriteUInt8(0x00); // imm8 = 0
        // JNZ L_DISPATCH — skip INT 28h(2) + JMP(2) = 4
        memoryAsmWriter.WriteJnz(4);
        // INT 28h (DOS idle)
        memoryAsmWriter.WriteInt(0x28);
        // JMP short back to STI: -(4+6+2+2+2+1) = -17
        memoryAsmWriter.WriteJumpShort(-17);

        // L_DISPATCH: dispatch to C# handler for AH=07h/08h/0Ah, or for AH=0Ch the function in AL
        memoryAsmWriter.RegisterAndWriteCallback(CallbackNumbers.DosInt21StdinInputDispatch, DispatchStdinInput);
        memoryAsmWriter.WriteIret();

        return handlerAddress;
    }

    /// <summary>
    /// Last step of the ASM stub's STDIN wait loop, once STDIN is ready: runs the function in AH, or for
    /// AH=0Ch, whose flush already ran, the input function in AL.
    /// </summary>
    public void DispatchStdinInput() {
        if (State.AH == ClearInputBufferAndReadFunction) {
            RunFlushFollowUp();
            return;
        }
        Run();
    }

    /// <summary>
    /// Writes 1 to the handler's flag byte if stdin has data, 0 if empty.
    /// No CPU registers or flags are modified.
    /// Used by the ASM wait loop for AH=07h/08h/0Ah.
    /// </summary>
    public void CallbackCheckStdinReady() {
        Memory[_stdinReadyFlagLinearAddress] = IsStandardInputAvailable() ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// INT 21h, AH=01h - Character Input with Echo.
    /// <para>
    /// Reads a single character from standard input and echoes it to standard output.
    /// The program waits for input; the user just needs to press the intended key
    /// WITHOUT pressing "enter" key.
    /// </para>
    /// <b>Returns:</b><br/>
    /// AL = ASCII code of the input character
    /// </summary>
    /// <remarks>
    /// This is a blocking call that reads from STDIN.
    /// The console device's Read method handles the echo internally when Echo is true. <br/>
    /// Used by (for example): Civilization.
    /// </remarks>
    [InterruptService(0x01, Unknown = true)]
    public void CharacterInputWithEcho() {
        if (CheckCtrlCOrCtrlBreakInKeyboardBuffer()) {
            return;
        }
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("CHARACTER INPUT WITH ECHO");
        }
        VirtualFileBase? stdin = GetStandardFile(DosStandardHandle.Stdin);
        if (stdin != null && stdin.CanRead) {
            ConsoleDevice? consoleDevice = stdin as ConsoleDevice;
            bool previousEchoState = false;
            if (consoleDevice != null) {
                previousEchoState = consoleDevice.Echo;
                consoleDevice.Echo = true;
            }

            byte[] bytes = new byte[1];
            int readCount = stdin.Read(bytes, 0, 1);

            if (consoleDevice != null) {
                consoleDevice.Echo = previousEchoState;
            }

            State.AL = readCount < 1 ? (byte)0 : bytes[0];
        } else {
            State.AL = 0;
        }
    }

    [InterruptService(0x2B, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite, Io = true,
        Implementation = nameof(SetDate), Inputs = [CpuValues.Cx, CpuValues.Dh, CpuValues.Dl])]
    public void SetDate() {
        State.AL = SetDate(State.CX, State.DH, State.DL);
    }

    /// <summary>Sets the DOS date to <paramref name="year"/>-<paramref name="month"/>-<paramref name="day"/>;
    /// returns 0 on success or 0xFF on an invalid date.</summary>
    public byte SetDate(ushort year, byte month, byte day) {
        bool valid = false;
        try {
            if (year >= 1980 && year <= 2099 && month >= 1 && month <= 12 && day >= 1) {
                _ = new DateTime(year, month, day);
                valid = true;
            }
        } catch (ArgumentOutOfRangeException) {
            valid = false;
        }

        if (!valid) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("SET DOS DATE called with invalid date: {Year}-{Month:D2}-{Day:D2}",
                    year, month, day);
            }
            return 0xFF;
        }

        int century = year / 100;
        int yearPart = year % 100;

        byte yearBcd = BcdConverter.ToBcd((byte)yearPart);
        byte monthBcd = BcdConverter.ToBcd(month);
        byte dayBcd = BcdConverter.ToBcd(day);
        byte centuryBcd = BcdConverter.ToBcd((byte)century);

        WriteCmosRegister(CmosRegisterAddresses.Year, yearBcd);
        WriteCmosRegister(CmosRegisterAddresses.Month, monthBcd);
        WriteCmosRegister(CmosRegisterAddresses.DayOfMonth, dayBcd);
        WriteCmosRegister(CmosRegisterAddresses.Century, centuryBcd);

        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("SET DOS DATE to CMOS: {Year}-{Month:D2}-{Day:D2}",
                year, month, day);
        }
        return 0;
    }

    [InterruptService(0x2D, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite, Io = true,
        Implementation = nameof(SetTime), Inputs = [CpuValues.Ch, CpuValues.Cl, CpuValues.Dh, CpuValues.Dl])]
    public void SetTime() {
        State.AL = SetTime(State.CH, State.CL, State.DH, State.DL);
    }

    /// <summary>Sets the DOS time to <paramref name="hour"/>:<paramref name="minutes"/>:
    /// <paramref name="seconds"/>.<paramref name="hundredths"/>; returns 0 on success or 0xFF on an
    /// invalid time.</summary>
    public byte SetTime(byte hour, byte minutes, byte seconds, byte hundredths) {
        bool valid = hour <= 23 &&
                     minutes <= 59 &&
                     seconds <= 59 &&
                     hundredths <= 99;

        if (!valid) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("SET DOS TIME called with invalid time: {Hour:D2}:{Minutes:D2}:{Seconds:D2}.{Hundredths:D2}",
                    hour, minutes, seconds, hundredths);
            }
            return 0xFF;
        }

        byte hourBcd = BcdConverter.ToBcd(hour);
        byte minutesBcd = BcdConverter.ToBcd(minutes);
        byte secondsBcd = BcdConverter.ToBcd(seconds);

        WriteCmosRegister(CmosRegisterAddresses.Hours, hourBcd);
        WriteCmosRegister(CmosRegisterAddresses.Minutes, minutesBcd);
        WriteCmosRegister(CmosRegisterAddresses.Seconds, secondsBcd);

        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("SET DOS TIME to CMOS: {Hour:D2}:{Minutes:D2}:{Seconds:D2}.{Hundredths:D2}",
                hour, minutes, seconds, hundredths);
        }
        return 0;
    }

    /// <summary>
    /// INT 21h, AH=63h - Get Double Byte Character Set (DBCS) Lead Byte Table.
    /// <para>
    /// Returns a pointer to the DBCS lead-byte table, which indicates which byte values
    /// are lead bytes in double-byte character sequences (e.g., Japanese, Chinese, Korean).
    /// An empty table (value 0) indicates no DBCS ranges are defined.
    /// </para>
    /// <b>Expects:</b><br/>
    /// AL = 0 to get DBCS lead byte table pointer
    /// <b>Returns:</b><br/>
    /// If AL was 0:<br/>
    /// - DS:SI = pointer to DBCS lead byte table<br/>
    /// - AL = 0<br/>
    /// - CF = 0 (undocumented)<br/>
    /// If AL was not 0:<br/>
    /// - AL = 0xFF<br/>
    /// </summary>
    [InterruptService(0x63, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al | CpuValues.Ds | CpuValues.Si | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadLeadByteTable), Inputs = [CpuValues.Al, CpuValues.Si, CpuValues.Ds, CpuValues.Carry])]
    private void GetLeadByteTable() {
        (byte al, ushort si, ushort ds, bool carryFlag) = ReadLeadByteTable(
            State.AL, State.SI, State.DS, ReadCarryFlag(true));
        State.AL = al;
        State.SI = si;
        State.DS = ds;
        SetCarryFlag(carryFlag, true);
    }

    /// <summary>Returns the DBCS lead-byte table for <paramref name="operation"/> 0;
    /// other operations only return AL=0xFF and preserve SI, DS and the carry flag.</summary>
    public (byte al, ushort si, ushort ds, bool carryFlag) ReadLeadByteTable(
        byte operation, ushort previousSi, ushort previousDs, bool previousCarry) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("INT 21h AH=63h - Get DBCS Lead Byte Table, AL={AL:X2}", operation);
        }

        if (operation != 0) {
            // Returns error without modifying carry flag for invalid subfunction
            return (0xFF, previousSi, previousDs, previousCarry);
        }

        uint dbcsAddress = _dosTables.DoubleByteCharacterSet.BaseAddress;
        ushort segment = MemoryUtils.ToSegment(dbcsAddress);
        ushort offset = (ushort)(dbcsAddress & OffsetMask);

        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("Returning DBCS table pointer at {Segment:X4}:{Offset:X4}", segment, offset);
        }
        return (0, offset, segment, false);
    }

    /// <summary>
    /// Obtains or selects the current code page.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x66, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Bx | CpuValues.Dx | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite)]
    public void GetSetGlobalLoadedCodePageTable(bool calledFromVm) {
        (DosFileOperationResult result, ushort bx, ushort dx) =
            GetSetCodePage(State.AL, State.BX, State.DX);
        State.BX = bx;
        State.DX = dx;
        SetStateFromDosFileOperationResult(calledFromVm, result);
    }

    /// <summary>Queries (<paramref name="operation"/> 1) the active and system code pages into the
    /// returned <c>bx</c> and <c>dx</c>, or sets (<paramref name="operation"/> 2) the active code page.</summary>
    public (DosFileOperationResult result, ushort bx, ushort dx) GetSetCodePage(
        byte operation, ushort activeCodePage, ushort systemCodePage) {
        switch (operation) {
            case 1:
                return (DosFileOperationResult.NoValue(),
                    _dosCodePageState.ActiveCodePage, _dosCodePageState.SystemCodePage);
            case 2:
                if (_dosCodePageState.TrySetActiveCodePage(activeCodePage, systemCodePage,
                        out DosErrorCode errorCode)) {
                    return (DosFileOperationResult.NoValue(), activeCodePage, systemCodePage);
                }
                return (DosFileOperationResult.LogError(errorCode), activeCodePage, systemCodePage);
            default:
                return (DosFileOperationResult.LogError(DosErrorCode.FunctionNumberInvalid),
                    activeCodePage, systemCodePage);
        }
    }

    /// <summary>
    /// INT 21h, AH=67h - Set handle count.
    /// </summary>
    /// <remarks>
    /// Input: BX = number of file handles for the current PSP.
    /// </remarks>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x67, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ResizeHandleTable), Inputs = [CpuValues.Bx, CpuValues.Ax])]
    public void SetHandleCount(bool calledFromVm) {
        (ushort ax, bool carryFlag) = ResizeHandleTable(State.BX, State.AX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>
    /// Resizes the current PSP's job file table to <paramref name="requestedSize"/> handles.
    /// </summary>
    /// <returns><paramref name="previousAx"/> on success (AX is preserved), or the DOS error
    /// code and the carry flag on error.</returns>
    public (ushort ax, bool carryFlag) ResizeHandleTable(ushort requestedSize, ushort previousAx) {
        ushort currentPspSegment = _sda.CurrentProgramSegmentPrefix;
        uint pspAddress = MemoryUtils.ToPhysicalAddress(currentPspSegment, 0);
        DosProgramSegmentPrefix currentPsp = new(Memory, pspAddress);
        ushort currentSize = _jobFileTable.GetSize(currentPspSegment);
        requestedSize = Math.Clamp(requestedSize, DosFileManager.MaxOpenFilesPerProcess, MaximumJobFileTableSize);
        if (requestedSize == currentSize) {
            return FileResult(DosFileOperationResult.NoValue(), previousAx);
        }
        for (ushort handle = requestedSize; handle < currentSize; handle++) {
            if (_jobFileTable.GetEntry(currentPspSegment, handle) != DosJobFileTable.UnusedEntry) {
                return FileResult(DosFileOperationResult.LogError(DosErrorCode.TooManyOpenFiles), previousAx);
            }
        }
        SegmentedAddress newTableAddress = new(currentPspSegment, PspJobFileTableOffset);
        if (requestedSize > DosFileManager.MaxOpenFilesPerProcess) {
            try {
                ushort tableSegment = _dosTables.GetJobFileTableSegment(currentPspSegment);
                newTableAddress = new SegmentedAddress(tableSegment, 0);
            } catch (InvalidOperationException) {
                return FileResult(DosFileOperationResult.LogError(DosErrorCode.InsufficientMemory), previousAx);
            }
        }
        byte[] entries = new byte[requestedSize];
        Array.Fill(entries, DosJobFileTable.UnusedEntry);
        for (ushort handle = 0; handle < Math.Min(requestedSize, currentSize); handle++) {
            entries[handle] = _jobFileTable.GetEntry(currentPspSegment, handle);
        }
        currentPsp.FileTableAddress = newTableAddress;
        currentPsp.MaximumOpenFiles = requestedSize;
        for (ushort handle = 0; handle < requestedSize; handle++) {
            _jobFileTable.SetEntry(currentPspSegment, handle, entries[handle]);
        }
        return FileResult(DosFileOperationResult.NoValue(), previousAx);
    }

    private bool IsStandardInputAvailable() {
        return _dosFileManager.IsInputAvailable(GetStandardFile(DosStandardHandle.Stdin));
    }

    /// <summary>
    /// Reads a character from the standard auxiliary device (usually the keyboard) and stores it in AL.
    /// </summary>
    /// <remarks>
    /// Standard AUX is usually the first serial port (COM1). <br/>
    /// Checks for Ctrl-C/Ctrl-Break in STDIN before reading. <br/>
    /// </remarks>
    [InterruptService(0x03, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    public void ReadCharacterFromStdAux() {
        if (CheckCtrlCOrCtrlBreakInKeyboardBuffer()) {
            return;
        }
        State.AL = ReadAuxiliaryCharacter();
    }

    /// <summary>Reads one character from the standard auxiliary device; returns 0 when it is
    /// unavailable.</summary>
    public byte ReadAuxiliaryCharacter() {
        if (_dosFileManager.TryGetOpenDeviceWithAttributes(DeviceAttributes.Character,
            out AuxDevice? aux) && aux.CanRead is true) {
            return (byte)aux.ReadByte();
        }
        return 0x0;
    }

    /// <summary>
    /// Writes a character from the AL register to the standard auxiliary device.
    /// </summary>
    /// <remarks>
    /// Checks for Ctrl-C/Ctrl-Break in STDIN before writing. <br/>
    /// </remarks>
    [InterruptService(0x04, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.None, Memory = ServiceMemoryEffect.ReadWrite)]
    public void WriteCharacterToStdAux() {
        if (CheckCtrlCOrCtrlBreakInKeyboardBuffer()) {
            return;
        }
        WriteAuxiliaryCharacter(State.AL);
    }

    /// <summary>Writes <paramref name="character"/> to the standard auxiliary device when it is
    /// open for writing.</summary>
    public void WriteAuxiliaryCharacter(byte character) {
        if (_dosFileManager.TryGetOpenDeviceWithAttributes(DeviceAttributes.Character,
            out AuxDevice? aux) && aux.CanWrite is true) {
            aux.WriteByte(character);
        }
    }

    /// <summary>
    /// Writes a character from the AL register to the printer device.
    /// </summary>
    /// <remarks>
    /// Standard printer is usually the first parallel port (LPT1), but may be redirected, as usual with any device in DOS 2+. <br/>
    /// Checks for Ctrl-C/Ctrl-Break in STDIN before writing. <br/>
    /// TODO: If the printer is busy, this function will wait. <br/>
    /// </remarks>
    [InterruptService(0x05, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.None, Memory = ServiceMemoryEffect.ReadWrite)]
    public void PrinterOutput() {
        if (CheckCtrlCOrCtrlBreakInKeyboardBuffer()) {
            return;
        }
        WritePrinterCharacter(State.AL);
    }

    /// <summary>Writes <paramref name="character"/> to the printer device.</summary>
    public void WritePrinterCharacter(byte character) {
        if (_dosFileManager.TryGetOpenDeviceWithAttributes(DeviceAttributes.Character,
            out PrinterDevice? prn) && prn.CanWrite is true) {
            prn.Write(character);
        } else if (LoggerService.IsEnabled(LogLevel.Warning)) {
            LoggerService.LogWarning("DOS INT21H PrinterOutput: Cannot write to printer device.");
        }
    }

    /// <summary>
    /// Returns 0xFF in AL if input character is available in the standard input, 0 otherwise.
    /// When the break flag is set, also checks for Ctrl-C in the keyboard buffer.
    /// </summary>
    [InterruptService(0x0B, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    public void CheckStandardInputStatus() {
        if (_isCtrlCFlag && CheckCtrlCOrCtrlBreakInKeyboardBuffer()) {
            return;
        }
        State.AL = ReadStandardInputStatus();
    }

    /// <summary>Returns 0xFF when a character is available on standard input, 0 otherwise.</summary>
    public byte ReadStandardInputStatus() =>
        IsStandardInputAvailable() ? (byte)0xFF : (byte)0x0;

    /// <summary>
    /// INT 21h, AH=07h - Direct Standard Input Without Echo.
    /// <para>
    /// Reads a single character from the STDIN handle into AL, without echo.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The in-RAM ASM stub waits for STDIN readiness with INT 28h idle calls,
    /// then dispatches here via <c>callback(Run)</c>.
    /// </remarks>
    [InterruptService(0x07, Unknown = true)]
    public void DirectStandardInputWithoutEcho() {
        VirtualFileBase? stdin = GetStandardFile(DosStandardHandle.Stdin);
        if (stdin != null && stdin.CanRead) {
            byte[] bytes = new byte[1];
            int readCount = stdin.Read(bytes, 0, 1);
            if (readCount < 1) {
                State.AL = 0;
            } else {
                State.AL = bytes[0];
            }
        } else {
            State.AL = 0;
        }
    }

    /// <summary>
    /// INT 21h, AH=08h - Direct Standard Input Without Echo With Break Check.
    /// <para>
    /// Reads a single character from the STDIN handle into AL, without echo.
    /// Checks for Ctrl-C in the keyboard buffer first.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The in-RAM ASM stub waits for STDIN readiness with INT 28h idle calls,
    /// then dispatches here via <c>callback(Run)</c>.
    /// </remarks>
    [InterruptService(0x08, Unknown = true)]
    public void DirectStandardInputWithoutEchoWithBreakCheck() {
        if (CheckCtrlCOrCtrlBreakInKeyboardBuffer()) {
            return;
        }
        DirectStandardInputWithoutEcho();
    }

    /// <summary>
    /// Creates a directory from the path pointed by DS:DX.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    [InterruptService(0x39, StringArgument = ServiceStringArgument.AsciiZ, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(CreateDirectory), Inputs = [CpuValues.Ds | CpuValues.Dx, CpuValues.Ax])]
    public void CreateDirectory(bool calledFromVm) {
        (ushort ax, bool carryFlag) = CreateDirectory(
            _dosStringDecoder.GetZeroTerminatedStringAtDsDx(), State.AX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Creates the directory <paramref name="path"/>.</summary>
    public (ushort ax, bool carryFlag) CreateDirectory(string path, ushort previousAx) =>
        FileResult(_dosFileManager.CreateDirectory(path), previousAx);

    /// <summary>
    /// Removes a file path, pointed by DS:DX.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    [InterruptService(0x41, StringArgument = ServiceStringArgument.AsciiZ, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite)]
    public void RemoveFile(bool calledFromVm) {
        SetStateFromDosFileOperationResult(calledFromVm,
            DeleteFile(_dosStringDecoder.GetZeroTerminatedStringAtDsDx()));
    }

    /// <summary>Deletes the file <paramref name="path"/>.</summary>
    public DosFileOperationResult DeleteFile(string path) =>
        _dosFileManager.RemoveFile(path);

    /// <summary>
    /// Removes a directory, pointed by DS:DX.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    [InterruptService(0x3A, StringArgument = ServiceStringArgument.AsciiZ, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite)]
    public void RemoveDirectory(bool calledFromVm) {
        SetStateFromDosFileOperationResult(calledFromVm,
            RemoveDirectory(_dosStringDecoder.GetZeroTerminatedStringAtDsDx()));
    }

    /// <summary>Removes the directory <paramref name="path"/>.</summary>
    public DosFileOperationResult RemoveDirectory(string path) =>
        _dosFileManager.RemoveDirectory(path);

    /// <summary>
    /// INT 21h, AH=1Ch - Get allocation info for a specific drive.
    /// Returns AL=0xFF if the drive is invalid or not mounted as a block drive.
    /// </summary>
    [InterruptService(0x1C, Reads = CpuValues.Ah | CpuValues.Dl | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx | CpuValues.Ds, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadAllocationInfo),
        Inputs = [CpuValues.Dl, CpuValues.Ah, CpuValues.Cx, CpuValues.Dx, CpuValues.Bx, CpuValues.Ds])]
    public void GetAllocationInfoForAnyDrive() {
        (ushort ax, ushort cx, ushort dx, ushort bx, ushort ds) = ReadAllocationInfo(
            State.DL, State.AH, State.CX, State.DX, State.BX, State.DS);
        State.AX = ax;
        State.CX = cx;
        State.DX = dx;
        State.BX = bx;
        State.DS = ds;
    }

    /// <summary>
    /// INT 21h, AH=1Bh - Get allocation info for the default drive.
    /// Returns AL=0xFF if the current default drive is invalid for FAT allocation queries.
    /// </summary>
    [InterruptService(0x1B, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx | CpuValues.Ds, Memory = ServiceMemoryEffect.ReadWrite)]
    public void GetAllocationInfoForDefaultDrive() {
        (ushort ax, ushort cx, ushort dx, ushort bx, ushort ds) = ReadAllocationInfo(
            0, State.AH, State.CX, State.DX, State.BX, State.DS);
        State.AX = ax;
        State.CX = cx;
        State.DX = dx;
        State.BX = bx;
        State.DS = ds;
    }

    /// <summary>Returns the FAT allocation information for the requested drive
    /// (<paramref name="driveRequest"/> 0 = default, 1 = A:, ...). Only <c>al</c> changes on
    /// failure; all other outputs stay preserved.</summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx, ushort ds) ReadAllocationInfo(
        byte driveRequest, byte previousAh, ushort previousCx, ushort previousDx,
        ushort previousBx, ushort previousDs) {
        ushort ax = (ushort)((previousAh << 8) | 0xFF);
        if (!TryGetAllocationInfoDrive(driveRequest, out byte driveIndex, out _)) {
            if (LoggerService.IsEnabled(LogLevel.Trace)) {
                LoggerService.LogTrace("INT 21h AH=1Bh/1Ch invalid/unmounted drive request {DriveRequest:X2}",
                    driveRequest);
            }
            return (ax, previousCx, previousDx, previousBx, previousDs);
        }

        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace(
                "INT 21h AH=1Bh/1Ch allocation info drive={DriveLetter}: AL(spc)={SectorsPerCluster:X2} CX(bps)={BytesPerSector:X4} DX(totalClusters)={TotalClusters:X4} DS:BX={Segment:X4}:{Offset:X4}",
                (char)('A' + driveIndex), DefaultSectorsPerCluster, DefaultBytesPerSector,
                DefaultTotalClusters, _dosDriveManager.MediaIdTableSegment,
                _dosDriveManager.MediaIdEntryOffset(driveIndex));
        }

        return ((ushort)((previousAh << 8) | DefaultSectorsPerCluster), DefaultBytesPerSector,
            DefaultTotalClusters, _dosDriveManager.MediaIdEntryOffset(driveIndex),
            _dosDriveManager.MediaIdTableSegment);
    }

    private bool TryGetAllocationInfoDrive(byte driveRequest, out byte driveIndex, out DosDriveBase? drive) {
        drive = null;
        if (driveRequest == 0) {
            driveIndex = _dosDriveManager.CurrentDriveIndex;
        } else {
            driveIndex = (byte)(driveRequest - 1);
        }

        if (!_dosDriveManager.TryGetDriveAtIndex(driveIndex, out DosDriveBase? resolvedDrive) ||
            resolvedDrive is EmptyDosDrive) {
            return false;
        }

        drive = resolvedDrive;
        return true;
    }

    /// <summary>
    /// Either gets (AL: 0) or sets (AL: not zero) the country code. <br/>
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x38, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite)]
    public void SetCountryCode(bool calledFromVm) {
        SetStateFromDosFileOperationResult(calledFromVm,
            CountryInformation(State.AL, State.BX, State.DS, State.DX));
    }

    /// <summary>Writes the country information to the buffer at
    /// <paramref name="ds"/>:<paramref name="dx"/> for <paramref name="operation"/> 0; the set
    /// operation is unsupported and returns 0.</summary>
    public DosFileOperationResult CountryInformation(byte operation, ushort bx, ushort ds, ushort dx) {
        switch (operation) {
            case 0: //Get country specific information
                uint dest = MemoryUtils.ToPhysicalAddress(ds, dx);
                Memory.LoadData(dest, BitConverter.GetBytes((ushort)_countryInfo.Country));
                return DosFileOperationResult.Value16((ushort)(bx + 1));
            default: //Set country code
                if (LoggerService.IsEnabled(LogLevel.Warning)) {
                    LoggerService.LogWarning("{MethodName}: subFunction is unsupported", nameof(SetCountryCode));
                }
                return DosFileOperationResult.Value16(0);
        }
    }

    /// <summary>
    /// Allocates a memory block of the requested size in paragraphs in BX. <br/>
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0x08 (Insufficient memory).
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    [InterruptService(0x48, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Bx | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(AllocateMemory), Inputs = [CpuValues.Bx])]
    public void AllocateMemoryBlock(bool calledFromVm) {
        (ushort ax, ushort bx, bool carryFlag) = AllocateMemory(State.BX);
        State.AX = ax;
        State.BX = bx;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Allocates a memory block of <paramref name="paragraphs"/> paragraphs; on success
    /// AX is the block segment and BX the request, on failure AX is the error and BX the largest
    /// free block size.</summary>
    public (ushort ax, ushort bx, bool carryFlag) AllocateMemory(ushort paragraphs) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("ALLOCATE MEMORY BLOCK {RequestedSize}", paragraphs);
        }
        DosMemoryControlBlock? res = _dosMemoryManager.AllocateMemoryBlock(paragraphs);
        if (res == null) {
            // did not find something good, error: INSUFFICIENT MEMORY
            return ((ushort)DosErrorCode.InsufficientMemory,
                _dosMemoryManager.FindLargestFreeSizeForAllocation(), true);
        }
        return (res.DataBlockSegment, paragraphs, false);
    }

    /// <summary>
    /// Changes the current directory to the one pointed by DS:DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    [InterruptService(0x3B, StringArgument = ServiceStringArgument.AsciiZ, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite)]
    public void ChangeCurrentDirectory(bool calledFromVm) {
        SetStateFromDosFileOperationResult(calledFromVm,
            ChangeDirectory(_dosStringDecoder.GetZeroTerminatedStringAtDsDx()));
    }

    /// <summary>Sets the current DOS directory to <paramref name="newDirectory"/>.</summary>
    public DosFileOperationResult ChangeDirectory(string newDirectory) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("SET CURRENT DIRECTORY: {NewDirectory}", newDirectory);
        }
        return _dosFileManager.SetCurrentDir(newDirectory);
    }

    /// <summary>
    /// Clears the keyboard buffer, then calls the INT21H input function whose number is in AL.
    /// DOS only runs functions 01h, 06h, 07h, 08h and 0Ah here; any other AL, 00h included, only flushes.
    /// </summary>
    /// <remarks>
    /// Functions 01h, 07h, 08h and 0Ah wait for input. When the ASM stub is installed, this method moves
    /// CS:IP to the stub's STDIN wait loop instead of running them, so they wait for a key typed after the
    /// flush; <see cref="DispatchStdinInput"/> runs them once STDIN is ready.
    /// </remarks>
    [InterruptService(ClearInputBufferAndReadFunction, Unknown = true)]
    public void ClearKeyboardBufferAndInvokeKeyboardFunction() {
        if (LoggerService.IsEnabled(LogLevel.Debug)) {
            LoggerService.LogDebug("CLEAR KEYBOARD AND CALL INT 21 {Operation}", State.AL);
        }
        _keyboardInt16Handler.FlushKeyboardBuffer();
        // NANSI dosfn7: also clears all device-level stuffahead buffers.
        GetStandardFile(DosStandardHandle.Stdin)?.Flush();
        if (_stdinWaitLoopAddress is SegmentedAddress stdinWaitLoop && FlushFollowUpWaitsForInput(State.AL)) {
            State.IpSegmentedAddress = stdinWaitLoop;
            return;
        }
        RunFlushFollowUp();
    }

    private void RunFlushFollowUp() {
        byte operation = State.AL;
        if (operation is not 0x1 and not 0x6 and not 0x7 and not 0x8 and not 0xA) {
            return;
        }
        Run(operation);
    }

    private static bool FlushFollowUpWaitsForInput(byte operation) {
        return operation is 0x1 or 0x7 or 0x8 or 0xA;
    }

    /// <summary>
    /// Closes a file handle. The handle is in BX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x3E, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(CloseFile), Inputs = [CpuValues.Bx, CpuValues.Ah])]
    public void CloseFileOrDevice(bool calledFromVm) {
        (ushort ax, bool carryFlag) = CloseFile(State.BX, State.AH);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Closes the DOS file <paramref name="fileHandle"/>; on success AL is the
    /// remaining reference count and AH stays <paramref name="previousAh"/>, on error AX is
    /// the error code.</summary>
    public (ushort ax, bool carryFlag) CloseFile(ushort fileHandle, byte previousAh) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("CLOSE FILE handle {FileHandle}", ConvertUtils.ToHex(fileHandle));
        }
        ushort pspSegment = _sda.CurrentProgramSegmentPrefix;
        if (!_jobFileTable.TryResolve(pspSegment, fileHandle, out byte systemFileTableIndex)) {
            return FileResult(DosFileOperationResult.LogError(DosErrorCode.InvalidHandle), 0);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.ReleaseHandleReference(systemFileTableIndex);
        _jobFileTable.SetEntry(pspSegment, fileHandle, DosJobFileTable.UnusedEntry);
        if (dosFileOperationResult.IsError) {
            return FileResult(dosFileOperationResult, 0);
        }
        return ((ushort)((previousAh << 8) | dosFileOperationResult.RefCount), false);
    }

    /// <summary>
    /// INT 21h, AH=68h - Commit buffered file data.
    /// Commits buffered data for the file handle in BX without closing it or changing its position.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x68, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(CommitFile), Inputs = [CpuValues.Bx, CpuValues.Ax])]
    public void CommitFile(bool calledFromVm) {
        (ushort ax, bool carryFlag) = CommitFile(State.BX, State.AX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Commits buffered data for the DOS file <paramref name="fileHandle"/> without
    /// closing it or changing its position; AX stays <paramref name="previousAx"/> on
    /// success.</summary>
    public (ushort ax, bool carryFlag) CommitFile(ushort fileHandle, ushort previousAx) {
        if (!TryResolveFileHandle(fileHandle, out byte systemFileTableIndex)) {
            return FileResult(DosFileOperationResult.Error(DosErrorCode.InvalidHandle), previousAx);
        }
        return FileResult(_dosFileManager.CommitFile(systemFileTableIndex), previousAx);
    }

    /// <summary>
    /// Creates a file using a handle. The file name is in DS:DX, the file attribute in CX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x3C, StringArgument = ServiceStringArgument.AsciiZ, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(CreateFile), Inputs = [CpuValues.Ds | CpuValues.Dx, CpuValues.Cx])]
    public void CreateFileUsingHandle(bool calledFromVm) {
        (ushort ax, bool carryFlag) = CreateFile(
            _dosStringDecoder.GetZeroTerminatedStringAtDsDx(), State.CX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Creates the file <paramref name="fileName"/> with
    /// <paramref name="fileAttribute"/> and returns its DOS handle.</summary>
    public (ushort ax, bool carryFlag) CreateFile(string fileName, ushort fileAttribute) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("CREATE FILE USING HANDLE: {FileName} with attribute {FileAttribute}",
                fileName, fileAttribute);
        }
        return FileResult(AssignJobFileTableHandle(
            _dosFileManager.CreateFileUsingHandle(fileName, fileAttribute)), 0);
    }

    /// <summary>
    /// INT 21h, AH=60h - Get true name.
    /// <para>
    /// Canonicalizes the ASCIIZ path at DS:SI into the fully qualified DOS path and writes it,
    /// zero terminated, to ES:DI. The drive comes from the path or from the current drive; the
    /// relative part is resolved against the current directory of that drive, with "." and ".."
    /// components collapsed. An empty path fails with error 2 (file not found), a drive that is
    /// not mounted with 15 (invalid drive), and the destination buffer is left unchanged on error.
    /// </para>
    /// <b>Returns:</b><br/>
    /// CF=0 and the destination buffer holds the canonical path on success; <br/>
    /// CF=1 and AX holds the error code on failure.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x60, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Si | CpuValues.Es | CpuValues.Di | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite)]
    public void GetTrueNameFromVm(bool calledFromVm) {
        (ushort ax, bool carryFlag) = GetTrueName(
            _dosStringDecoder.GetDosString(State.DS, State.SI, '\0'), State.ES, State.DI);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>
    /// Canonicalizes <paramref name="dosPath"/> and writes the canonical ASCIIZ path to
    /// <paramref name="es"/>:<paramref name="di"/>; AX is 0 on success and the DOS error code on error.
    /// </summary>
    public (ushort ax, bool carryFlag) GetTrueName(string dosPath, ushort es, ushort di) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET TRUE NAME {Path}", dosPath);
        }
        DosFileOperationResult result = _dosFileManager.GetTrueName(dosPath, out string? fullDosPath);
        if (result.IsError || fullDosPath is null) {
            return FileResult(result, 0);
        }
        byte[] bytes = _dosStringDecoder.Encoding.GetBytes(fullDosPath);
        uint address = MemoryUtils.ToPhysicalAddress(es, di);
        for (int i = 0; i < bytes.Length; i++) {
            Memory.UInt8[address + (uint)i] = bytes[i];
        }
        Memory.UInt8[address + (uint)bytes.Length] = 0;
        return (0, false);
    }

    /// <summary>
    /// INT 21h, AH=0Ah - Buffered Input.
    /// <para>
    /// Reads characters from STDIN into a buffer at DS:DX until CR is received (or buffer is full).
    /// Echoes each character to STDOUT. Supports backspace editing.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The in-RAM ASM stub waits for STDIN readiness with INT 28h idle calls,
    /// then dispatches here via <c>callback(Run)</c>.
    /// </remarks>
    [InterruptService(0x0A, Unknown = true)]
    public void BufferedInput() {
        if (CheckCtrlCOrCtrlBreakInKeyboardBuffer()) {
            return;
        }
        uint address = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        DosInputBuffer dosInputBuffer = new DosInputBuffer(Memory, address);
        int charCount = 0;
        bool endedWithCr = false;
        VirtualFileBase? stdin = GetStandardFile(DosStandardHandle.Stdin);
        VirtualFileBase? stdout = GetStandardFile(DosStandardHandle.Stdout);
        if (stdin == null || stdout == null) {
            return;
        }
        dosInputBuffer.Characters = string.Empty;

        while (State.IsRunning) {
            byte[] inputBuffer = new byte[1];
            int bytesRead = stdin.Read(inputBuffer, 0, 1);
            if (bytesRead < 1) {
                break;
            }
            byte c = inputBuffer[0];
            if (c == (byte)AsciiControlCodes.LineFeed) {
                continue;
            }

            if (c == (byte)AsciiControlCodes.Backspace) {
                if (charCount > 0) {
                    stdout.WriteByte(c);
                    stdout.WriteByte((byte)' ');
                    stdout.WriteByte((byte)AsciiControlCodes.Backspace);
                    string chars = dosInputBuffer.Characters;
                    if (chars.Length > 0) {
                        dosInputBuffer.Characters = chars[..^1];
                    }
                    charCount--;
                }
                continue;
            }
            if (charCount >= dosInputBuffer.Length && c != (byte)AsciiControlCodes.CarriageReturn) {
                stdout.WriteByte(7); // bell
                continue;
            }
            stdout.WriteByte(c);
            dosInputBuffer.Characters += (char)c;
            charCount++;
            if (c == (byte)AsciiControlCodes.CarriageReturn) {
                endedWithCr = true;
                break;
            }
        }
        // FreeDOS read_line: kp->kb_count = count - 1 — kb_count never includes the trailing CR.
        dosInputBuffer.ReadCount = (byte)(endedWithCr ? charCount - 1 : charCount);
    }

    /// <summary>
    /// Performs an IO control operation. <br/>
    /// </summary>
    /// <remarks>
    /// Does not check for Ctrl-C or Ctrl-Break <br/>
    /// </remarks>
    /// <returns>
    /// On output, returns the character in AL, despite the docs saying that nothing is returned. <br/>
    /// On input, AL is set to 0 when no input is available, despite this being not documented. <br/>
    /// If there is no keycode pending in the keyboard controller buffer, ZF is cleared and AL is set to 0. <br/>
    /// Otherwise, the Zero flag is cleared and the keycode is in AL.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x06, Reads = CpuValues.Ah | CpuValues.Dl | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al | CpuValues.Zero, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(DirectConsoleIo), Inputs = [CpuValues.Dl, CpuValues.Al, CpuValues.Zero])]
    public void DirectConsoleIo(bool calledFromVm) {
        (byte al, bool zeroFlag) = DirectConsoleIo(
            State.DL, State.AL, ReadZeroFlag(calledFromVm));
        State.AL = al;
        SetZeroFlag(zeroFlag, calledFromVm);
    }

    /// <summary>Reads a character from standard input when <paramref name="character"/> is 0xFF
    /// (returned with the zero flag), otherwise writes it to standard output.</summary>
    public (byte al, bool zeroFlag) DirectConsoleIo(
        byte character, byte previousAl, bool previousZero) {
        if (character == 0xFF) {
            if (LoggerService.IsEnabled(LogLevel.Trace)) {
                LoggerService.LogTrace("DOS INT21H DirectConsoleIo, INPUT REQUESTED");
            }
            VirtualFileBase? stdin = GetStandardFile(DosStandardHandle.Stdin);
            if (stdin != null && _dosFileManager.IsInputAvailable(stdin)) {
                byte[] bytes = new byte[1];
                int readCount = stdin.Read(bytes, 0, 1);
                if (readCount < 1) {
                    return (0, true);
                }
                return (bytes[0], false);
            }
            return (0, true);
        }
        // Output
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("DOS INT21H DirectConsoleIo, OUTPUT REQUESTED: {Character}, {Ascii}",
                character, ConvertUtils.ToChar(character));
        }
        VirtualFileBase? stdout = GetStandardFile(DosStandardHandle.Stdout);
        if (stdout == null) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("DOS INT21H DirectConsoleIo: Cannot write to standard output device.");
            }
            return (previousAl, previousZero);
        }
        ConsoleDevice? console = stdout as ConsoleDevice;
        if (console != null) {
            console.DirectOutput = true;
        }
        stdout.WriteByte(character);
        if (console != null) {
            console.DirectOutput = false;
        }
        return (character, previousZero);
    }

    /// <summary>
    /// Disk Reset. Not implemented. Does nothing.
    /// </summary>
    [InterruptService(0x0D, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.None, Memory = ServiceMemoryEffect.ReadWrite)]
    public void DiskReset() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("DISK RESET (Nothing to do...)");
        }
    }

    /// <summary>
    /// Writes the character from the DL register to the standard output device. <br/>
    /// </summary>
    /// <returns>
    /// AL = DL (the character being written), as per FreeDOS. <br/>
    /// </returns>
    /// <remarks>
    /// FreeDOS equivalent: lr.AL = lr.DL; write_char_stdout(lr.AL); <br/>
    /// Checks for Ctrl-C/Ctrl-Break in STDIN before writing, like FreeDOS check_handle_break.
    /// </remarks>
    [InterruptService(0x02, Reads = CpuValues.Ah | CpuValues.Dl | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite, Io = true)]
    public void DisplayOutput() {
        if (CheckCtrlCOrCtrlBreakInKeyboardBuffer()) {
            return;
        }
        State.AL = WriteDisplayCharacter(State.DL);
    }

    /// <summary>Writes <paramref name="character"/> to the standard output device and returns it.</summary>
    public byte WriteDisplayCharacter(byte character) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            string dosCharacter = _dosStringDecoder.ConvertSingleDosChar(character);
            LoggerService.LogTrace("PRINT CHR: {CharacterByte} ({Character})",
                ConvertUtils.ToHex8(character), dosCharacter);
        }
        WriteCharToStdout(character);
        return character;
    }

    /// <summary>
    /// Writes one character to the standard output handle. <br/>
    /// FreeDOS equivalent of write_char_stdout: routes through the stdout SFT entry.
    /// </summary>
    private void WriteCharToStdout(byte character) {
        VirtualFileBase? stdout = GetStandardFile(DosStandardHandle.Stdout);
        if (stdout != null && stdout.CanWrite) {
            stdout.WriteByte(character);
        }
    }

    /// <summary>
    /// Duplicate a file handle. The handle is in BX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x45, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(DuplicateHandle), Inputs = [CpuValues.Bx])]
    public void DuplicateFileHandle(bool calledFromVm) {
        (ushort ax, bool carryFlag) = DuplicateHandle(State.BX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Duplicates the DOS file <paramref name="fileHandle"/> onto the first free
    /// handle of the current PSP and returns the new handle.</summary>
    public (ushort ax, bool carryFlag) DuplicateHandle(ushort fileHandle) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("DUPLICATE FILE HANDLE. {FileHandle}", fileHandle);
        }
        ushort pspSegment = _sda.CurrentProgramSegmentPrefix;
        if (!_jobFileTable.TryResolve(pspSegment, fileHandle, out byte systemFileTableIndex)) {
            return FileResult(DosFileOperationResult.LogError(DosErrorCode.InvalidHandle), 0);
        }
        if (!_jobFileTable.TryFindFreeHandle(pspSegment, out ushort newHandle)
            || !_dosFileManager.AddHandleReference(systemFileTableIndex)) {
            return FileResult(DosFileOperationResult.LogError(DosErrorCode.TooManyOpenFiles), 0);
        }
        _jobFileTable.SetEntry(pspSegment, newHandle, systemFileTableIndex);
        return (newHandle, false);
    }

    /// <summary>
    /// Force duplicates a file handle. The handle is in BX. The new handle is in CX.
    /// </summary>
    /// <remarks>
    /// When the new handle is already open, it is closed first.
    /// </remarks>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x46, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Cx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ForceDuplicateHandle), Inputs = [CpuValues.Bx, CpuValues.Cx])]
    public void ForceDuplicateFileHandle(bool calledFromVm) {
        (ushort ax, bool carryFlag) = ForceDuplicateHandle(State.BX, State.CX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Duplicates the DOS file <paramref name="fileHandle"/> onto
    /// <paramref name="newHandle"/>, closing the new handle first when it is open.</summary>
    public (ushort ax, bool carryFlag) ForceDuplicateHandle(ushort fileHandle, ushort newHandle) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FORCE DUPLICATE FILE HANDLE. {FileHandle}, {NewHandle}",
                fileHandle, newHandle);
        }
        ushort pspSegment = _sda.CurrentProgramSegmentPrefix;
        if (newHandle == fileHandle || newHandle >= _jobFileTable.GetSize(pspSegment)
            || !_jobFileTable.TryResolve(pspSegment, fileHandle, out byte systemFileTableIndex)) {
            return FileResult(DosFileOperationResult.LogError(DosErrorCode.InvalidHandle), 0);
        }
        if (_jobFileTable.TryResolve(pspSegment, newHandle, out byte replacedSystemFileTableIndex)) {
            _dosFileManager.ReleaseHandleReference(replacedSystemFileTableIndex);
            _jobFileTable.SetEntry(pspSegment, newHandle, DosJobFileTable.UnusedEntry);
        }
        if (!_dosFileManager.AddHandleReference(systemFileTableIndex)) {
            return FileResult(DosFileOperationResult.LogError(DosErrorCode.TooManyOpenFiles), 0);
        }
        _jobFileTable.SetEntry(pspSegment, newHandle, systemFileTableIndex);
        return (newHandle, false);
    }

    private DosFileOperationResult AssignJobFileTableHandle(DosFileOperationResult systemFileTableResult) {
        if (systemFileTableResult.IsError || systemFileTableResult.Value is null) {
            return systemFileTableResult;
        }
        byte systemFileTableIndex = (byte)systemFileTableResult.Value.Value;
        ushort pspSegment = _sda.CurrentProgramSegmentPrefix;
        if (!_jobFileTable.TryFindFreeHandle(pspSegment, out ushort handle)) {
            _dosFileManager.ReleaseHandleReference(systemFileTableIndex);
            return DosFileOperationResult.LogError(DosErrorCode.TooManyOpenFiles);
        }
        _jobFileTable.SetEntry(pspSegment, handle, systemFileTableIndex);
        return DosFileOperationResult.Value16(handle);
    }

    private bool TryResolveFileHandle(ushort fileHandle, out byte systemFileTableIndex) {
        return _jobFileTable.TryResolve(_sda.CurrentProgramSegmentPrefix, fileHandle, out systemFileTableIndex);
    }

    private VirtualFileBase? GetStandardFile(DosStandardHandle standardHandle) {
        if (_sda.CurrentProgramSegmentPrefix == 0) {
            return _dosFileManager.OpenFiles[(ushort)standardHandle];
        }
        if (TryResolveFileHandle((ushort)standardHandle, out byte systemFileTableIndex)) {
            return _dosFileManager.OpenFiles[systemFileTableIndex];
        }
        return null;
    }

    /// <summary>
    /// Finds the first file matching the DOS file spec pointed by DS:DX and the attributes in CX. <br/>
    /// </summary>
    /// <returns>
    /// CF and AX are cleared on success. <br/>
    /// CF is set on error. <br/>
    /// The matching file is returned in the Disk Transfer Area.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x4E, StringArgument = ServiceStringArgument.AsciiZ, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(FindFirst), Inputs = [CpuValues.Ds | CpuValues.Dx, CpuValues.Cx])]
    public void FindFirstMatchingFile(bool calledFromVm) {
        (ushort ax, bool carryFlag) = FindFirst(
            _dosStringDecoder.GetZeroTerminatedStringAtDsDx(), State.CX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Finds the first file matching <paramref name="fileSpec"/> with
    /// <paramref name="attributes"/>; the match is written to the Disk Transfer Area. AX is 0
    /// on success (undocumented behaviour expected by Qbix and Willy Beamish), the error code
    /// on failure.</summary>
    public (ushort ax, bool carryFlag) FindFirst(string fileSpec, ushort attributes) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FIND FIRST MATCHING FILE {Attributes}, {FileSpec}",
                ConvertUtils.ToHex16(attributes), fileSpec);
        }
        DosFileOperationResult result = _dosFileManager.FindFirstMatchingFile(fileSpec, attributes);
        return (result.IsError ? (ushort)result.Value.GetValueOrDefault() : (ushort)0,
            result.IsError);
    }

    /// <summary>
    /// Finds the next file matching the DOS file spec given to <see cref="FindFirstMatchingFile"/>. <br/>
    /// </summary>
    /// <returns>
    /// CF and AX are cleared on success. <br/>
    /// CF is set on error. <br/>
    /// The matching file is returned in the Disk Transfer Area.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    [InterruptService(0x4F, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(FindNext), Inputs = [])]
    public void FindNextMatchingFile(bool calledFromVm) {
        (ushort ax, bool carryFlag) = FindNext();
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Finds the next file matching the file spec given to <see cref="FindFirst"/>;
    /// the match is written to the Disk Transfer Area. AX is 0 on success (undocumented
    /// behaviour expected by Qbix and Willy Beamish), the error code on failure.</summary>
    public (ushort ax, bool carryFlag) FindNext() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FIND NEXT MATCHING FILE");
        }
        DosFileOperationResult result = _dosFileManager.FindNextMatchingFile();
        return (result.IsError ? (ushort)result.Value.GetValueOrDefault() : (ushort)0,
            result.IsError);
    }

    /// <summary>
    /// Free a memory block identified by the block segment in ES. <br/>
    /// </summary>
    /// <returns>
    /// CF and AX are cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0x09 (Invalid memory block address). <br/>
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    [InterruptService(0x49, Reads = CpuValues.Ah | CpuValues.Es | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(FreeMemory), Inputs = [CpuValues.Es, CpuValues.Ax])]
    public void FreeMemoryBlock(bool calledFromVm) {
        (ushort ax, bool carryFlag) = FreeMemory(State.ES, State.AX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Frees the memory block whose segment is <paramref name="blockSegment"/>;
    /// AX stays <paramref name="previousAx"/> on success.</summary>
    public (ushort ax, bool carryFlag) FreeMemory(ushort blockSegment, ushort previousAx) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FREE ALLOCATED MEMORY {BlockSegment}",
                ConvertUtils.ToHex16(blockSegment));
        }
        if (!_dosMemoryManager.FreeMemoryBlock((ushort)(blockSegment - 1))) {
            return FileResult(DosFileOperationResult.LogError(DosErrorCode.MemoryBlockAddressInvalid),
                previousAx);
        }
        return (previousAx, false);
    }

    /// <summary>
    /// Sets the current Program Segment Prefix (PSP) segment. Function 50H. <br/>
    /// </summary>
    /// <remarks>
    /// Input: BX = new PSP segment value. <br/>
    /// Used by (for example) Day of the Tentacle.
    /// </remarks>
    /// <returns>
    /// None.
    /// </returns>
    [InterruptService(0x50, Reads = CpuValues.Ah | CpuValues.Bx,
        Memory = ServiceMemoryEffect.Write,
        Implementation = nameof(SetCurrentPsp), Inputs = [CpuValues.Bx])]
    public void SetCurrentPsp() {
        SetCurrentPsp(State.BX);
    }

    /// <summary>Sets the current Program Segment Prefix segment to <paramref name="segment"/>.</summary>
    public void SetCurrentPsp(ushort segment) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("SET CURRENT PSP: {PspSegment}", ConvertUtils.ToHex16(segment));
        }
        _sda.CurrentProgramSegmentPrefix = segment;
    }

    /// <summary>
    /// Gets the current default drive
    /// </summary>
    /// <returns>
    /// AL = current default drive (0x0: A:, 0x1: B:, 0x2: C:, 0x3: D:, ...)
    /// </returns>
    [InterruptService(0x19, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadCurrentDefaultDrive), Inputs = [])]
    public void GetCurrentDefaultDrive() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET CURRENT DEFAULT DRIVE");
        }
        State.AL = ReadCurrentDefaultDrive();
    }

    /// <summary>Returns the current default drive index (0 = A:, 1 = B:, ...).</summary>
    public byte ReadCurrentDefaultDrive() => _dosDriveManager.CurrentDriveIndex;

    /// <summary>
    /// Gets the current data from the host's DateTime.Now.
    /// </summary>
    /// <returns>
    /// AL = day of the week <br/>
    /// CX = year <br/>
    /// DH = month <br/>
    /// DL = day <br/>
    /// </returns>
    [InterruptService(0x2A, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Cx | CpuValues.Dx, Memory = ServiceMemoryEffect.ReadWrite, Io = true,
        Implementation = nameof(ReadDate), Inputs = [CpuValues.Ah])]
    public void GetDate() {
        (ushort ax, ushort cx, ushort dx) = ReadDate(State.AH);
        State.AX = ax;
        State.CX = cx;
        State.DX = dx;
    }

    /// <summary>Returns the DOS date (day of week, year, month, day) from the CMOS registers;
    /// AH stays <paramref name="previousAh"/>.</summary>
    public (ushort ax, ushort cx, ushort dx) ReadDate(byte previousAh) {
        byte yearBcd = ReadCmosRegister(CmosRegisterAddresses.Year);
        byte monthBcd = ReadCmosRegister(CmosRegisterAddresses.Month);
        byte dayBcd = ReadCmosRegister(CmosRegisterAddresses.DayOfMonth);
        byte dayOfWeekBcd = ReadCmosRegister(CmosRegisterAddresses.DayOfWeek);
        byte centuryBcd = ReadCmosRegister(CmosRegisterAddresses.Century);

        int year = BcdConverter.FromBcd(yearBcd);
        int century = BcdConverter.FromBcd(centuryBcd);
        int month = BcdConverter.FromBcd(monthBcd);
        int day = BcdConverter.FromBcd(dayBcd);
        int dayOfWeek = BcdConverter.FromBcd(dayOfWeekBcd);

        int fullYear = century * 100 + year;

        int dosDayOfWeek;
        if (dayOfWeek == 0) {
            dosDayOfWeek = 0;
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("CMOS DayOfWeek register returned 0 (invalid). Defaulting DOS day of week to Sunday (0).");
            }
        } else {
            dosDayOfWeek = dayOfWeek - 1;
        }

        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET DOS DATE from CMOS: {Year}-{Month:D2}-{Day:D2} (day of week: {DayOfWeek})",
                fullYear, month, day, dosDayOfWeek);
        }
        return ((ushort)((previousAh << 8) | (byte)dosDayOfWeek), (ushort)fullYear,
            (ushort)((month << 8) | day));
    }

    /// <summary>
    /// Gets the address of the DTA.
    /// </summary>
    [InterruptService(0x2F, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Bx | CpuValues.Es, Memory = ServiceMemoryEffect.ReadWrite)]
    public void GetDiskTransferAddress() {
        SegmentedAddress address = ReadDiskTransferAddress();
        State.ES = address.Segment;
        State.BX = address.Offset;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET DTA (DISK TRANSFER ADDRESS) DS:DX {DsDx}",
                ConvertUtils.ToSegmentedAddressRepresentation(address.Segment, address.Offset));
        }
    }

    /// <summary>Returns the Disk Transfer Area address.</summary>
    public SegmentedAddress ReadDiskTransferAddress() => new(
        _dosFileManager.DiskTransferAreaAddressSegment,
        _dosFileManager.DiskTransferAreaAddressOffset);

    /// <summary>
    /// Returns the major, minor, and OEM version of MS-DOS.
    /// </summary>
    [InterruptService(0x30, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Bx | CpuValues.Cx, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadDosVersion))]
    public void GetDosVersion() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET DOS VERSION");
        }
        (ushort ax, ushort cx, ushort bx) = ReadDosVersion();
        State.AX = ax;
        State.CX = cx;
        State.BX = bx;
    }

    /// <summary>Returns the reported DOS version: AX = 5.0, CX = 0, BX = 0xFF00 (MS-DOS OEM).</summary>
    public (ushort ax, ushort cx, ushort bx) ReadDosVersion() =>
        // 5.0 in AX; BH = 0xFF => MS DOS, BL = 0 => OEM key 0
        (0x0005, 0x0000, 0xFF00);

    /// <summary>
    /// INT 21h, AH=31h - Terminate and Stay Resident.
    /// Keeps the program in memory and returns control to the parent process.
    /// </summary>
    /// <remarks>
    /// Input: AL = return code, DX = number of paragraphs to keep resident
    /// </remarks>
    [InterruptService(0x31, Unknown = true)]
    private void TerminateAndStayResident() {
        ushort paragraphsToKeep = State.DX;
        byte returnCode = State.AL;

        ushort currentPspSegment = _sda.CurrentProgramSegmentPrefix;

        if (LoggerService.IsEnabled(LogLevel.Information)) {
            LoggerService.LogInformation(
                "TSR: Terminating with return code {ReturnCode}, keeping {Paragraphs} paragraphs at PSP {PspSegment:X4}",
                returnCode, paragraphsToKeep, currentPspSegment);
        }

        DosErrorCode errorCode = _dosMemoryManager.TryModifyBlock(
            currentPspSegment,
            paragraphsToKeep,
            out DosMemoryControlBlock resizedBlock);

        if (errorCode == DosErrorCode.NoError) {
            _dosProcessManager.TrackResidentBlock(resizedBlock);
        }

        // Even if resize fails, we still terminate as a TSR
        if (errorCode != DosErrorCode.NoError && LoggerService.IsEnabled(LogLevel.Warning)) {
            LoggerService.LogWarning(
                "TSR: Failed to resize memory block to {Paragraphs} paragraphs, error: {Error}",
                paragraphsToKeep, errorCode);
        }

        _dosProcessManager.TerminateProcess(returnCode, DosTerminationType.TSR);
    }

    /// <summary>
    /// Returns the amount of free disk space, in clusters, sectors per byte, and number of available clusters.
    /// <remarks>
    /// Always returns 127 sectors per cluster, 512 bytes per sector, 4031 clusters available (~250MB), and 16383 total clusters (~1000MB)
    /// </remarks>
    /// </summary>
    [InterruptService(0x36, Reads = CpuValues.Ah | CpuValues.Dl | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadFreeDiskSpace), Inputs = [CpuValues.Dl])]
    public void GetFreeDiskSpace() {
        (ushort ax, ushort cx, ushort dx, ushort bx) = ReadFreeDiskSpace(State.DL);
        State.AX = ax;
        State.CX = cx;
        State.DX = dx;
        State.BX = bx;
    }

    /// <summary>Returns the reported free disk space for <paramref name="driveNumber"/>:
    /// sectors per cluster, bytes per sector, total clusters and available clusters.</summary>
    public (ushort ax, ushort cx, ushort dx, ushort bx) ReadFreeDiskSpace(byte driveNumber) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET FREE DISK SPACE FOR DRIVE {DriveNumber}",
                driveNumber);
        }
        // 127 sectors per cluster; 512 bytes per sector;
        // 16383 total clusters (~1000MB); 4031 clusters available (~250MB)
        return (0x7F, 0x200, 0x3FFF, 0xFBF);
    }

    /// <inheritdoc/>
    public override byte VectorNumber => 0x21;

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
    [InterruptService(0x35, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Bx | CpuValues.Es, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadInterruptVector), Inputs = [CpuValues.Al])]
    public void GetInterruptVector() {
        (ushort bx, ushort es) = ReadInterruptVector(State.AL);
        State.ES = es;
        State.BX = bx;
    }

    /// <summary>Returns the address stored in the interrupt vector table for
    /// <paramref name="vectorNumber"/>.</summary>
    public (ushort bx, ushort es) ReadInterruptVector(byte vectorNumber) {
        (ushort segment, ushort offset) = _interruptVectorTable[vectorNumber];
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET INTERRUPT VECTOR INT {VectorInt}, got {SegmentedAddress}",
                ConvertUtils.ToHex8(vectorNumber),
                ConvertUtils.ToSegmentedAddressRepresentation(segment, offset));
        }
        return (offset, segment);
    }

    /// <summary>
    /// Gets or sets the DOS switch character and the "availdev" character (INT 21h AH=37h, "DosSwitchChar").
    /// </summary>
    /// <remarks>
    /// The get subfunctions (AL=00h, AL=02h) always return '/' (0x2F) like DOS 5.0 and later, and the
    /// set subfunctions (AL=01h, AL=03h) report success and keep DL. The game 'Iron Blood' calls this
    /// service.
    /// </remarks>
    [InterruptService(0x37, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dl | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al | CpuValues.Dl, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(GetSetSwitchCharacter), Inputs = [CpuValues.Al, CpuValues.Dl])]
    public void GetSetSwitchCharacter() {
        (byte al, byte dl) = GetSetSwitchCharacter(State.AL, State.DL);
        State.AL = al;
        State.DL = dl;
    }

    /// <summary>Handles the switch character operation <paramref name="operation"/>: the get subfunctions
    /// return AL=0 and the switch character '/' in DL, the set subfunctions return AL=0 and keep the
    /// character the caller had in DL, and any other subfunction returns AL=0xFF.</summary>
    public (byte al, byte dl) GetSetSwitchCharacter(byte operation, byte previousDl) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET/SET SWITCH CHARACTER (DosSwitchChar) OPERATION {Operation}",
                ConvertUtils.ToHex8(operation));
        }
        const byte switchCharacter = 0x2F; // '/'
        switch (operation) {
            case 0x00: // Get switch character
                return (0, switchCharacter);
            case 0x01: // Set switch character
                return (0, previousDl);
            case 0x02: // Get availdev character
                return (0, switchCharacter);
            case 0x03: // Set availdev character
                return (0, previousDl);
            default:
                if (LoggerService.IsEnabled(LogLevel.Warning)) {
                    LoggerService.LogWarning("DOS:0x37: call for not supported switchchar");
                }
                return (0xFF, previousDl);
        }
    }

    /// <summary>
    /// Gets the address of the current Program Segment Prefix.
    /// </summary>
    /// <returns>
    /// The segment of the current PSP in BX.
    /// </returns>
    [InterruptService(0x51, Name = "GetCurrentPsp", Reads = CpuValues.Ah, Writes = CpuValues.Bx, Memory = ServiceMemoryEffect.Read,
        Implementation = nameof(ReadPspAddress), Inputs = [])]
    [InterruptService(0x62, Reads = CpuValues.Ah, Writes = CpuValues.Bx, Memory = ServiceMemoryEffect.Read,
        Implementation = nameof(ReadPspAddress), Inputs = [])]
    public void GetPspAddress() {
        State.BX = ReadPspAddress();
    }

    /// <summary>Returns the segment of the current Program Segment Prefix.</summary>
    public ushort ReadPspAddress() {
        ushort pspSegment = _sda.CurrentProgramSegmentPrefix;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET PSP ADDRESS {PspSegment}",
                ConvertUtils.ToHex16(pspSegment));
        }
        return pspSegment;
    }

    /// <summary>
    /// INT 21h, AH=52h - Get List of Lists (SYSVARS).
    /// <para>
    /// Returns a pointer to the DOS internal tables (also known as the "List of Lists" or SYSVARS).
    /// </para>
    /// <remarks>
    /// Like MS-DOS, this actually returns a pointer to the first drive DOS Parameter Block, which is the "official" start of the structure. <br/>
    /// This is used by (for example) the game 'Day of the Tentacle' and 'Indiana Jones and the Fate of Atlantis'.
    /// </remarks>
    /// </summary>
    [InterruptService(0x52, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Bx | CpuValues.Es, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadListOfListsAddress), Inputs = [])]
    private void GetListOfLists() {
        (ushort bx, ushort es) = ReadListOfListsAddress();
        State.ES = es;
        State.BX = bx;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace(
                "GET LIST OF LISTS ES:BX={Es}:{Bx}",
                ConvertUtils.ToHex16(State.ES),
                ConvertUtils.ToHex16(State.BX));
        }
    }

    /// <summary>Returns the address of the DOS internal tables ("List of Lists" / SYSVARS).</summary>
    public (ushort bx, ushort es) ReadListOfListsAddress() =>
        (DosSysVars.OfficialOffset, DosSysVars.Segment);

    /// <summary>
    /// Peeks the keyboard buffer for a pending Ctrl-C (ASCII 0x03) or Ctrl-Break (AX=0).
    /// If found, flushes the buffer and terminates the current process via
    /// <see cref="DosTerminationType.CtrlC"/>.
    /// </summary>
    /// <returns><c>true</c> if Ctrl-C/Ctrl-Break was detected and the process was terminated; otherwise <c>false</c>.</returns>
    private bool CheckCtrlCOrCtrlBreakInKeyboardBuffer() {
        if (_keyboardInt16Handler.TryGetPendingKeyCode(out ushort? keyCode) &&
            ((keyCode.Value & 0xFF) == 0x03 || keyCode.Value == 0x0000)) {
            _keyboardInt16Handler.FlushKeyboardBuffer();
            _dosProcessManager.TerminateProcess(0xFF, DosTerminationType.CtrlC);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets or sets the Ctrl-C flag and other DOS variables (INT 21h AH=33h, "DosVars").
    /// </summary>
    [InterruptService(0x33, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dl | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al | CpuValues.Bl | CpuValues.Bh | CpuValues.Dl | CpuValues.Dh, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(GetSetControlBreak), Inputs = [CpuValues.Al, CpuValues.Dl, CpuValues.Dh, CpuValues.Bx])]
    public void GetSetControlBreak() {
        (byte al, ushort dx, ushort bx) = GetSetControlBreak(State.AL, State.DL, State.DH, State.BX);
        State.AL = al;
        State.DX = dx;
        State.BX = bx;
    }

    /// <summary>Gets or sets the Ctrl-C flag and related DOS variables for
    /// <paramref name="operation"/>; <paramref name="value"/> is the new flag value for the set
    /// subfunctions. Unsupported operations return AL=0xFF and preserve the other outputs.</summary>
    public (byte al, ushort dx, ushort bx) GetSetControlBreak(
        byte operation, byte value, byte previousDh, ushort previousBx) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET/SET CTRL-C FLAG / DOSVARS");
        }
        static ushort Dx(byte dh, byte dl) => (ushort)((dh << 8) | dl);
        switch (operation) {
            case 0x00: // Get Ctrl-C flag
                return (operation, Dx(previousDh, _isCtrlCFlag ? (byte)1 : (byte)0), previousBx);
            case 0x01: // Set Ctrl-C flag
                _isCtrlCFlag = (value & 1) == 1;
                return (operation, Dx(previousDh, _isCtrlCFlag ? (byte)1 : (byte)0), previousBx);
            case 0x02: { // Get/Set extended control break
                byte previous = _isCtrlCFlag ? (byte)1 : (byte)0;
                _isCtrlCFlag = (value & 1) == 1;
                return (operation, Dx(previousDh, previous), previousBx);
            }
            case 0x05: // Get Boot Drive
                return (operation, Dx(previousDh, _dosSysVars.BootDrive), previousBx);
            case 0x06: // Get (real) DOS version
                // BL = major version, BH = minor, DL = revision, DH = version flags
                return (operation, 0x0000, 0x0005);
            default:
                return (0xFF, Dx(previousDh, value), previousBx); // Error, do NOT set carry
        }
    }

    /// <summary>
    /// Gets the address of the InDOS flag.
    /// </summary>
    [InterruptService(0x34, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Bx | CpuValues.Es, Memory = ServiceMemoryEffect.ReadWrite)]
    private void GetInDosFlagAddress() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET InDOS FLAG ADDRESS");
        }
        SegmentedAddress address = ReadInDosFlagAddress();
        State.ES = address.Segment;
        State.BX = address.Offset;
    }

    /// <summary>Returns the address of the InDOS flag in the DOS Swappable Data Area.</summary>
    public SegmentedAddress ReadInDosFlagAddress() =>
        new(DosSwappableDataArea.BaseSegment, DosSwappableDataArea.InDosFlagOffset);

    /// <summary>
    /// Returns the current MS-DOS time in CH (hour), CL (minute), DH (second), and DL (hundredths of a second).
    /// All four come from one reading of the emulated clock (the clock the CMOS RTC also reads),
    /// so they describe the same moment whatever the RTC data mode.
    /// </summary>
    [InterruptService(0x2C, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Cx | CpuValues.Dx, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadTime), Inputs = [])]
    public void GetTime() {
        (ushort cx, ushort dx) = ReadTime();
        State.CX = cx;
        State.DX = dx;
    }

    /// <summary>Returns the DOS time (hour, minute, second, hundredths) from the emulated clock.</summary>
    public (ushort cx, ushort dx) ReadTime() {
        DateTimeOffset now = _emulatedClock.CurrentDateTime;
        byte hour = (byte)now.Hour;
        byte minute = (byte)now.Minute;
        byte second = (byte)now.Second;
        byte hundredths = (byte)(now.Millisecond / 10);

        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET DOS TIME from the emulated clock: {Hour:D2}:{Minute:D2}:{Second:D2}.{Hundredths:D2}",
                hour, minute, second, hundredths);
        }
        return ((ushort)((hour << 8) | minute), (ushort)((second << 8) | hundredths));
    }

    /// <summary>
    /// Modifies a memory block identified by the block segment in ES, <br/>
    /// and sets the new requested size in paragraphs to the value in BX. <br/>
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// On success, AX is set to the segment (ES value).
    /// CF is set on error. The error is in AX. <br/>
    /// Possible error code in AX: 0x08 (Insufficient memory) or 0x09 (MCB block destroyed). <br/>
    /// BX is set to largest free block size in paragraphs on error. <br/>
    /// </returns>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    [InterruptService(0x4A, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Es | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Bx | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ModifyMemory), Inputs = [CpuValues.Es, CpuValues.Bx])]
    public void ModifyMemoryBlock(bool calledFromVm) {
        (ushort ax, ushort bx, bool carryFlag) = ModifyMemory(State.ES, State.BX);
        State.AX = ax;
        State.BX = bx;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Resizes the memory block at <paramref name="blockSegment"/> to
    /// <paramref name="paragraphs"/> paragraphs; on error AX is the code and BX the failed
    /// block's size.</summary>
    public (ushort ax, ushort bx, bool carryFlag) ModifyMemory(
        ushort blockSegment, ushort paragraphs) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("MODIFY MEMORY BLOCK {Size} at {BlockSegment}",
                paragraphs, ConvertUtils.ToHex16(blockSegment));
        }
        DosErrorCode errorCode = _dosMemoryManager.TryModifyBlock(blockSegment,
            paragraphs, out DosMemoryControlBlock mcb);
        if (errorCode == DosErrorCode.NoError) {
            // Undocumented MS-DOS behaviour expected by BRUN45!
            return (blockSegment, paragraphs, false);
        }
        return ((ushort)errorCode, mcb.Size, true);
    }

    /// <summary>
    /// INT 21h, AH=4Bh - EXEC: Load and/or Execute Program.
    /// <para>
    /// <b>Call with:</b><br/>
    /// AH = 4Bh<br/>
    /// AL = 00h (Load and Execute), 01h (Load Only), 03h (Load Overlay)<br/>
    /// DS:DX = ASCIIZ program path<br/>
    /// ES:BX = parameter block
    /// </para>
    /// <para>
    /// <b>Parameter Block (AL=00h/01h):</b><br/>
    /// +00h WORD environment segment (0=inherit)<br/>
    /// +02h DWORD command tail pointer<br/>
    /// +06h DWORD FCB1 pointer<br/>
    /// +0Ah DWORD FCB2 pointer<br/>
    /// AL=01h returns: +0Eh SS, +10h SP, +12h CS, +14h IP
    /// </para>
    /// <para>
    /// <b>Parameter Block (AL=03h):</b><br/>
    /// +00h WORD load segment<br/>
    /// +02h WORD relocation factor
    /// </para>
    /// <para>
    /// <b>Returns:</b><br/>
    /// CF=0: success (AL=00h: child terminated; AL=01h: param block filled; AL=03h: AX=DX=0)<br/>
    /// CF=1: error (AX=02h file not found, 05h access denied, 08h insufficient memory, 0Bh invalid format)
    /// </para>
    /// <para>
    /// <b>Behavior:</b><br/>
    /// AL=00h: Creates child PSP, loads EXE/COM, executes, returns when child terminates via INT 21h/4Ch<br/>
    /// AL=01h: Creates child PSP, loads program, fills param block with CS:IP/SS:SP, returns immediately without execution<br/>
    /// AL=03h: Loads EXE overlay at specified segment with relocation, no PSP creation or execution<br/>
    /// Child PSP inherits file handles (except DoNotInherit/Private), environment, parent's termination address from caller's CS:IP on stack<br/>
    /// EXE: loads at PSP+10h, applies relocations, sets CS:IP/SS:SP from header<br/>
    /// COM: loads at PSP:0100h, sets CS=DS=ES=SS=PSP, IP=0100h, SP=FFFEh<br/>
    /// FCB pointers 0000:0000 and FFFF:FFFF treated as null<br/>
    /// AX/BX return FCB validity code (00FFh=FCB1 invalid, FF00h=FCB2 invalid, 0000h=both valid)<br/>
    /// Parent SS:SP saved; on child termination, restores parent context via modified interrupt frame and IRET to INT 22h vector
    /// </para>
    /// </summary>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    [InterruptService(0x4B, Unknown = true, StringArgument = ServiceStringArgument.AsciiZ)]
    public void LoadAndOrExecute(bool calledFromVm) {
        string programName = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
        DosExecLoadType loadType = (DosExecLoadType)State.AL;
        uint paramBlockAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);

        if (LoggerService.IsEnabled(LogLevel.Information)) {
            LoggerService.LogInformation("INT21H/4B: DS:DX={Ds:X4}:{Dx:X4}, programName=\"{ProgramName}\", loadType={LoadType}",
                State.DS, State.DX, programName, loadType);
        }

        DosExecResult result;

        if (loadType == DosExecLoadType.LoadOverlay) {
            DosExecOverlayParameterBlock overlayParamBlock = new(Memory, paramBlockAddress);
            result = _dosProcessManager.LoadOverlay(programName, overlayParamBlock.LoadSegment, overlayParamBlock.RelocationFactor);
        } else {
            DosExecParameterBlock paramBlock = new(Memory, paramBlockAddress);
            uint cmdTailAddress = MemoryUtils.ToPhysicalAddress(paramBlock.CommandTailSegment, paramBlock.CommandTailOffset);
            DosCommandTail cmdTail = new(Memory, cmdTailAddress);
            string commandTail = cmdTail.Length > 0 ? cmdTail.Command.TrimEnd('\r') : string.Empty;
            result = _dosProcessManager.LoadOrLoadAndExecute(programName, paramBlock, commandTail, loadType, paramBlock.EnvironmentSegment);
        }
        HandleDosExecResult(calledFromVm, result);
    }

    private void HandleDosExecResult(bool calledFromVm, DosExecResult result) {
        if (result.Success) {
            SetCarryFlag(false, calledFromVm);
            // Set AX and DX if the result specifies values (e.g., LoadOverlay returns AX=0, DX=0)
            if (result.AX.HasValue) {
                State.AX = result.AX.Value;
            }
            if (result.DX.HasValue) {
                State.DX = result.DX.Value;
            }
        } else {
            SetCarryFlag(true, calledFromVm);
            State.AX = (ushort)result.ErrorCode;
            LogDosError(calledFromVm);
        }
    }

    public DosExecResult LoadOverlay(string programName, DosExecOverlayParameterBlock overlayParamBlock) {
        DosExecResult result = _dosProcessManager.LoadOverlay(programName, overlayParamBlock.LoadSegment, overlayParamBlock.RelocationFactor);
        HandleDosExecResult(calledFromVm: true, result);
        return result;
    }

    public DosExecResult LoadOnly(string programName, DosExecParameterBlock paramBlock, string? commandTail = "") {
        DosExecResult result = _dosProcessManager.LoadOrLoadAndExecute(programName, paramBlock, commandTail ?? "", DosExecLoadType.LoadOnly, paramBlock.EnvironmentSegment);
        HandleDosExecResult(calledFromVm: true, result);
        return result;
    }

    public DosExecResult LoadAndExecute(string programName, DosExecParameterBlock paramBlock, string? commandTail = "") {
        DosExecResult result = _dosProcessManager.LoadOrLoadAndExecute(programName, paramBlock, commandTail ?? "", DosExecLoadType.LoadAndExecute, paramBlock.EnvironmentSegment);
        HandleDosExecResult(calledFromVm: true, result);
        return result;
    }

    /// <summary>
    /// Moves a file using a DOS handle. AL specifies the origin of the move, BX the file handle, CX:DX the offset.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    [InterruptService(0x42, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Dx | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite, Implementation = nameof(SeekFile), Inputs = [CpuValues.Al, CpuValues.Bx, CpuValues.Cx, CpuValues.Dx])]
    public void MoveFilePointerUsingHandle(bool calledFromVm) {
        (ushort ax, ushort dx, bool carryFlag) =
            SeekFile((SeekOrigin)State.AL, State.BX, State.CX, State.DX);
        State.AX = ax;
        State.DX = dx;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Moves the file pointer of <paramref name="fileHandle"/> to the offset built from
    /// <paramref name="offsetHigh"/> and <paramref name="offsetLow"/>, relative to
    /// <paramref name="originOfMove"/>, and returns the new DX:AX position and carry flag.</summary>
    public (ushort ax, ushort dx, bool carryFlag) SeekFile(
        SeekOrigin originOfMove, ushort fileHandle, ushort offsetHigh, ushort offsetLow) {
        int offset = (offsetHigh << 16) | offsetLow;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("MOVE FILE POINTER USING HANDLE. {OriginOfMove}, {FileHandle}, {Offset}",
                originOfMove, fileHandle, offset);
        }
        DosFileOperationResult result;
        if (!TryResolveFileHandle(fileHandle, out byte systemFileTableIndex)) {
            result = DosFileOperationResult.LogError(DosErrorCode.InvalidHandle);
        } else {
            result = _dosFileManager.MoveFilePointerUsingHandle(originOfMove, systemFileTableIndex, offset);
        }
        uint value = result.Value.GetValueOrDefault();
        ushort dx = result.IsValueIsUint32 ? (ushort)(value >> 16) : offsetLow;
        return ((ushort)value, dx, result.IsError);
    }

    /// <summary>
    /// Opens the file pointed by DS:DX with the access mode in AL.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    [InterruptService(0x3D, StringArgument = ServiceStringArgument.AsciiZ, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(OpenFile), Inputs = [CpuValues.Al, CpuValues.Ds | CpuValues.Dx])]
    public void OpenFileOrDevice(bool calledFromVm) {
        (ushort ax, bool carryFlag) = OpenFile(
            State.AL, _dosStringDecoder.GetZeroTerminatedStringAtDsDx());
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Opens the file <paramref name="fileName"/> with the raw
    /// <paramref name="accessMode"/> byte and returns its DOS handle.</summary>
    public (ushort ax, bool carryFlag) OpenFile(byte accessMode, string fileName) {
        FileAccessMode fileAccessMode = (FileAccessMode)(accessMode & 0b111);
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("OPEN FILE {FileName} with mode {AccessMode} : {FileAccessModeByte}",
                fileName, fileAccessMode,
                ConvertUtils.ToHex8(accessMode));
        }
        return FileResult(AssignJobFileTableHandle(
            _dosFileManager.OpenFileOrDevice(fileName, fileAccessMode)), 0);
    }

    /// <summary>
    /// Prints a dollar terminated string pointed by DS:DX to the standard output.
    /// </summary>
    [InterruptService(0x09, StringArgument = ServiceStringArgument.DollarTerminated, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.None, Memory = ServiceMemoryEffect.ReadWrite, Io = true)]
    public void PrintString() {
        if (CheckCtrlCOrCtrlBreakInKeyboardBuffer()) {
            return;
        }
        WriteString(_dosStringDecoder.GetDosString(State.DS, State.DX, '$'));
    }

    /// <summary>Writes <paramref name="text"/> to the standard output device.</summary>
    public void WriteString(string text) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("PRINT STRING: {String}", text);
        }
        VirtualFileBase? stdout = GetStandardFile(DosStandardHandle.Stdout);
        if (stdout != null) {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            stdout.Write(bytes, 0, bytes.Length);
        } else if (LoggerService.IsEnabled(LogLevel.Warning)) {
            LoggerService.LogWarning("DOS INT21H PrintString: Cannot write to standard output device.");
        }
    }

    /// <summary>
    /// Legacy CP/M program termination handler (INT 21h, AH=00h).
    /// Behaves like INT 21h, AH=4Ch with AL=0.
    /// </summary>
    [InterruptService(0x00, Unknown = true)]
    public void LegacyTerminateProgram() {
        State.AL = 0;
        State.AH = 0x4C;
        QuitWithExitCode();
    }

    /// <summary>
    /// INT 21h, AH=4Ch - Terminate Program.
    /// Terminates the current process and returns control to the parent via INT 22h.
    /// If Ctrl-C was detected, termination type is set to CtrlC (1) instead of Normal (0).
    /// </summary>
    /// <remarks>
    /// INT 20h is an alias for this function with AL=0.
    /// </remarks>
    [InterruptService(0x4C, Unknown = true)]
    public void QuitWithExitCode() {
        byte exitCode = State.AL;
        // Determine termination type based on break flag
        DosTerminationType terminationType = DosTerminationType.Normal;
        if (_isCtrlCFlag) {
            terminationType = DosTerminationType.CtrlC;
            _isCtrlCFlag = false;

            if (LoggerService.IsEnabled(LogLevel.Information)) {
                LoggerService.LogInformation(
                    "INT21H AH=4Ch: TERMINATE with exit code {ExitCode:X2} (Ctrl-C break detected)",
                    exitCode);
            }
        } else if (LoggerService.IsEnabled(LogLevel.Information)) {
            LoggerService.LogInformation("INT21H AH=4Ch: TERMINATE with exit code {ExitCode:X2}", exitCode);
        }

        _dosProcessManager.TerminateProcess(exitCode, terminationType);
    }

    /// <summary>
    /// INT 21h, AH=4Dh - Get Return Code of Subprogram.
    /// </summary>
    [InterruptService(0x4D, Reads = CpuValues.Ah | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadReturnCode), Inputs = [])]
    public void GetReturnCode() {
        State.AX = ReadReturnCode();
    }

    /// <summary>Returns the exit code of the last child process and clears the stored code (RBIL).</summary>
    public ushort ReadReturnCode() {
        ushort returnCode = _dosProcessManager.LastChildReturnCode;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET RETURN CODE: AX={Ax:X4}", returnCode);
        }
        // Clear the stored return code after reading it (RBIL)
        _dosProcessManager.LastChildReturnCode = 0;
        return returnCode;
    }

    /// <summary>
    /// INT 21h, AH=26h - Create New PSP.
    /// Creates a copy of the current PSP at the segment specified in DX.
    /// </summary>
    [InterruptService(0x26, Reads = CpuValues.Ah | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    public void CreateNewPsp() {
        State.AL = CreateNewPsp(State.DX);
    }

    /// <summary>Creates a copy of the current PSP at <paramref name="segment"/>.</summary>
    public byte CreateNewPsp(ushort segment) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("CREATE NEW PSP at segment {Segment:X4}", segment);
        }

        _dosProcessManager.CreateNewPsp(segment);
        return ExpectedValueOfALInCreateChildPsp;
    }

    /// <summary>
    /// INT 21h, AH=55h - Create Child PSP.
    /// Creates a child PSP at DX with size SI paragraphs, makes it the current PSP, and returns AL=0xF0.
    /// </summary>
    [InterruptService(0x55, Reads = CpuValues.Ah | CpuValues.Dx | CpuValues.Si | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    public void CreateChildPsp() {
        State.AL = CreateChildPsp(State.DX, State.SI);
    }

    /// <summary>Creates a child PSP at <paramref name="segment"/> with the size
    /// <paramref name="sizeInParagraphs"/> and makes it the current PSP.</summary>
    public byte CreateChildPsp(ushort segment, ushort sizeInParagraphs) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("CREATE CHILD PSP at segment {Segment:X4}, size {Size} paragraphs",
                segment, sizeInParagraphs);
        }

        _dosProcessManager.CreateChildPsp(segment, sizeInParagraphs);
        _sda.CurrentProgramSegmentPrefix = segment;
        return ExpectedValueOfALInCreateChildPsp;
    }

    /// <summary>
    /// Subfunction ID in AL for <see cref="AllocationStrategyOrUpperMemoryLinkState"/>
    /// </summary>
    public enum AllocationStrategySubFunction : byte {
        /// <summary>
        /// Gets the current strategy in AX
        /// </summary>
        QueryMemoryAllocationStrategy = 0x0,
        /// <summary>
        /// Sets the current strategy from BX
        /// </summary>
        SetMemoryAllocationStrategy = 0x1,
        /// <summary>
        /// Gets whether the upper memory block is currently linked in AL (1) or not (0).
        /// </summary>
        QueryUpperMemoryBlockState = 0x2,
        /// <summary>
        /// Set the state of the upper memory block link.
        /// </summary>
        SetUpperMemoryBlockState = 0x3,
    }

    /// <summary>
    /// INT 21h, AH=58h
    /// AX=5800H Query Memory Allocation Strategy
    /// AX=5801H Set Memory Allocation Strategy
    /// AX=5802H Query Upper-Memory Link State
    /// AX=5803H Set Upper-Memory Link State
    /// </summary>
    [InterruptService(0x58, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(MemoryAllocationStrategy), Inputs = [CpuValues.Ax, CpuValues.Bx])]
    public void AllocationStrategyOrUpperMemoryLinkState(bool calledFromVm) {
        if (State.AL > (byte)AllocationStrategySubFunction.SetUpperMemoryBlockState) {
            throw GenerateUnhandledOperationException(State.AL);
        }
        (ushort ax, bool carryFlag) = MemoryAllocationStrategy(State.AX, State.BX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Queries or sets the memory allocation strategy or the upper-memory link state for
    /// the low byte of <paramref name="ax"/>; <paramref name="value"/> is the new strategy or link
    /// state for the set subfunctions. Query results merge into the input AX.</summary>
    public (ushort ax, bool carryFlag) MemoryAllocationStrategy(ushort ax, ushort value) {
        switch ((byte)ax) {
            case (byte)AllocationStrategySubFunction.QueryMemoryAllocationStrategy:
                return (_dosMemoryManager.AllocationStrategyValue, false);
            case (byte)AllocationStrategySubFunction.SetMemoryAllocationStrategy:
                bool setStrategy = _dosMemoryManager.TrySetAllocationStrategy(value);
                return (setStrategy ? (ushort)0 : (ushort)DosErrorCode.FunctionNumberInvalid,
                    !setStrategy);
            case (byte)AllocationStrategySubFunction.QueryUpperMemoryBlockState:
                // AL bit 0 is set when the UMB chain is linked to the conventional MCB chain.
                return ((ushort)((ax & 0xFF00) | _dosMemoryManager.UmbChainState), false);
            case (byte)AllocationStrategySubFunction.SetUpperMemoryBlockState:
                bool setLink = _dosMemoryManager.SetUmbChainLinkState(value);
                return (setLink ? ax : (ushort)DosErrorCode.FunctionNumberInvalid, !setLink);
            default:
                throw new ArgumentOutOfRangeException(nameof(ax), ax,
                    "Unsupported memory allocation subfunction");
        }
    }
    /// <summary>
    /// Reads a file from disk from the file handle in BX, the read length in CX, and the buffer at DS:DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    [InterruptService(0x3F, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Cx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(ReadFile), Inputs = [CpuValues.Bx, CpuValues.Cx, CpuValues.Ds, CpuValues.Dx])]
    public void ReadFileOrDevice(bool calledFromVm) {
        (ushort ax, bool carryFlag) = ReadFile(State.BX, State.CX, State.DS, State.DX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Reads <paramref name="readLength"/> bytes from <paramref name="fileHandle"/>
    /// into the buffer at <paramref name="segment"/>:<paramref name="offset"/>.</summary>
    public (ushort ax, bool carryFlag) ReadFile(ushort fileHandle, ushort readLength,
        ushort segment, ushort offset) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("READ FROM FILE handle {FileHandle} length {ReadLength} to {DsDx}",
                fileHandle, readLength,
                ConvertUtils.ToSegmentedAddressRepresentation(segment, offset));
        }
        if (!TryResolveFileHandle(fileHandle, out byte systemFileTableIndex)) {
            return FileResult(DosFileOperationResult.LogError(DosErrorCode.InvalidHandle), 0);
        }
        uint targetMemory = MemoryUtils.ToPhysicalAddress(segment, offset);
        return FileResult(_dosFileManager.ReadFileOrDevice(systemFileTableIndex, readLength,
            targetMemory), 0);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Like MS-DOS, the caller SS:SP is saved into the current PSP (offset 2Eh) on entry, except for the
    /// PSP get/set functions. Process termination restores the parent's stack from this field.
    /// </remarks>
    public override void Run() {
        byte operation = State.AH;
        if (SavesCallerStack(operation)) {
            RecordCallerStack(State.SS, (ushort)(State.SP + 6));
        }
        Run(operation);
    }

    /// <summary>Whether <paramref name="operation"/> enters through the caller-stack save DOS
    /// performs on service entry: every operation except the PSP get/set functions.</summary>
    public static bool SavesCallerStack(byte operation) =>
        operation is not SetCurrentPspFunction and not GetCurrentPspFunction
            and not GetPspAddressFunction;

    /// <summary>Saves <paramref name="ss"/> and the stack pointer <paramref name="callerSp"/> had
    /// before the interrupt pushed its frame into the current PSP's caller-stack field (offset 2Eh),
    /// as DOS does on service entry. The saved pointer addresses the top of the pushed frame, six
    /// bytes below the caller SP.</summary>
    public void RecordCallerStack(ushort ss, ushort callerSp) {
        ushort pspSegment = _sda.CurrentProgramSegmentPrefix;
        if (pspSegment == 0) {
            return;
        }
        DosProgramSegmentPrefix psp = new(Memory, MemoryUtils.ToPhysicalAddress(pspSegment, 0));
        psp.StackPointer = new SegmentedAddress(ss, (ushort)(callerSp - 6));
    }

    /// <summary>
    /// Sets the default drive from the value in the DL register.
    /// </summary>
    /// <returns>
    /// The number of potentially valid drive letters in AL.
    /// </returns>
    [InterruptService(0x0E, Reads = CpuValues.Ah | CpuValues.Dl | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(SelectDefaultDrive), Inputs = [CpuValues.Dl])]
    public void SelectDefaultDrive() {
        State.AL = SelectDefaultDrive(State.DL);
    }

    /// <summary>Makes <paramref name="driveIndex"/> the default drive and returns the number of
    /// potentially valid drive letters.</summary>
    public byte SelectDefaultDrive(byte driveIndex) {
        if (driveIndex < DosDriveManager.MaxDriveCount) {
            if (_dosDriveManager.TryGetDriveAtIndex(driveIndex, out DosDriveBase? mountedDrive)) {
                _dosDriveManager.CurrentDrive = mountedDrive;
            }
        } else if (LoggerService.IsEnabled(LogLevel.Error)) {
            LoggerService.LogError("DOS INT21H: Could not set default drive! Unrecognized index in State.DL: {DriveIndex}", driveIndex);
        }
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("SELECT DEFAULT DRIVE {@DefaultDrive}", _dosDriveManager.CurrentDrive);
        }
        return DosDriveManager.MaxDriveCount;
    }

    /// <summary>
    /// Sets the address of the DTA from DS:DX.
    /// </summary>
    [InterruptService(0x1A, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.None, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(SetDiskTransferAddress), Inputs = [CpuValues.Ds, CpuValues.Dx])]
    public void SetDiskTransferAddress() {
        SetDiskTransferAddress(State.DS, State.DX);
    }

    /// <summary>Sets the Disk Transfer Area address to
    /// <paramref name="segment"/>:<paramref name="offset"/>.</summary>
    public void SetDiskTransferAddress(ushort segment, ushort offset) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("SET DTA (DISK TRANSFER ADDRESS) DS:DX {DsDxSegmentOffset}",
                ConvertUtils.ToSegmentedAddressRepresentation(
                    segment, offset));
        }
        _dosFileManager.SetDiskTransferAreaAddress(segment, offset);
    }

    /// <summary>
    /// Sets a new interrupt vector from an existing one in the Interrupt Vector Table. <br/>
    /// Be sure to call <see cref="GetInterruptVector"/> first to get the current vector address. <br/>
    /// Params: <br/>
    /// - AL: Vector Number <br/>
    /// - DS:DX: New interrupt vector address
    /// </summary>
    [InterruptService(0x25, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.None, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(SetInterruptVector), Inputs = [CpuValues.Al, CpuValues.Ds, CpuValues.Dx])]
    public void SetInterruptVector() {
        SetInterruptVector(State.AL, State.DS, State.DX);
    }

    /// <summary>
    /// Sets a new interrupt vector from an existing one in the Interrupt Vector Table.
    /// </summary>
    /// <param name="vectorNumber">The vector number the new vector will answer to.</param>
    /// <param name="segment">The address of the new interrupt vector, segment part.</param>
    /// <param name="offset">The address of the new interrupt vector, offset part.</param>
    public void SetInterruptVector(byte vectorNumber, ushort segment, ushort offset) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("SET INTERRUPT VECTOR FOR INT {VectorNumber} at address {SegmentOffset}",
                ConvertUtils.ToHex(vectorNumber),
                ConvertUtils.ToSegmentedAddressRepresentation(
                    segment, offset));
        }
        _interruptVectorTable[vectorNumber] = new(segment, offset);
    }

    /// <summary>
    /// Writes a file to disk from the file handle in BX, the file length in CX, and the buffer at DS:DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the called was called by the emulator.</param>
    [InterruptService(0x40, Reads = CpuValues.Ah | CpuValues.Bx | CpuValues.Cx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(WriteFile), Inputs = [CpuValues.Bx, CpuValues.Cx, CpuValues.Ds, CpuValues.Dx])]
    public void WriteToFileOrDevice(bool calledFromVm) {
        (ushort ax, bool carryFlag) = WriteFile(State.BX, State.CX, State.DS, State.DX);
        State.AX = ax;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Writes <paramref name="writeLength"/> bytes to <paramref name="fileHandle"/>
    /// from the buffer at <paramref name="segment"/>:<paramref name="offset"/>.</summary>
    public (ushort ax, bool carryFlag) WriteFile(ushort fileHandle, ushort writeLength,
        ushort segment, ushort offset) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("WRITE TO FILE handle {FileHandle} length {WriteLength} from {DsDx}",
                ConvertUtils.ToHex(fileHandle),
                ConvertUtils.ToHex(writeLength),
                ConvertUtils.ToSegmentedAddressRepresentation(segment, offset));
        }
        if (!TryResolveFileHandle(fileHandle, out byte systemFileTableIndex)) {
            return FileResult(DosFileOperationResult.LogError(DosErrorCode.InvalidHandle), 0);
        }
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        return FileResult(_dosFileManager.WriteToFileOrDevice(systemFileTableIndex, writeLength,
            bufferAddress), 0);
    }

    /// <summary>
    /// Gets an ASCIIZ pathname containing the current DOS directory in the address at DS:DI. <br/>
    /// Params: <br/>
    /// DL = drive number (0x0: A:, 0x1: B:, 0x2: C:, 0x3: D:, ...)
    /// </summary>
    /// <remarks>
    /// Does not include a drive, or the initial backslash
    /// </remarks>
    /// <returns>
    /// DS:DI = ASCIZ pathname containing the current DOS directory. <br/>
    /// CF is cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0xF (Invalid Drive).
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    [InterruptService(0x47, Reads = CpuValues.Ah | CpuValues.Dl | CpuValues.Ds | CpuValues.Si | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite)]
    public void GetCurrentDirectory(bool calledFromVm) {
        SetStateFromDosFileOperationResult(calledFromVm,
            GetCurrentDirectory(State.DL, State.DS, State.SI));
    }

    /// <summary>Writes the ASCIIZ current directory of <paramref name="driveNumber"/> to the buffer
    /// at <paramref name="segment"/>:<paramref name="offset"/>; the result value is 0x0100 on
    /// success.</summary>
    public DosFileOperationResult GetCurrentDirectory(byte driveNumber, ushort segment, ushort offset) {
        uint responseAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        DosFileOperationResult result = _dosFileManager.GetCurrentDir(driveNumber, out string currentDir);
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("GET CURRENT DIRECTORY {ResponseAddress}: {CurrentDpsDirectory}",
                ConvertUtils.ToSegmentedAddressRepresentation(
                    segment, offset), currentDir);
        }
        Memory.SetZeroTerminatedString(responseAddress, currentDir);
        // According to Ralf's Interrupt List, many Microsoft Windows products rely on AX being 0x0100 on success
        return result.IsError ? result : DosFileOperationResult.Value16(0x0100);
    }

    /// <summary>
    /// Gets (AL: 0) or sets (AL: 1) file attributes for the file identified via its file path at DS:DX.
    /// TODO: The Set File Attributes operation is not implemented.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0x2 (File not found). <br/>
    /// TODO: Always returns that the file is read / write in Get File Attributes mode.
    /// </returns>
    /// <exception cref="UnhandledOperationException">When the operation in the AL Register is not supported.</exception>
    /// <param name="calledFromVm">Whether this was called from internal emulator code.</param>
    [InterruptService(0x43, StringArgument = ServiceStringArgument.AsciiZ, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Cx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Cx | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(GetSetFileAttributes), Inputs = [CpuValues.Ax, CpuValues.Ds | CpuValues.Dx, CpuValues.Cx])]
    public void GetSetFileAttributes(bool calledFromVm) {
        byte operation = State.AL;
        (ushort ax, ushort cx, bool carryFlag) = GetSetFileAttributes(
            State.AX, _dosStringDecoder.GetZeroTerminatedStringAtDsDx(), State.CX);
        if (!IsFileAttributeOperationSupported(operation) && carryFlag &&
            ax == (ushort)DosErrorCode.FunctionNumberInvalid) {
            SetCarryFlag(false, calledFromVm);
            throw new UnhandledOperationException(State, "getSetFileAttribute operation unhandled: " + operation);
        }
        State.AX = ax;
        State.CX = cx;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Whether <paramref name="operation"/> is a get/set file attribute operation the
    /// handler completes: an unsupported operation reads the stacked flags and throws after the
    /// host-file lookup, so only these operations may be called directly.</summary>
    public static bool IsFileAttributeOperationSupported(byte operation) => operation is 0 or 1;

    /// <summary>Gets (the low byte of <paramref name="ax"/> 0) or sets (1) the attributes of the
    /// file <paramref name="dosFileName"/>. Get returns the attributes in <c>cx</c>; set takes
    /// them from <paramref name="attributes"/>; AX stays the input value when the operation
    /// returns no value.</summary>
    public (ushort ax, ushort cx, bool carryFlag) GetSetFileAttributes(
        ushort ax, string dosFileName, ushort attributes) {
        byte operation = (byte)ax;
        if (_dosDriveManager.TryGetContentEntry(dosFileName, out DosContentEntry? contentEntry) &&
            contentEntry?.HostPath is null) {
            if (contentEntry is null) {
                return ((ushort)DosErrorCode.FileNotFound, attributes, true);
            }
            if (operation == 0) {
                return (ax, (ushort)contentEntry.Attributes, false);
            }
            return ((ushort)DosErrorCode.AccessDenied, attributes, true);
        }
        string? fileName = _dosFileManager.TryGetFullHostPathFromDos(dosFileName);
        if (!File.Exists(fileName)) {
            return ((ushort)DosErrorCode.FileNotFound, attributes, true);
        }
        switch (operation) {
            case 0: {
                    if (LoggerService.IsEnabled(LogLevel.Trace)) {
                        LoggerService.LogTrace("GET FILE ATTRIBUTE {FileName}", fileName);
                    }
                    FileAttributes fileAttributes = File.GetAttributes(fileName);
                    // .NET FileAttributes bits match DOS FAT bits for ReadOnly(0x01), Hidden(0x02),
                    // System(0x04), Directory(0x10), Archive(0x20). Mask to valid DOS range.
                    return (ax, (ushort)((int)fileAttributes & 0x37), false);
                }
            case 1: {
                    if (LoggerService.IsEnabled(LogLevel.Trace)) {
                        LoggerService.LogTrace("SET FILE ATTRIBUTE {FileName}, {Attribute}",
                            fileName, attributes);
                    }
                    // Preserve Directory bit from current attributes (cannot be changed via this call).
                    FileAttributes current = File.GetAttributes(fileName);
                    FileAttributes preserved = current & FileAttributes.Directory;
                    // Map DOS bits to .NET FileAttributes (same numeric values for the shared flags).
                    FileAttributes newAttrs = (FileAttributes)(attributes & 0x27) | preserved;
                    File.SetAttributes(fileName, newAttrs);
                    return (ax, attributes, false);
                }
            default: return ((ushort)DosErrorCode.FunctionNumberInvalid, attributes, true);
        }
    }

    /// <summary>
    /// Gets (AL: 0) or sets (AL: 1) file DateTime for the file identified via its file path at DS:DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0x1 (function number invalid) 0x6 (invalid handle). <br/>
    /// </returns>
    /// <param name="calledFromVm">Whether this was called from internal emulator code.</param>
    [InterruptService(0x57, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Cx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Cx | CpuValues.Dx | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite)]
    public void GetSetFileDateAndTime(bool calledFromVm) {
        (DosFileOperationResult result, ushort? time, ushort? date) = GetSetFileDateAndTime(
            State.AL, _dosStringDecoder.GetZeroTerminatedStringAtDsDx(), State.CX, State.DX);
        if (time is ushort timeValue) {
            State.CX = timeValue;
        }
        if (date is ushort dateValue) {
            State.DX = dateValue;
        }
        SetStateFromDosFileOperationResult(calledFromVm, result);
    }

    /// <summary>Gets (<paramref name="operation"/> 0) or sets (<paramref name="operation"/> 1) the
    /// last-access date and time of the file <paramref name="dosFileName"/>. Get returns the DOS time
    /// and date; set takes them from <paramref name="dosFileTime"/> and
    /// <paramref name="dosFileDate"/>.</summary>
    public (DosFileOperationResult result, ushort? time, ushort? date) GetSetFileDateAndTime(
        byte operation, string dosFileName, ushort dosFileTime, ushort dosFileDate) {
        if (_dosDriveManager.TryGetContentEntry(dosFileName, out DosContentEntry? contentEntry) &&
            contentEntry?.HostPath is null) {
            if (contentEntry is null) {
                return (DosFileOperationResult.LogError(DosErrorCode.InvalidHandle), null, null);
            }
            if (operation == 0) {
                DateTime dateTime = contentEntry.CreationTimeUtc.ToLocalTime();
                return (DosFileOperationResult.NoValue(),
                    DosFileManager.ToDosTime(dateTime), DosFileManager.ToDosDate(dateTime));
            }
            return (DosFileOperationResult.LogError(DosErrorCode.AccessDenied), null, null);
        }
        string? fileName = _dosFileManager.TryGetFullHostPathFromDos(dosFileName);
        if (!File.Exists(fileName)) {
            return (DosFileOperationResult.LogError(DosErrorCode.InvalidHandle), null, null);
        }
        switch (operation) {
            case 0: {
                    if (LoggerService.IsEnabled(LogLevel.Trace)) {
                        LoggerService.LogTrace("GET FILE DATETIME {FileName}", fileName);
                    }
                    DateTime dateTime = File.GetLastAccessTime(fileName);
                    return (DosFileOperationResult.NoValue(),
                        DosFileManager.ToDosTime(dateTime), DosFileManager.ToDosDate(dateTime));
                }
            case 1: {
                    if (LoggerService.IsEnabled(LogLevel.Trace)) {
                        LoggerService.LogTrace("SET FILE DATETIME {FileName}, {FileTime}, {FileDate}",
                            fileName, dosFileDate, dosFileTime);
                    }
                    DateTime fileDateTime = DateTimeFormDosDateAndTime(dosFileTime, dosFileDate);
                    File.SetLastAccessTime(fileName, fileDateTime);
                    return (DosFileOperationResult.NoValue(), null, null);
                }
            default: {
                    return (DosFileOperationResult.LogError(DosErrorCode.FunctionNumberInvalid), null, null);
                }
        }
    }

    /// <summary>
    /// Converts DOS Date and Time to DateTime
    /// DOS date format: bits 15-9=year-1980, bits 8-5=month, bits 4-0=day.
    /// DOS time format: bits 15-11=hour, bits 10-5=minute, bits 4-0=seconds/2.
    /// </summary>
    public static DateTime DateTimeFormDosDateAndTime(ushort dosDate, ushort dosTime) {
        int day = (dosDate >> 0) & 0b11111;
        int month = (dosDate >> 5) & 0b1111;
        int year = (dosDate >> 9) & 0b1111111 + 1980;

        int seconds = (dosTime >> 0) & 0b11111 * 2;
        int minutes = (dosTime >> 5) & 0b111111;
        int hours = (dosTime >> 11) & 0b11111;

        return new DateTime(year, month, day, hours, minutes, seconds);
    }

    /// <summary>
    /// Gets the FCB linear address from DS:DX.
    /// </summary>
    private uint GetFcbAddress() => MemoryUtils.ToPhysicalAddress(State.DS, State.DX);

    /// <summary>
    /// Gets the DTA address for FCB operations.
    /// </summary>
    private uint GetDtaAddress() => _dosFileManager.GetDiskTransferAreaPhysicalAddress();

    /// <summary>
    /// INT 21h, AH=0Fh - Open File Using FCB.
    /// Opens an existing file using a File Control Block.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an unopened FCB</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if file opened/found, FFh if file not found</para>
    /// </remarks>
    [InterruptService(0x0F, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(FcbOpenFile), Inputs = [CpuValues.Ds, CpuValues.Dx])]
    private void FcbOpenFile() {
        State.AL = FcbOpenFile(State.DS, State.DX);
    }

    /// <summary>Opens the file of the unopened FCB at
    /// <paramref name="segment"/>:<paramref name="offset"/>; returns the FCB status byte.</summary>
    public byte FcbOpenFile(ushort segment, ushort offset) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB OPEN FILE at {Address}", fcbAddress);
        }
        return (byte)_dosFcbManager.OpenFile(fcbAddress);
    }

    /// <summary>
    /// INT 21h, AH=10h - Close File Using FCB.
    /// Closes a file opened with an FCB.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an opened FCB</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if file closed, FFh if file not found</para>
    /// </remarks>
    [InterruptService(0x10, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(FcbCloseFile), Inputs = [CpuValues.Ds, CpuValues.Dx])]
    private void FcbCloseFile() {
        State.AL = FcbCloseFile(State.DS, State.DX);
    }

    /// <summary>Closes the file of the opened FCB at
    /// <paramref name="segment"/>:<paramref name="offset"/>; returns the FCB status byte.</summary>
    public byte FcbCloseFile(ushort segment, ushort offset) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB CLOSE FILE at {Address}", fcbAddress);
        }
        return (byte)_dosFcbManager.CloseFile(fcbAddress);
    }

    /// <summary>
    /// INT 21h, AH=11h - Find First Matching File Using FCB.
    /// Finds the first file matching the FCB file specification.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an unopened FCB (filespec may contain '?'s)</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if a matching filename found (and DTA is filled)</para>
    /// <para>AL = FFh if no match was found</para>
    /// <para>The DTA is filled with the directory entry of the matching file.</para>
    /// </remarks>
    [InterruptService(0x11, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbFindFirst() {
        uint fcbAddress = GetFcbAddress();
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB FIND FIRST at {Address}", fcbAddress);
        }
        State.AL = (byte)_dosFcbManager.FindFirst(fcbAddress);
    }

    /// <summary>
    /// INT 21h, AH=12h - Find Next Matching File Using FCB.
    /// Finds the next file matching the FCB file specification from a previous Find First call.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to the same FCB used in Find First</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if a matching filename found (and DTA is filled)</para>
    /// <para>AL = FFh if no more files match</para>
    /// <para>The reserved area of the FCB carries information used in continuing the search,</para>
    /// <para>so don't open or alter the FCB between calls to Fns 11h and 12h.</para>
    /// </remarks>
    [InterruptService(0x12, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbFindNext() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB FIND NEXT at {Address}", GetFcbAddress());
        }
        State.AL = (byte)_dosFcbManager.FindNext();
    }

    /// <summary>
    /// INT 21h, AH=13h - Delete File Using FCB.
    /// Deletes a file specified by the FCB filename pattern.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an FCB or extended FCB with filename pattern (may contain wildcards)</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h on success, FFh if no files matched or error</para>
    /// </remarks>
    [InterruptService(0x13, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(FcbDeleteFile), Inputs = [CpuValues.Ds, CpuValues.Dx])]
    private void FcbDeleteFile() {
        State.AL = FcbDeleteFile(State.DS, State.DX);
    }

    /// <summary>Deletes the file of the FCB at
    /// <paramref name="segment"/>:<paramref name="offset"/>; returns the FCB status byte.</summary>
    public byte FcbDeleteFile(ushort segment, ushort offset) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB DELETE FILE at {Address}", fcbAddress);
        }
        return (byte)_dosFcbManager.DeleteFile(fcbAddress);
    }

    /// <summary>
    /// INT 21h, AH=17h - Rename File Using FCB.
    /// Renames a file using a special rename FCB structure.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to a rename FCB (special structure with old and new filenames)</para>
    /// <para>Format: drive (1 byte), old name (8 bytes), old ext (3 bytes), reserved (5 bytes),</para>
    /// <para>        new name (8 bytes), new ext (3 bytes), reserved (9 bytes)</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h on success</para>
    /// <para>AL = FFh if file not found or error</para>
    /// <para>Supports wildcards: '?' in new name takes character from old filename</para>
    /// </remarks>
    [InterruptService(0x17, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbRenameFile() {
        uint fcbAddress = GetFcbAddress();
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB RENAME FILE at {Address}", fcbAddress);
        }
        State.AL = (byte)_dosFcbManager.RenameFile(fcbAddress);
    }

    /// <summary>
    /// INT 21h, AH=14h - Sequential Read Using FCB.
    /// Reads the next sequential record from a file.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an opened FCB</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if successful, 01h if EOF and no data read, 02h if segment wrap, 03h if EOF with partial read</para>
    /// </remarks>
    [InterruptService(0x14, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(FcbSequentialRead), Inputs = [CpuValues.Ds, CpuValues.Dx])]
    private void FcbSequentialRead() {
        State.AL = FcbSequentialRead(State.DS, State.DX);
    }

    /// <summary>Reads the next sequential record of the opened FCB at
    /// <paramref name="segment"/>:<paramref name="offset"/> into the DTA; returns the FCB status
    /// byte.</summary>
    public byte FcbSequentialRead(ushort segment, ushort offset) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB SEQUENTIAL READ at {Address}", fcbAddress);
        }
        return (byte)_dosFcbManager.SequentialRead(fcbAddress, GetDtaAddress());
    }

    /// <summary>
    /// INT 21h, AH=15h - Sequential Write Using FCB.
    /// Writes the next sequential record to a file.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an opened FCB</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if successful, 01h if disk full, 02h if segment wrap</para>
    /// </remarks>
    [InterruptService(0x15, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(FcbSequentialWrite), Inputs = [CpuValues.Ds, CpuValues.Dx])]
    private void FcbSequentialWrite() {
        State.AL = FcbSequentialWrite(State.DS, State.DX);
    }

    /// <summary>Writes the next sequential record of the opened FCB at
    /// <paramref name="segment"/>:<paramref name="offset"/> from the DTA; returns the FCB status
    /// byte.</summary>
    public byte FcbSequentialWrite(ushort segment, ushort offset) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB SEQUENTIAL WRITE at {Address}", fcbAddress);
        }
        return (byte)_dosFcbManager.SequentialWrite(fcbAddress, GetDtaAddress());
    }

    /// <summary>
    /// INT 21h, AH=16h - Create File Using FCB.
    /// Creates a new file or truncates an existing file.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an unopened FCB</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if file created, FFh if error</para>
    /// </remarks>
    [InterruptService(0x16, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(FcbCreateFile), Inputs = [CpuValues.Ds, CpuValues.Dx])]
    private void FcbCreateFile() {
        State.AL = FcbCreateFile(State.DS, State.DX);
    }

    /// <summary>Creates the file of the unopened FCB at
    /// <paramref name="segment"/>:<paramref name="offset"/>; returns the FCB status byte.</summary>
    public byte FcbCreateFile(ushort segment, ushort offset) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB CREATE FILE at {Address}", fcbAddress);
        }
        return (byte)_dosFcbManager.CreateFile(fcbAddress);
    }

    /// <summary>
    /// INT 21h, AH=21h - Random Read Using FCB.
    /// Reads a record at the random record position.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an opened FCB</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if successful, 01h if EOF, 02h if segment wrap, 03h if partial read</para>
    /// </remarks>
    [InterruptService(0x21, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbRandomRead() {
        uint fcbAddress = GetFcbAddress();
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB RANDOM READ at {Address}", fcbAddress);
        }
        State.AL = (byte)_dosFcbManager.RandomRead(fcbAddress, GetDtaAddress());
    }

    /// <summary>
    /// INT 21h, AH=22h - Random Write Using FCB.
    /// Writes a record at the random record position.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an opened FCB</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if successful, 01h if disk full, 02h if segment wrap</para>
    /// </remarks>
    [InterruptService(0x22, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbRandomWrite() {
        uint fcbAddress = GetFcbAddress();
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB RANDOM WRITE at {Address}", fcbAddress);
        }
        State.AL = (byte)_dosFcbManager.RandomWrite(fcbAddress, GetDtaAddress());
    }

    /// <summary>
    /// INT 21h, AH=23h - Get File Size Using FCB.
    /// Gets the file size in records and stores it in the FCB random record field.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an FCB with file name and record size set</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if file found (random record field set to file size in records), FFh if file not found</para>
    /// </remarks>
    [InterruptService(0x23, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbGetFileSize() {
        uint fcbAddress = GetFcbAddress();
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB GET FILE SIZE at {Address}", fcbAddress);
        }
        State.AL = (byte)_dosFcbManager.GetFileSize(fcbAddress);
    }

    /// <summary>
    /// INT 21h, AH=24h - Set Random Record Number (FCB).
    /// Sets the <c>RandomRecord</c> field in the FCB from the current block and record numbers.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an FCB (standard or extended with 0xFF header)</para>
    /// <para><b>Operation:</b></para>
    /// <para>Computes `RandomRecord = CurrentBlock * 128 + CurrentRecord` and stores it in the FCB (DWORD at offset 0x21).</para>
    /// <para><b>Notes:</b></para>
    /// <para>Extended FCBs with 0xFF marker are supported; the inner FCB begins at +7 bytes.</para>
    /// </remarks>
    [InterruptService(0x24, Reads = CpuValues.Ah | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.None, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbSetRandomRecordNumber() {
        uint fcbAddress = GetFcbAddress();
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB SET RANDOM RECORD NUMBER at {Address}", fcbAddress);
        }
        _dosFcbManager.UpdateRandomRecord(fcbAddress);
    }

    /// <summary>
    /// INT 21h, AH=27h - Random Block Read Using FCB.
    /// Reads one or more records starting at the random record position.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an opened FCB</para>
    /// <para>CX = number of records to read</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if successful, 01h if EOF, 02h if segment wrap, 03h if partial read</para>
    /// <para>CX = actual number of records read</para>
    /// </remarks>
    [InterruptService(0x27, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al | CpuValues.Cx, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbRandomBlockRead() {
        uint fcbAddress = GetFcbAddress();
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB RANDOM BLOCK READ at {Address}, {Count} records", fcbAddress, State.CX);
        }
        (FcbStatus status, ushort actualRecordCount) = _dosFcbManager.RandomBlockRead(fcbAddress, GetDtaAddress(), State.CX);
        State.AL = (byte)status;
        State.CX = actualRecordCount;
    }

    /// <summary>
    /// INT 21h, AH=28h - Random Block Write Using FCB.
    /// Writes one or more records starting at the random record position.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:DX = pointer to an opened FCB</para>
    /// <para>CX = number of records to write (0 = truncate file to random record position)</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if successful, 01h if disk full, 02h if segment wrap</para>
    /// <para>CX = actual number of records written</para>
    /// </remarks>
    [InterruptService(0x28, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al | CpuValues.Cx, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbRandomBlockWrite() {
        uint fcbAddress = GetFcbAddress();
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB RANDOM BLOCK WRITE at {Address}, {Count} records", fcbAddress, State.CX);
        }
        (FcbStatus status, ushort actualRecordCount) = _dosFcbManager.RandomBlockWrite(fcbAddress, GetDtaAddress(), State.CX);
        State.AL = (byte)status;
        State.CX = actualRecordCount;
    }

    /// <summary>
    /// INT 21h, AH=29h - Parse Filename into FCB.
    /// Parses a filename string into an FCB format.
    /// </summary>
    /// <remarks>
    /// <para><b>Expects:</b></para>
    /// <para>DS:SI = pointer to filename string to parse</para>
    /// <para>ES:DI = pointer to FCB to fill</para>
    /// <para>AL = parsing control byte:</para>
    /// <para>  bit 0: when set, skip leading separators</para>
    /// <para>  bit 1: when clear, set default drive if not specified; when set, do not change drive</para>
    /// <para>  bit 2: when clear, blank filename if not specified; when set, leave filename unchanged</para>
    /// <para>  bit 3: when clear, blank extension if not specified; when set, leave extension unchanged</para>
    /// <para><b>Returns:</b></para>
    /// <para>AL = 00h if no wildcards, 01h if wildcards present, FFh if invalid drive</para>
    /// <para>DS:SI = pointer to first byte after parsed filename</para>
    /// </remarks>
    [InterruptService(0x29, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Ds | CpuValues.Si | CpuValues.Es | CpuValues.Di | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Al | CpuValues.Si, Memory = ServiceMemoryEffect.ReadWrite)]
    private void FcbParseFilename() {
        SegmentedAddress stringAddress = new SegmentedAddress(State.DS, State.SI);
        SegmentedAddress fcbAddress = new SegmentedAddress(State.ES, State.DI);
        FcbParseControl parseControl = (FcbParseControl)State.AL;

        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("FCB PARSE FILENAME from {StringAddress} to {FcbAddress}, control={Control:X2}",
                stringAddress, fcbAddress, parseControl);
        }

        (FcbParseResult parseStatus, uint bytesAdvanced) = _dosFcbManager.ParseFilename(
            MemoryUtils.ToPhysicalAddress(stringAddress.Segment, stringAddress.Offset),
            MemoryUtils.ToPhysicalAddress(fcbAddress.Segment, fcbAddress.Offset),
            parseControl);
        State.AL = (byte)parseStatus;
        State.SI += (ushort)bytesAdvanced;
    }

    /// <summary>
    /// Provides MS-DOS drivers based IOCTL operations, which are device-driver specific operations.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    /// <remarks>
    /// Supports the following operations based on the AL register:
    /// <list type="bullet">
    ///   <item><description>AL = 0x00: Get Device Information from handle in BX. Returns information in DX.</description></item>
    ///   <item><description>AL = 0x01: Set Device Information using handle in BX and data in DL.</description></item>
    ///   <item><description>AL = 0x02: Read from Device Control Channel using handle in BX.</description></item>
    ///   <item><description>AL = 0x03: Write to Device Control Channel using handle in BX.</description></item>
    ///   <item><description>AL = 0x06: Get Input Status for handle in BX. Returns status in AL (0xFF=ready, 0x00=not ready).</description></item>
    ///   <item><description>AL = 0x07: Get Output Status for handle in BX. Returns status in AL (0xFF=ready, 0x00=not ready).</description></item>
    ///   <item><description>AL = 0x08: Check if block device in BL is removable. Returns AL=0 for removable, AL=1 for fixed.</description></item>
    ///   <item><description>AL = 0x09: Check if block device in BL is remote. Returns DX with device attributes.</description></item>
    ///   <item><description>AL = 0x0B: Set sharing retry count. DX=retry count.</description></item>
    ///   <item><description>AL = 0x0D: Generic block device request for drive in BL. Command in CL, parameter block at DS:DX.</description></item>
    ///   <item><description>AL = 0x0E: Get Logical Drive Map for drive in BL. Returns physical drive in AL.</description></item>
    /// </list>
    /// </remarks>
    [InterruptService(0x44, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Bx | CpuValues.Cx | CpuValues.Ds | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp, Writes = CpuValues.Ax | CpuValues.Dx | CpuValues.Carry, Memory = ServiceMemoryEffect.ReadWrite,
        Implementation = nameof(IoControl), Inputs = [CpuValues.Ax, CpuValues.Cx, CpuValues.Dx, CpuValues.Bx, CpuValues.Ds])]
    public void IoControl(bool calledFromVm) {
        (ushort ax, ushort dx, bool carryFlag) =
            IoControl(State.AX, State.CX, State.DX, State.BX, State.DS);
        State.AX = ax;
        State.DX = dx;
        ApplyCarryFlag(calledFromVm, carryFlag);
    }

    /// <summary>Performs the IOCTL operation in the low byte of <paramref name="ax"/> with the
    /// device-control parameters <paramref name="cx"/>, <paramref name="dx"/>,
    /// <paramref name="bx"/> and <paramref name="ds"/>; <paramref name="bx"/> is a DOS file handle
    /// for handle subfunctions and a drive number for the others. An operation result without a
    /// value only merges its AL output into the input AX; a 32-bit result value also returns its
    /// high word in <c>dx</c>.</summary>
    public (ushort ax, ushort dx, bool carryFlag) IoControl(
        ushort ax, ushort cx, ushort dx, ushort bx, ushort ds) {
        byte operation = (byte)ax;
        DosFileOperationResult result;
        byte? al = null;
        ushort resultDx = dx;
        if (DosFileManager.IsIoctlHandleSubfunction(operation)) {
            if (TryResolveFileHandle(bx, out byte systemFileTableIndex)) {
                // Handle subfunctions take a system file table index, not the DOS handle.
                bx = systemFileTableIndex;
            } else {
                result = DosFileOperationResult.Error(DosErrorCode.InvalidHandle);
                return ((ushort)result.Value.GetValueOrDefault(), resultDx, true);
            }
        }
        (result, al, resultDx) = _dosFileManager.IoControl(operation, cx, dx, bx, ds);
        ushort outAx = al is { } alValue ? (ushort)((ax & 0xFF00) | alValue) : ax;
        if (result.Value is { } value) {
            outAx = (ushort)value;
        }
        ushort outDx = resultDx;
        if (result.IsValueIsUint32) {
            outDx = (ushort)(result.Value.GetValueOrDefault() >> 16);
        }
        return (outAx, outDx, result.IsError);
    }

    /// <summary>The file control block manager backing the FCB file services.</summary>
    public DosFcbManager FileControlBlocks => _dosFcbManager;

    /// <summary>The decoder generated code reads DOS string arguments with.</summary>
    public DosStringDecoder StringDecoder => _dosStringDecoder;

    private void ApplyCarryFlag(bool calledFromVm, bool carryFlag) {
        if (carryFlag) {
            LogDosError(calledFromVm);
        }
        SetCarryFlag(carryFlag, calledFromVm);
    }

    private static (ushort ax, bool carryFlag) FileResult(DosFileOperationResult result,
        ushort previousAx) =>
        ((ushort)(result.Value ?? previousAx), result.IsError);

    private void LogDosError(bool calledFromVm, [CallerMemberName] string? callerName = null) {
        string returnMessage = "";
        if (calledFromVm) {
            returnMessage = $"Int will return to {FunctionHandlerProvider.FunctionHandlerInUse.PeekReturn()}. ";
        }
        if (LoggerService.IsEnabled(LogLevel.Error)) {
            LoggerService.LogError("DOS operation from {CallerName} failed with an error. {ReturnMessage}. State is {State}",
                callerName, returnMessage, State.ToString());
        }
    }

    private void SetStateFromDosFileOperationResult(bool calledFromVm,
        DosFileOperationResult dosFileOperationResult, [CallerMemberName] string? callerName = null) {
        (ushort ax, bool carryFlag) = FileResult(dosFileOperationResult, State.AX);
        if (carryFlag) {
            LogDosError(calledFromVm, callerName);
        }
        SetCarryFlag(carryFlag, calledFromVm);
        State.AX = ax;
        if (dosFileOperationResult.IsValueIsUint32) {
            State.DX = (ushort)(dosFileOperationResult.Value.GetValueOrDefault() >> 16);
        }
    }

    /// <summary>
    /// Reads a value from a CMOS register via I/O ports.
    /// Writes the register index to the address port and reads the value from the data port.
    /// </summary>
    private byte ReadCmosRegister(byte register) {
        _ioPortDispatcher.WriteByte(CmosPorts.Address, register);
        return _ioPortDispatcher.ReadByte(CmosPorts.Data);
    }

    /// <summary>
    /// Writes a value to a CMOS register via I/O ports.
    /// Writes the register index to the address port and the value to the data port.
    /// </summary>
    private void WriteCmosRegister(byte register, byte value) {
        _ioPortDispatcher.WriteByte(CmosPorts.Address, register);
        _ioPortDispatcher.WriteByte(CmosPorts.Data, value);
    }
}
