namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.IO;

/// <summary>
/// Represents the console device.
/// </summary>
public class ConsoleDevice : CharacterDevice {
    private const string CON = "CON";
    private byte _readCache = 0;
    public const int InputAvailable = 0x80D3;
    public const int NoInputAvailable = 0x8093;

    private readonly ILoggerService _loggerService;
    private readonly BiosDataArea _biosDataArea;
    private readonly BiosKeyboardBuffer _biosKeybardBuffer;
    private readonly IVgaFunctionality _vgaFunctionality;
    private readonly KeyboardInt16Handler _keyboardInt16Handler;
    private readonly State _state;
    private readonly MemoryAsmWriter _memoryAsmWriter;

    // Global memory bus (needed to emit code and patch the interrupt return frame).
    private readonly IIndexable _memory;

    // ASM thunk that blocks with INT 16h AH=00h, then IRET to original program return address.
    private bool _thunkInitialized;
    private SegmentedAddress _thunkEntry;
    private ushort _savedIpVarOffset;
    private ushort _savedCsVarOffset;
    private ushort _savedFlagsVarOffset;

    private readonly Ansi _ansi = new Ansi();
    private class Ansi {
        public const int ANSI_DATA_LENGTH = 10;
        public bool Esc { get; set; }
        public bool Sci { get; set; }
        public bool IsEnabled { get; set; }
        public byte Attribute { get; set; } = 0x7;
        public byte[] Data { get; } = new byte[ANSI_DATA_LENGTH];
        public byte NumberOfArg { get; set; }
        public sbyte SaveColumn { get; set; }
        public sbyte SaveRow { get; set; }
        public bool WasWarned { get; set; }
    }

    /// <summary>
    /// Create a new console device.
    /// </summary>
    public ConsoleDevice(IMemory memory, uint baseAddress,
        ILoggerService loggerService, State state, BiosDataArea biosDataArea,
        KeyboardInt16Handler keyboardInt16Handler, IVgaFunctionality vgaFunctionality,
        BiosKeyboardBuffer biosKeyboardBuffer, MemoryAsmWriter memoryAsmWriter)
        : base(memory, baseAddress, CON,
            DeviceAttributes.CurrentStdin | DeviceAttributes.CurrentStdout) {

        _loggerService = loggerService;
        _biosKeybardBuffer = biosKeyboardBuffer;
        _keyboardInt16Handler = keyboardInt16Handler;
        _state = state;
        _biosDataArea = biosDataArea;
        _vgaFunctionality = vgaFunctionality;
        _memory = memory;
        _memoryAsmWriter = memoryAsmWriter;

        vgaFunctionality.VideoModeChanged += OnVideModeChanged;
        _currentMode = vgaFunctionality.GetCurrentMode();
    }

    private VgaMode _currentMode;

    private void OnVideModeChanged(object? sender, VideoModeChangedEventArgs e) {
        _currentMode = e.NewMode;
    }

    private bool GetIsInTextMode() {
        return _currentMode.MemoryModel == MemoryModel.Text;
    }

    public bool InternalOutput { get; set; }

    public bool Echo { get; set; } = true;

    public bool DirectOutput { get; set; }

    public override string Name => CON;

    public override bool CanSeek => false;

    public override bool CanRead => _biosKeybardBuffer.IsEmpty is false;

    public override bool CanWrite => true;

    public override void Flush() {
        // No operation needed for console device flush
    }

