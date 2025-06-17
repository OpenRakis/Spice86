namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.IO;

/// <summary>
/// Represents the console device.
/// </summary>
public class ConsoleDevice : CharacterDevice {
    private byte _readCache = 0;
    public const int InputAvailable = 0x80D3;
    public const int NoInputAvailable = 0x8093;
    private readonly BiosDataArea _biosDataArea;
    private readonly BiosKeyboardBuffer _biosKeybardBuffer;
    private readonly IVgaFunctionality _vgaFunctionality;
    private readonly EmulationLoopRecalls _emulationLoopRecalls;
    private readonly State _state;
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
    public ConsoleDevice(ILoggerService loggerService, State state,
        EmulationLoopRecalls emulationLoopRecalls, BiosDataArea biosDataArea,
        IVgaFunctionality vgaFunctionality, BiosKeyboardBuffer biosKeyboardBuffer,
        DeviceAttributes attributes)
        : base(loggerService, attributes, "CON") {
        _biosKeybardBuffer = biosKeyboardBuffer;
        _emulationLoopRecalls = emulationLoopRecalls;
        _state = state;
        _biosDataArea = biosDataArea;
        _vgaFunctionality = vgaFunctionality;
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

    public override string Name => "CON";

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

    public override int Read(byte[] buffer, int offset, int count) {
        if(count == 0 || offset > buffer.Length || buffer.Length == 0) {
            return 0;
        }
        ushort oldAx = _state.AX;
        int index = offset;
        int readCount = 0;
        if ((_readCache > 0) && (buffer.Length > 0)) {
            buffer[index++] = _readCache;
            if (Echo) {
                OutputWithNoAttributes(_readCache);
            }
            _readCache = 0;
        }
        while(index < buffer.Length && readCount < count) {
            // Function 0: Read keystroke
            _state.AH = 0x0;
            byte? scanCode = GetOrWaitForScanCode();
            if(scanCode is null) {
                break;
            }
            switch (scanCode) {
                case (byte)AsciiControlCodes.CarriageReturn:
                    buffer[index++] = (byte)AsciiControlCodes.CarriageReturn;
                    readCount++;

                    //It's only expanded if there is room for it
                    if (index > buffer.Length) {
                        buffer[index++] = (byte)AsciiControlCodes.LineFeed;
                        readCount++;
                    }
                    _state.AX = oldAx;
                    if (Echo) {
                        // Maybe don't do this (no need for it actually)
                        // (but it's compatible)
                        OutputWithNoAttributes(AsciiControlCodes.LineFeed);
                        OutputWithNoAttributes(AsciiControlCodes.CarriageReturn);
                    }
                    break;
                case (byte)AsciiControlCodes.Backspace:
                    if (buffer.Length == 1) {
                        buffer[index++] = scanCode.Value;
                        readCount++;
                    } else if (index > 0) {
                        buffer[index--] = 0;
                        readCount--;
                        OutputWithNoAttributes(AsciiControlCodes.Backspace);
                        OutputWithNoAttributes(' ');
                    } else {
                        // No data yet, so restart the loop
                        continue;
                    }
                    break;
                case (byte)AsciiControlCodes.Extended:
                    // Extended keys in the INT 16H 0x10 function call case
                    // This probably won't run until we implement different
                    // variant of the IBM PC clone architecture and call function 0x10 instead of 0x0.
                    // See IS_EGAVGA_ARCH macro in DOSBox, and the MachineType enum (which carries values such as MCH_PCJR)
                    if (_state.AH != 0) {
                        // Extended key if _state.AH is not 0x0
                        buffer[index++] = scanCode.Value;
                        readCount++;
                    } else {
                        buffer[index++] = 0;
                        readCount++;
                        if (buffer.Length > index) {
                            buffer[index++] = _state.AH;
                            readCount++;
                        } else {
                            _readCache = _state.AH;
                        }
                    }
                    break;
                case (byte)AsciiControlCodes.Null:
                    // Extended keys in the INT 16H 0x0 function call case
                    buffer[index++] = scanCode.Value;
                    readCount++;
                    if (buffer.Length > index) {
                        buffer[index++] = _state.AH;
                        readCount++;
                    } else {
                        _readCache = _state.AH;
                    }
                    break;
                default:
                    buffer[index++] = scanCode.Value;
                    readCount++;
                    break;
            }
            if (Echo) {
                // What to do if buffer.Length == 1 and character is BackSpace ?
                OutputWithNoAttributes(scanCode.Value);
            }
        }
        _state.AX = oldAx; // Restore AX
        return readCount;
    }

    /// <summary>
    /// Tries to shortcut the emulation loop recall by reading memory first. <br/>
    /// If nothing is available, we wait for the scan code in the AL register.
    /// <remarks>This is different from FreeDOS and DOS, which just wait directly. <br/>
    /// May break some TSRs which rely on the ability to intercept this.</remarks>
    /// </summary>
    /// <returns>The scancode byte, coming from either the BIOS keyboard buffer or directly from the INT16H software interrupt.</returns>
    private byte GetOrWaitForScanCode() {
        byte? scanCode = (byte?)_biosKeybardBuffer.DequeueKeyCode();
        scanCode ??= _emulationLoopRecalls.ReadBiosInt16HGetKeyStroke();
        return scanCode.Value;
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
                        if(Logger.IsEnabled(LogEventLevel.Warning)) {
                            Logger.Warning("ANSI: Unknown char {AnsiChar} after an Esc character", $"{chr:X2}");
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
                                if(Logger.IsEnabled(LogEventLevel.Information)) {
                                    Logger.Information("ANSI: No support for underline yet");
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
                    if(!_ansi.WasWarned && Logger.IsEnabled(LogEventLevel.Warning)) {
                        _ansi.WasWarned = true;
                        Logger.Warning("ANSI Warning to debugger: ANSI SEQUENCES USED");
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
                    if (_ansi.Data[0] != 2 && Logger.IsEnabled(LogEventLevel.Information)) {
                        Logger.Information("ANSI: {EscapceSequence} called : not supported handling as 2",
                            $"Esc{_ansi.Data[0]:d}J");
                    }
                    _vgaFunctionality.SetActivePage(page);
                    _vgaFunctionality.VerifyScroll(0, 0, 0, 255, 255, 0, _ansi.Attribute);
                    ClearAnsi();
                    _vgaFunctionality.SetCursorPosition(new CursorPosition(0, 0, page));
                    break;
                case 'h': // Set mode (if code =7 enable linewrap)
                case 'I': // Reset mode
                    if(Logger.IsEnabled(LogEventLevel.Warning)) {
                        Logger.Warning("ANSI: set/reset mode called (not supported)");
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
                    if (Logger.IsEnabled(LogEventLevel.Information)) {
                        Logger.Information("ANSI: Unhandled character {AnsiChar} in Escape sequence", $"{chr:X2}");
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