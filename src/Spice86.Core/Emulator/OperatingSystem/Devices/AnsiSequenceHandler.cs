namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Shared.Interfaces;

/// <summary>
/// Executes completed ANSI CSI sequences against the VGA display.
/// Each public method corresponds to one ANSI command letter and mirrors
/// the subroutine table in NANSI's nansi_f.asm (ansi_fn_table: cup, cdn, cfw, cbk,
/// hvp, sgr, eid, eil, scp, rcp, sm, rm, il, d_l).
/// </summary>
public sealed class AnsiSequenceHandler {
    private readonly ILoggerService _loggerService;
    private readonly BiosDataArea _biosDataArea;
    private readonly IVgaFunctionality _vga;
    private readonly AnsiState _state;
    private readonly Queue<byte> _stuffAheadBuffer;

    /// <summary>
    /// Creates a handler that applies ANSI commands to the given VGA subsystem.
    /// </summary>
    /// <param name="loggerService">Logger for ANSI diagnostics and warnings.</param>
    /// <param name="biosDataArea">BIOS data area for screen dimensions and cursor positions.</param>
    /// <param name="vga">VGA BIOS interface for cursor movement, scrolling, and character output.</param>
    /// <param name="state">Shared ANSI parser state (attribute, parameters, wrap flag, etc.).</param>
    /// <param name="stuffAheadBuffer">
    /// Device-level stuffahead buffer for injecting synthesized characters (e.g. DSR responses).
    /// NANSI uses a priority-ordered set of stuffahead buffers (fnkey, cprseq, brkkey, xlatseq)
    /// that are drained before polling the BIOS keyboard. This queue serves the same purpose,
    /// ensuring DSR responses bypass the key reassignment lookup in the Read path.
    /// </param>
    public AnsiSequenceHandler(ILoggerService loggerService, BiosDataArea biosDataArea,
        IVgaFunctionality vga, AnsiState state, Queue<byte> stuffAheadBuffer) {
        _loggerService = loggerService;
        _biosDataArea = biosDataArea;
        _vga = vga;
        _state = state;
        _stuffAheadBuffer = stuffAheadBuffer;
    }