    public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException("Console device does not support seeking.");
    }

    public override void SetLength(long value) {
        throw new NotSupportedException();
    }

    public override long Length => throw new NotSupportedException("Console device does not have a length.");

    public override long Position {
        get => throw new NotSupportedException("Console device does not support getting position.");
        set => throw new NotSupportedException("Console device does not support setting position.");
    }

    // inspired by the mouse driver - and a solution to remove emulationLoopRecall class:
    // Call a function that we wait on - except it's our function.
    // Layout:
    //   [savedIP: dw 0][savedCS: dw 0][savedFLAGS: dw 0]
    //   entry:
    //     sti
    //     push bx / push cx / push dx / push si / push di / push bp      ; save regs (AX intentionally not saved)
    //     push cs
    //     pop  ds                                                         ; DS = CS for local data
    //     mov  ah,00
    //     int  16h                                                        ; busy-wait handled by INT16h handler ASM
    //     pop  bp / pop di / pop si / pop dx / pop cx / pop bx           ; restore regs (AX holds key, do not restore)
    //     mov  ax,[savedFLAGS]  ; push flags, cs, ip for final IRET
    //     push ax
    //     mov  ax,[savedCS]
    //     push ax
    //     mov  ax,[savedIP]
    //     push ax
    //     iret
    private void EnsureBlockingThunk() {
        if (_thunkInitialized) {
            return;
        }

        // Place thunk  high in DOS device drivers segment
        var begin = new SegmentedAddress(MemoryMap.DeviceDriversSegment, 0xFF00);
        MemoryAsmWriter asmWriter = _memoryAsmWriter;
        SegmentedAddress savedAddress = asmWriter.CurrentAddress;
        asmWriter.CurrentAddress = begin;

        // Reserve variables first so their offsets are known.
        _savedIpVarOffset = asmWriter.CurrentAddress.Offset; asmWriter.WriteUInt16(0);
        _savedCsVarOffset = asmWriter.CurrentAddress.Offset; asmWriter.WriteUInt16(0);
        _savedFlagsVarOffset = asmWriter.CurrentAddress.Offset; asmWriter.WriteUInt16(0);

        _thunkEntry = asmWriter.CurrentAddress;

        asmWriter.WriteSti();

        // push bx, push cx, push dx, push si, push di, push bp
        asmWriter.WriteUInt8(0x53); // push bx
        asmWriter.WriteUInt8(0x51); // push cx
        asmWriter.WriteUInt8(0x52); // push dx
        asmWriter.WriteUInt8(0x56); // push si
        asmWriter.WriteUInt8(0x57); // push di
        asmWriter.WriteUInt8(0x55); // push bp

        // DS := CS  (push cs; pop ds)
        asmWriter.WriteUInt8(0x0E); // push cs
        asmWriter.WriteUInt8(0x1F); // pop ds

        // mov ah,00
        asmWriter.WriteUInt8(0xB4); asmWriter.WriteUInt8(0x00);

        // int 16h
        asmWriter.WriteUInt8(0xCD); asmWriter.WriteUInt8(0x16);

        // pop bp, pop di, pop si, pop dx, pop cx, pop bx
        asmWriter.WriteUInt8(0x5D); // pop bp
        asmWriter.WriteUInt8(0x5F); // pop di
        asmWriter.WriteUInt8(0x5E); // pop si
        asmWriter.WriteUInt8(0x5A); // pop dx
        asmWriter.WriteUInt8(0x59); // pop cx
        asmWriter.WriteUInt8(0x5B); // pop bx

        // push saved FLAGS, CS, IP (via AX loads) then IRET
        asmWriter.WriteMemoryAddressToAx(_savedFlagsVarOffset); asmWriter.WriteUInt8(0x50); // mov ax,[imm16]; push ax
        asmWriter.WriteMemoryAddressToAx(_savedCsVarOffset);    asmWriter.WriteUInt8(0x50);
        asmWriter.WriteMemoryAddressToAx(_savedIpVarOffset);    asmWriter.WriteUInt8(0x50);

        asmWriter.WriteIret();

        _thunkInitialized = true;
        asmWriter.CurrentAddress = savedAddress;
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (count == 0 || offset > buffer.Length || buffer.Length == 0) {
            return 0;
        }

        // Preserve caller AX while we interact with the BIOS buffer.
        ushort oldAx = _state.AX;

        int index = offset;
        int readCount = 0;

        // Emit pending second byte of a previously returned extended key.
        if ((_readCache > 0) && (buffer.Length > 0)) {
            buffer[index++] = _readCache;
            if (Echo) {
                OutputWithNoAttributes(_readCache);
            }
            _readCache = 0;
            readCount++;
            if (readCount >= count) {
                _state.AX = oldAx;
                return readCount;
            }
        }

        // Fast path: drain BIOS buffer now (non-blocking).
        while (index < buffer.Length && readCount < count) {
            ushort? keyCode = _biosKeybardBuffer.PeekKeyCode();
            if (keyCode is null) {
                break;
            }

            _state.AX = keyCode.Value;
            _biosKeybardBuffer.DequeueKeyCode();

            byte ascii = _state.AL;
            byte scan = _state.AH;

            switch (ascii) {
                case (byte)AsciiControlCodes.CarriageReturn: {
                    buffer[index++] = (byte)AsciiControlCodes.CarriageReturn;
                    readCount++;
                    if (index < buffer.Length && readCount < count) {
                        buffer[index++] = (byte)AsciiControlCodes.LineFeed;
                        readCount++;
                    }
                    if (Echo) {
                        OutputWithNoAttributes(AsciiControlCodes.LineFeed);
                        OutputWithNoAttributes(AsciiControlCodes.CarriageReturn);
                    }
                    break;
                }
                case (byte)AsciiControlCodes.Backspace: {
                    if (buffer.Length == 1) {
                        buffer[index++] = ascii;
                        readCount++;
                    } else if (index > offset) {
                        buffer[--index] = 0;
                        readCount--;
                        OutputWithNoAttributes(AsciiControlCodes.Backspace);
                        OutputWithNoAttributes(' ');
                        OutputWithNoAttributes(AsciiControlCodes.Backspace);
                    } else {
                        continue;
                    }
                    break;
                }
                case (byte)AsciiControlCodes.Extended:
                    if (_state.AH != 0) {
                        buffer[index++] = ascii;
                        readCount++;
                    } else {
                        buffer[index++] = 0;
                        readCount++;
                        if (index < buffer.Length && readCount < count) {
                            buffer[index++] = scan;
                            readCount++;
                        } else {
                            _readCache = scan;
                        }
                    }
                    break;
                case (byte)AsciiControlCodes.Null:
                    buffer[index++] = 0;
                    readCount++;
                    if (index < buffer.Length && readCount < count) {
                        buffer[index++] = scan;
                        readCount++;
                    } else {
                        _readCache = scan;
                    }
                    break;
                default:
                    buffer[index++] = ascii;
                    readCount++;
                    break;
            }

            if (Echo) {
                OutputWithNoAttributes(ascii);
            }
        }

        // If we already read something, return it.
        if (readCount > 0) {
            _state.AX = oldAx;
            return readCount;
        }

        // BIOS buffer empty: install one-shot trampoline to our blocking thunk.
        // On IRET from INT 21h, control will go to the thunk, which does INT 16h AH=00h,
        // restores registers, and IRETs to the original program return address with AL set.
        EnsureBlockingThunk();

        uint sp = _state.StackPhysicalAddress;

        // Interrupt frame layout (at SS:SP):
        // [0] = IP (word), [2] = CS (word), [4] = FLAGS (word)
        ushort origIP = BitConverter.ToUInt16(_memory.GetData(sp + 0, 2), 0);
        ushort origCS = BitConverter.ToUInt16(_memory.GetData(sp + 2, 2), 0);
        ushort origFL = BitConverter.ToUInt16(_memory.GetData(sp + 4, 2), 0);

        // Save original return triplet next to the thunk (in its own code segment).
        uint savedIpPhys = MemoryUtils.ToPhysicalAddress(_thunkEntry.Segment, _savedIpVarOffset);
        uint savedCsPhys = MemoryUtils.ToPhysicalAddress(_thunkEntry.Segment, _savedCsVarOffset);
        uint savedFlPhys = MemoryUtils.ToPhysicalAddress(_thunkEntry.Segment, _savedFlagsVarOffset);

        _memory.LoadData(savedIpPhys, BitConverter.GetBytes(origIP));
        _memory.LoadData(savedCsPhys, BitConverter.GetBytes(origCS));
        _memory.LoadData(savedFlPhys, BitConverter.GetBytes(origFL));

        // Hijack INT 21h IRET to go to the thunk.
        _memory.LoadData(sp + 0, BitConverter.GetBytes(_thunkEntry.Offset));
        _memory.LoadData(sp + 2, BitConverter.GetBytes(_thunkEntry.Segment));
        // Keep FLAGS as-is; thunk does STI anyway.

        // Do not block here; return 0 to the caller of Read.
        // The thunk will produce AL for the program when INT 21h IRETs.
        _state.AX = oldAx;
        return 0;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        byte page = _biosDataArea.CurrentVideoPage;
        byte col, row;
        ushort ncols, nrows, tempdata;
        for (int i = offset; i < count; i++) {
            byte chr = buffer[i];

            if (!_ansi.Esc) {
                if (chr == (byte)AsciiControlCodes.Escape) {
                    ClearAnsi();
                    // Start the sequence
                    _ansi.Esc = true;
                    continue;
                } else if (chr == '\t' && !DirectOutput) {
                    // Expand tab if no direct output
                    page = _biosDataArea.CurrentVideoPage;
                    do {
                        Output(' ');
                        col = (byte)_vgaFunctionality.GetCursorPosition(page).X;
                    } while (col % 8 != 0);
                    continue;
                } else {
                    Output((char)chr);
                    continue;
                }
            }
            if(!_ansi.Sci) {
                switch((char)chr) {
                    case '[':
                        _ansi.Sci = true;
                        break;
                    case '7': // Save cursor pos + attr
                    case '8': // Restore this (wonder if this is actually used)
                    case 'D': // Scrolling down
                    case 'M': // Scrolling up
                    default:
                        if(_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("ANSI: Unknown char {AnsiChar} after an Esc character", $"{chr:X2}");
                        }
                        ClearAnsi();
                        break;
                }
                continue;
            }

            // ansi.Esc and ansi.Sci are true
            if (!InternalOutput) {
                _ansi.IsEnabled = true;
            }

            switch ((char)chr) {
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    _ansi.Data[_ansi.NumberOfArg] = (byte)(10 * _ansi.Data[_ansi.NumberOfArg] +
                                                  (((char)chr) - '0'));
                    break;
                case ';':
                    // Till a max of NUMBER_ANSI_DATA
                    _ansi.NumberOfArg++;
                    break;
                case 'm': // SGR (we re-use i on purpose)
                    for (i = 0; i <= _ansi.NumberOfArg; i++) {
                        switch (_ansi.Data[i]) {
                            case 0: // Normal
                                    // Real ansi does this as well. (should
                                    // do current defaults)
                                _ansi.Attribute = 0x07;
                                break;
                            case 1: // Bold mode on
                                _ansi.Attribute |= 0x08;
                                break;
                            case 4: // Underline
                                if(_loggerService.IsEnabled(LogEventLevel.Information)) {
                                    _loggerService.Information("ANSI: No support for underline yet");
                                }
                                break;
                            case 5: // Blinking
                                _ansi.Attribute |= 0x80;
                                break;
                            case 7: // Reverse
                                    // Just like real ansi. (should do use
                                    // current colors reversed)
                                _ansi.Attribute = 0x70;
                                break;
                            case 30: // Foreground color black
                                _ansi.Attribute &= 0xf8;
                                _ansi.Attribute |= 0x0;
                                break;
                            case 31: // Foreground color red
                                _ansi.Attribute &= 0xf8;
                                _ansi.Attribute |= 0x4;
                                break;
                            case 32: // Foreground color green
                                _ansi.Attribute &= 0xf8;
                                _ansi.Attribute |= 0x2;
                                break;
                            case 33: // Foreground color yellow
                                _ansi.Attribute &= 0xf8;
                                _ansi.Attribute |= 0x6;
                                break;
                            case 34: // Foreground color blue
                                _ansi.Attribute &= 0xf8;
                                _ansi.Attribute |= 0x1;
                                break;
                            case 35: // Foreground color magenta
                                _ansi.Attribute &= 0xf8;
                                _ansi.Attribute |= 0x5;
                                break;
                            case 36: // Foreground color cyan
                                _ansi.Attribute &= 0xf8;
                                _ansi.Attribute |= 0x3;
                                break;
                            case 37: // Foreground color white
                                _ansi.Attribute &= 0xf8;
                                _ansi.Attribute |= 0x7;
                                break;
                            case 40:
                                _ansi.Attribute &= 0x8f;
                                _ansi.Attribute |= 0x0;
                                break;
                            case 41:
                                _ansi.Attribute &= 0x8f;
                                _ansi.Attribute |= 0x40;
                                break;
                            case 42:
                                _ansi.Attribute &= 0x8f;
                                _ansi.Attribute |= 0x20;
                                break;
                            case 43:
                                _ansi.Attribute &= 0x8f;
                                _ansi.Attribute |= 0x60;
                                break;
                            case 44:
                                _ansi.Attribute &= 0x8f;
                                _ansi.Attribute |= 0x10;
                                break;
                            case 45:
                                _ansi.Attribute &= 0x8f;
                                _ansi.Attribute |= 0x50;
                                break;
                            case 46:
                                _ansi.Attribute &= 0x8f;
                                _ansi.Attribute |= 0x30;
                                break;
                            case 47:
                                _ansi.Attribute &= 0x8f;
                                _ansi.Attribute |= 0x70;
                                break;
                            default:
                                break;
                        }
                    }
                    ClearAnsi();
                    break;
                case 'f':
                case 'H': // Cursor Position
                    if(!_ansi.WasWarned && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _ansi.WasWarned = true;
                        _loggerService.Warning("ANSI Warning to debugger: ANSI SEQUENCES USED");
                    }
                    ncols = _biosDataArea.ScreenColumns;
                    nrows = (ushort)(_biosDataArea.ScreenRows + 1);
                    // Turn them into positions that are on the screen
                    if (_ansi.Data[0] == 0) {
                        _ansi.Data[0] = 1;
                    }
                    if (_ansi.Data[1] == 0) {
                        _ansi.Data[1] = 1;
                    }
                    if (_ansi.Data[0] > nrows) {
                        _ansi.Data[0] = (byte)nrows;
                    }
                    if (_ansi.Data[1] > ncols) {
                        _ansi.Data[1] = (byte)ncols;
                    }

                    // ansi=1 based,  int10 is 0 based
                    _vgaFunctionality.SetCursorPosition(
                        new CursorPosition(--(_ansi.Data[0]), --(_ansi.Data[1]), page));
                    ClearAnsi();
                    break;
                case 'A': // Cursor up
                    col = (byte)_vgaFunctionality.GetCursorPosition(page).X;
                    row = (byte)_vgaFunctionality.GetCursorPosition(page).Y;
                    tempdata = (ushort)(_ansi.Data[0] != 0 ? _ansi.Data[0] : 1);
                    if (tempdata > row) {
                        row = 0;
                    } else {
                        row -= (byte)tempdata;
                    }
                    _vgaFunctionality.SetCursorPosition(new CursorPosition(col, row, page));
                    ClearAnsi();
                    break;
                case 'B': // Cursor Down
                    col = (byte)_vgaFunctionality.GetCursorPosition(page).X;
                    row = (byte)_vgaFunctionality.GetCursorPosition(page).Y;
                    nrows = (ushort)(_biosDataArea.ScreenRows + 1);
                    tempdata = (ushort)(_ansi.Data[0] > 0 ? _ansi.Data[0] : 1);
                    if (tempdata + row >= nrows) {
                        row = (byte)(nrows - 1);
                    } else {
                        row += (byte)tempdata;
                    }
                    _vgaFunctionality.SetCursorPosition(new CursorPosition(col, row, page));
                    ClearAnsi();
                    break;
                case 'C': // Cursor forward
                    col = (byte)_vgaFunctionality.GetCursorPosition(page).X;
                    row = (byte)_vgaFunctionality.GetCursorPosition(page).Y;
                    ncols = _biosDataArea.ScreenColumns;
                    tempdata = (ushort)(_ansi.Data[0] > 0 ? _ansi.Data[0] : 1);
                    if (tempdata + col >= ncols) {
                        col = (byte)(ncols - 1);
                    } else {
                        col += (byte)tempdata;
                    }
                    _vgaFunctionality.SetCursorPosition(new CursorPosition(col, row, page));
                    ClearAnsi();
                    break;
                case 'D': // Cursor backward
                    col = (byte)_vgaFunctionality.GetCursorPosition(page).X;
                    row = (byte)_vgaFunctionality.GetCursorPosition(page).X;
                    tempdata = (ushort)(_ansi.Data[0] > 0 ? _ansi.Data[0] : 1);
                    if (tempdata > col) {
                        col = 0;
                    } else {
                        col -= (byte)tempdata;
                    }
                    _vgaFunctionality.SetCursorPosition(new CursorPosition(col, row, page));
                    ClearAnsi();
                    break;
                case 'J': // Erase screen and move cursor home
                    if (_ansi.Data[0] == 0) {
                        _ansi.Data[0] = 2;
                    }
                    // Every version behaves like type 2
                    if (_ansi.Data[0] != 2 && _loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information("ANSI: {EscapceSequence} called : not supported handling as 2",
                            $"Esc{_ansi.Data[0]:d}J");
                    }
                    _vgaFunctionality.SetActivePage(page);
                    _vgaFunctionality.VerifyScroll(0, 0, 0, 255, 255, 0, _ansi.Attribute);
                    ClearAnsi();
                    _vgaFunctionality.SetCursorPosition(new CursorPosition(0, 0, page));
                    break;
                case 'h': // Set mode (if code =7 enable linewrap)
                case 'I': // Reset mode
                    if(_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("ANSI: set/reset mode called (not supported)");
                    }
                    ClearAnsi();
                    break;
                case 'u': // Restore Cursor Pos
                    _vgaFunctionality.SetCursorPosition(new CursorPosition(_ansi.SaveColumn, _ansi.SaveRow, page));
                    ClearAnsi();
                    break;
                case 's': // save cursor
                    _ansi.SaveColumn = (sbyte)_vgaFunctionality.GetCursorPosition(page).X;
                    _ansi.SaveRow = (sbyte)_vgaFunctionality.GetCursorPosition(page).Y;
                    break;
                case 'K': // Erase till end of line (don't touch cursor)
                    col = (byte)_vgaFunctionality.GetCursorPosition(page).X;
                    row = (byte)_vgaFunctionality.GetCursorPosition(page).Y;
                    ncols = _biosDataArea.ScreenColumns;

                    // Use this one to prevent scrolling when end of screen
                    // is reached
                    _vgaFunctionality.WriteCharacterAtCursor(
                        new CharacterPlusAttribute(' ', _ansi.Attribute, true),
                        page, ncols - col);

                    _vgaFunctionality.SetCursorPosition(new CursorPosition(col, row, page));
                    ClearAnsi();
                    break;
                case 'M': // Delete line (NANSI)
                    row = (byte)_vgaFunctionality.GetCursorPosition(page).Y;
                    ncols = _biosDataArea.ScreenColumns;
                    nrows = (ushort)(_biosDataArea.ScreenRows + 1);
                    _vgaFunctionality.SetActivePage(page);
                    _vgaFunctionality.VerifyScroll(0, row, 0,
                        (byte)(ncols - 1), (byte)(nrows - 1),
                        _ansi.Data[0] > 0 ? - _ansi.Data[0] : -1,
                        _ansi.Attribute);
                    break;
                case 'l': // (if code =7) disable linewrap
                case 'p': // Reassign keys (needs strings)
                case 'i': // Printer stuff
                default:
                    if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information("ANSI: Unhandled character {AnsiChar} in Escape sequence", $"{chr:X2}");
                    }
                    ClearAnsi();
                    break;
            }
        }
    }

    public override ushort Information {
        get {
            if (_biosKeybardBuffer.IsEmpty && _readCache is 0) {
                return NoInputAvailable;
            }

            if(_readCache is not 0 || _biosKeybardBuffer.PeekKeyCode() is not null) {
                return InputAvailable;
            }

            _biosKeybardBuffer.Flush();
            return NoInputAvailable;
        }
    }

    public override bool TryReadFromControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }

    public override bool TryWriteToControlChannel(uint address, ushort size, [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }

    private void Output(char chr) {
        if (InternalOutput || _ansi.IsEnabled) {
            if (GetIsInTextMode()) {
                byte page = _biosDataArea.CurrentVideoPage;
                ushort pos = _biosDataArea.CursorPosition[page];
                byte col = ConvertUtils.ReadLsb(pos);
                byte row = ConvertUtils.ReadMsb(pos);
                ushort ncols = _biosDataArea.ScreenColumns;
                ushort nrows = _biosDataArea.ScreenRows;

                if (nrows == row + 1 &&
                (chr == '\n' || (ncols == col + 1 && chr != '\r' &&
                                chr != 8 && chr != 7))) {
                    _vgaFunctionality.SetActivePage(page);
                    _vgaFunctionality.VerifyScroll(0,
                                        0,
                                        0,
                                        (byte)(ncols - 1),
                                        (byte)(nrows - 1),
                                        -1,
                                        _ansi.Attribute);
                    _vgaFunctionality.SetCursorPosition(
                        new CursorPosition(col, row - 1, page));
                }

                CharacterPlusAttribute characterPlusAttribute = new CharacterPlusAttribute(chr, _ansi.Attribute, UseAttribute: true);
                _vgaFunctionality.WriteTextInTeletypeMode(characterPlusAttribute);
            }

        } else {
            OutputWithNoAttributes(chr);
        }
    }

    private void OutputWithNoAttributes(byte byteChar) {
        if(!GetIsInTextMode()) {
            return;
        }
        OutputWithNoAttributes((char)byteChar);
    }

    private void OutputWithNoAttributes(AsciiControlCodes controlCode) {
        if (!GetIsInTextMode()) {
            return;
        }
        OutputWithNoAttributes((char)controlCode);
    }

    private void OutputWithNoAttributes(char chr) {
        if (!GetIsInTextMode()) {
            return;
        }
        _vgaFunctionality.WriteTextInTeletypeMode(new(chr, 7, UseAttribute: false));
    }

    private void ClearAnsi() {
        _ansi.Esc = false;
        _ansi.Sci = false;
        Array.Clear(_ansi.Data, 0, Ansi.ANSI_DATA_LENGTH);
        _ansi.NumberOfArg = 0;
    }
}