    /// <summary>
    /// Dispatches a completed CSI sequence identified by its final command character.
    /// This is the C# equivalent of NANSI's ansi_fn_table dispatch.
    /// </summary>
    public void Execute(char command) {
        switch (command) {
            case '@':
                InsertCharacters();
                break;
            case 'A':
                CursorUp();
                break;
            case 'B':
                CursorDown();
                break;
            case 'C':
                CursorForward();
                break;
            case 'D':
                CursorBackward();
                break;
            case 'H':
            case 'f':
                CursorPosition();
                break;
            case 'J':
                EraseInDisplay();
                break;
            case 'K':
                EraseInLine();
                break;
            case 'L':
                InsertLines();
                break;
            case 'M':
                DeleteLines();
                break;
            case 'P':
                DeleteCharacters();
                break;
            case 'm':
                SetGraphicsRendition();
                break;
            case 'p':
                KeyboardReassignment();
                break;
            case 's':
                SaveCursorPosition();
                break;
            case 'u':
                RestoreCursorPosition();
                break;
            case 'h':
            case 'l':
                SetResetMode(command);
                break;
            case 'n':
                DeviceStatusReport();
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("ANSI: unsupported CSI command '{Command}'", command);
                }
                break;
        }
    }

    private int Param(int index, int defaultValue = 1) {
        if (index >= _state.ParameterCount + 1) {
            return defaultValue;
        }
        int value = _state.Parameters[index];
        return value == 0 ? defaultValue : value;
    }

    private byte Page => _biosDataArea.CurrentVideoPage;
    private ushort ScreenColumns => _biosDataArea.ScreenColumns;
    private int ScreenRows => _biosDataArea.ScreenRows + 1;

    /// <summary>
    /// ESC[nA — Move cursor up n rows. NANSI: cup.
    /// </summary>
    private void CursorUp() {
        CursorPosition pos = _vga.GetCursorPosition(Page);
        int delta = Param(0);
        int row = pos.Y - delta;
        if (row < 0) {
            row = 0;
        }
        _vga.SetCursorPosition(new CursorPosition(pos.X, row, Page));
    }

    /// <summary>
    /// ESC[nB — Move cursor down n rows. NANSI: cdn.
    /// </summary>
    private void CursorDown() {
        CursorPosition pos = _vga.GetCursorPosition(Page);
        int delta = Param(0);
        int row = pos.Y + delta;
        if (row >= ScreenRows) {
            row = ScreenRows - 1;
        }
        _vga.SetCursorPosition(new CursorPosition(pos.X, row, Page));
    }

    /// <summary>
    /// ESC[nC — Move cursor forward n columns. NANSI: cfw.
    /// </summary>
    private void CursorForward() {
        CursorPosition pos = _vga.GetCursorPosition(Page);
        int delta = Param(0);
        int col = pos.X + delta;
        if (col >= ScreenColumns) {
            col = ScreenColumns - 1;
        }
        _vga.SetCursorPosition(new CursorPosition(col, pos.Y, Page));
    }

    /// <summary>
    /// ESC[nD — Move cursor backward n columns. NANSI: cbk.
    /// </summary>
    private void CursorBackward() {
        CursorPosition pos = _vga.GetCursorPosition(Page);
        int delta = Param(0);
        int col = pos.X - delta;
        if (col < 0) {
            col = 0;
        }
        _vga.SetCursorPosition(new CursorPosition(col, pos.Y, Page));
    }

    /// <summary>
    /// ESC[row;colH or ESC[row;colf — Set cursor position (1-based).
    /// NANSI: hvp. Clips to screen bounds like the original.
    /// </summary>
    private void CursorPosition() {
        int row = Param(0) - 1;
        int col = Param(1) - 1;
        if (row < 0) {
            row = 0;
        }
        if (col < 0) {
            col = 0;
        }
        if (row >= ScreenRows) {
            row = ScreenRows - 1;
        }
        if (col >= ScreenColumns) {
            col = ScreenColumns - 1;
        }
        _vga.SetCursorPosition(new CursorPosition(col, row, Page));
    }

    /// <summary>
    /// ESC[nJ — Erase in display. ANSI.SYS treats all variants as "clear screen + home cursor".
    /// NANSI: eid. cls_homes_too is set for ANSI.SYS compatibility.
    /// </summary>
    private void EraseInDisplay() {
        byte page = Page;
        _vga.SetActivePage(page);
        byte blankAttribute = (byte)(_state.Attribute & 0x7F);
        _vga.VerifyScroll(0, 0, 0, (byte)(ScreenColumns - 1), (byte)(ScreenRows - 1), 0, blankAttribute);
        _vga.SetCursorPosition(new CursorPosition(0, 0, page));
    }

    /// <summary>
    /// ESC[K — Erase from cursor to end of line. NANSI: eil.
    /// </summary>
    private void EraseInLine() {
        byte page = Page;
        CursorPosition pos = _vga.GetCursorPosition(page);
        int remaining = ScreenColumns - pos.X;
        byte blankAttribute = (byte)(_state.Attribute & 0x7F);
        _vga.WriteCharacterAtCursor(
            new CharacterPlusAttribute(' ', blankAttribute, true), page, remaining);
        _vga.SetCursorPosition(pos);
    }

    /// <summary>
    /// ESC[nL — Insert n lines at cursor row, scrolling down. NANSI: il.
    /// Count is clamped: if count exceeds available rows, the region is cleared instead.
    /// </summary>
    private void InsertLines() {
        byte page = Page;
        CursorPosition pos = _vga.GetCursorPosition(page);
        int count = Param(0);
        int maxCount = ScreenRows - 1 - pos.Y;
        if (count > maxCount) {
            count = 0;
        }
        byte blankAttribute = (byte)(_state.Attribute & 0x7F);
        _vga.SetActivePage(page);
        _vga.VerifyScroll(-1, 0, (byte)pos.Y,
            (byte)(ScreenColumns - 1), (byte)(ScreenRows - 1),
            count, blankAttribute);
    }

    /// <summary>
    /// ESC[nM — Delete n lines at cursor row, scrolling up. NANSI: d_l.
    /// Count is clamped: if count exceeds available rows, the region is cleared instead.
    /// </summary>
    private void DeleteLines() {
        byte page = Page;
        CursorPosition pos = _vga.GetCursorPosition(page);
        int count = Param(0);
        int maxCount = ScreenRows - 1 - pos.Y;
        if (count > maxCount) {
            count = 0;
        }
        byte blankAttribute = (byte)(_state.Attribute & 0x7F);
        _vga.SetActivePage(page);
        _vga.VerifyScroll(1, 0, (byte)pos.Y,
            (byte)(ScreenColumns - 1), (byte)(ScreenRows - 1),
            count, blankAttribute);
    }

    /// <summary>
    /// ESC[n;n;...m — Set text attributes. NANSI: sgr.
    /// The color_table in nansi_f.asm encodes each entry as (code, AND-mask, OR-mask).
    /// We apply the same mask logic here.
    /// </summary>
    private void SetGraphicsRendition() {
        int count = _state.ParameterCount + 1;
        for (int i = 0; i < count; i++) {
            byte code = _state.Parameters[i];
            ApplySgrCode(code);
        }
    }

    private void ApplySgrCode(byte code) {
        // Derived from NANSI color_table: each entry is (code, AND-mask, OR-mask).
        // cur_attrib = (cur_attrib AND mask) OR value
        switch (code) {
            case 0: // Reset all attributes (normal)
                _state.Attribute = 0x07;
                break;
            case 1: // Bold — set intensity bit
                _state.Attribute |= 0x08;
                break;
            case 4: // Underline — maps to blue foreground on MDA; set blue fg
                _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x01);
                break;
            case 5: // Blink
                _state.Attribute |= 0x80;
                break;
            case 7: // Reverse video
                _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x70);
                break;
            case 8: // Invisible (foreground = background)
                _state.Attribute = (byte)((_state.Attribute & 0x88) | 0x00);
                break;
            // Foreground colors 30-37: AND 0xF8, OR color
            case 30: _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x00); break;
            case 31: _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x04); break;
            case 32: _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x02); break;
            case 33: _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x06); break;
            case 34: _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x01); break;
            case 35: _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x05); break;
            case 36: _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x03); break;
            case 37: _state.Attribute = (byte)((_state.Attribute & 0xF8) | 0x07); break;
            // Background colors 40-47: AND 0x8F, OR color<<4
            case 40: _state.Attribute = (byte)((_state.Attribute & 0x8F) | 0x00); break;
            case 41: _state.Attribute = (byte)((_state.Attribute & 0x8F) | 0x40); break;
            case 42: _state.Attribute = (byte)((_state.Attribute & 0x8F) | 0x20); break;
            case 43: _state.Attribute = (byte)((_state.Attribute & 0x8F) | 0x60); break;
            case 44: _state.Attribute = (byte)((_state.Attribute & 0x8F) | 0x10); break;
            case 45: _state.Attribute = (byte)((_state.Attribute & 0x8F) | 0x50); break;
            case 46: _state.Attribute = (byte)((_state.Attribute & 0x8F) | 0x30); break;
            case 47: _state.Attribute = (byte)((_state.Attribute & 0x8F) | 0x70); break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                    _loggerService.Information("ANSI: unsupported SGR code {Code}", code);
                }
                break;
        }
    }

    /// <summary>
    /// ESC[s — Save current cursor position. NANSI: scp.
    /// </summary>
    private void SaveCursorPosition() {
        CursorPosition pos = _vga.GetCursorPosition(Page);
        _state.SavedColumn = pos.X;
        _state.SavedRow = pos.Y;
    }

    /// <summary>
    /// ESC[u — Restore saved cursor position. NANSI: rcp.
    /// Clips to current screen bounds in case video mode changed since save.
    /// </summary>
    private void RestoreCursorPosition() {
        int col = _state.SavedColumn;
        int row = _state.SavedRow;
        if (col < 0 || row < 0) {
            return;
        }
        if (col >= ScreenColumns) {
            col = ScreenColumns - 1;
        }
        if (row >= ScreenRows) {
            row = ScreenRows - 1;
        }
        _vga.SetCursorPosition(new CursorPosition(col, row, Page));
    }

    /// <summary>
    /// ESC[nh (set) / ESC[nl (reset) — Mode changes.
    /// NANSI: sm/rm. Mode 7 toggles line wrap. Other modes trigger video mode set via BIOS.
    /// </summary>
    private void SetResetMode(char command) {
        int mode = Param(0, 0);
        bool isSet = command == 'h';
        if (mode == 7) {
            _state.WrapFlag = isSet;
            return;
        }
        if (mode <= 0) {
            return;
        }
        _vga.VgaSetMode(mode, ModeFlags.Legacy);
        // NANSI sm_home: after a mode set, home the cursor.
        _vga.SetCursorPosition(new CursorPosition(0, 0, Page));
    }

    /// <summary>
    /// ESC[6n — Report cursor position. NANSI: dsr.
    /// Injects ESC[row;colR followed by CR into the stuffahead buffer.
    /// NANSI stores this in the cprseq buffer which has higher priority than
    /// the keyboard and bypasses key reassignment lookup.
    /// Coordinates are 1-based.
    /// </summary>
    private void DeviceStatusReport() {
        CursorPosition pos = _vga.GetCursorPosition(Page);
        string response = $"\x1B[{pos.Y + 1};{pos.X + 1}R\r";
        foreach (char c in response) {
            _stuffAheadBuffer.Enqueue((byte)c);
        }
    }

    /// <summary>
    /// ESC[n@ — Insert n characters at cursor, shifting existing chars right. NANSI: ic.
    /// Blanks are filled using the current attribute with blink stripped.
    /// </summary>
    private void InsertCharacters() {
        byte page = Page;
        CursorPosition pos = _vga.GetCursorPosition(page);
        int count = Param(0);
        int charsOnLine = ScreenColumns - pos.X;
        byte blankAttribute = (byte)(_state.Attribute & 0x7F);
        if (count >= charsOnLine) {
            _vga.WriteCharacterAtCursor(
                new CharacterPlusAttribute(' ', blankAttribute, true), page, charsOnLine);
            _vga.SetCursorPosition(pos);
            return;
        }
        int charsToMove = charsOnLine - count;
        for (int i = charsToMove - 1; i >= 0; i--) {
            CharacterPlusAttribute ch = _vga.ReadChar(
                new CursorPosition(pos.X + i, pos.Y, page));
            _vga.SetCursorPosition(new CursorPosition(pos.X + i + count, pos.Y, page));
            _vga.WriteCharacterAtCursor(ch, page, 1);
        }
        _vga.SetCursorPosition(pos);
        _vga.WriteCharacterAtCursor(
            new CharacterPlusAttribute(' ', blankAttribute, true), page, count);
        _vga.SetCursorPosition(pos);
    }

    /// <summary>
    /// ESC[nP — Delete n characters at cursor, shifting remaining chars left. NANSI: dc.
    /// Blanks are filled at end of line using the current attribute with blink stripped.
    /// </summary>
    private void DeleteCharacters() {
        byte page = Page;
        CursorPosition pos = _vga.GetCursorPosition(page);
        int count = Param(0);
        int charsOnLine = ScreenColumns - pos.X;
        byte blankAttribute = (byte)(_state.Attribute & 0x7F);
        if (count >= charsOnLine) {
            _vga.WriteCharacterAtCursor(
                new CharacterPlusAttribute(' ', blankAttribute, true), page, charsOnLine);
            _vga.SetCursorPosition(pos);
            return;
        }
        int charsToMove = charsOnLine - count;
        for (int i = 0; i < charsToMove; i++) {
            CharacterPlusAttribute ch = _vga.ReadChar(
                new CursorPosition(pos.X + i + count, pos.Y, page));
            _vga.SetCursorPosition(new CursorPosition(pos.X + i, pos.Y, page));
            _vga.WriteCharacterAtCursor(ch, page, 1);
        }
        _vga.SetCursorPosition(new CursorPosition(pos.X + charsToMove, pos.Y, page));
        _vga.WriteCharacterAtCursor(
            new CharacterPlusAttribute(' ', blankAttribute, true), page, count);
        _vga.SetCursorPosition(pos);
    }

    /// <summary>
    /// ESC[key;string...p — Keyboard reassignment. NANSI: key.
    /// First 1-2 parameter bytes define the key code; remaining bytes define
    /// the replacement string. With no parameters, resets all reassignments.
    /// </summary>
    private void KeyboardReassignment() {
        int totalBytes = _state.ParameterCount + 1;
        if (totalBytes == 1 && _state.Parameters[0] == 0) {
            // NANSI key_init: reset to defaults, which includes one default
            // mapping of Ctrl+PrintScreen (0x7200) → Ctrl+P (0x10).
            _state.KeyRedefinitions.Clear();
            _state.KeyRedefinitions[0x7200] = new byte[] { 0x10 };
            return;
        }
        int dataStart;
        ushort keyCode;
        byte firstByte = _state.Parameters[0];
        if (firstByte == 0 || firstByte == 0xE0) {
            if (totalBytes < 2) {
                return;
            }
            keyCode = (ushort)(_state.Parameters[1] << 8);
            dataStart = 2;
        } else {
            keyCode = firstByte;
            dataStart = 1;
        }
        int dataLength = totalBytes - dataStart;
        if (dataLength <= 0) {
            _state.KeyRedefinitions.Remove(keyCode);
            return;
        }
        byte[] replacement = new byte[dataLength];
        Array.Copy(_state.Parameters, dataStart, replacement, 0, dataLength);
        _state.KeyRedefinitions[keyCode] = replacement;
    }
}
