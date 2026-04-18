namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.IO;

/// <summary>
/// DOS CON device — the console driver with ANSI.SYS escape sequence support.
/// <para>
/// This is the C# equivalent of the NANSI driver (FreeDOS ANSI.SYS replacement).
/// The Write path implements a three-phase state machine (Normal → EscapeReceived → CsiCollecting)
/// matching NANSI's f_escape / f_bracket / f_get_param progression in nansi_p.asm.
/// Completed CSI sequences are dispatched to <see cref="AnsiSequenceHandler"/>.
/// </para>
/// <para>
/// The Read path handles line-buffered keyboard input via INT 16h, with CR→CRLF expansion,
/// backspace editing, and extended key encoding, matching real ANSI.SYS / CON behavior.
/// </para>
/// </summary>
public class ConsoleDevice : CharacterDevice {
    private const string CON = "CON";

    /// <summary>
    /// Device status word indicating input is available.
    /// </summary>
    public const int InputAvailable = 0x8093;

    /// <summary>
    /// Device status word indicating no input is available.
    /// </summary>
    public const int NoInputAvailable = 0x80D3;

    private readonly ILoggerService _loggerService;
    private readonly BiosDataArea _biosDataArea;
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;
    private readonly IVgaFunctionality _vga;
    private readonly KeyboardInt16Handler _keyboardInt16Handler;
    private readonly State _cpuState;

    private readonly AnsiState _ansiState = new();
    private readonly AnsiSequenceHandler _ansiHandler;
    private readonly Queue<byte> _stuffAheadBuffer = new();

    private VgaMode _currentMode;

    /// <summary>
    /// When an extended key's second byte cannot fit in the caller's buffer,
    /// it is cached here and returned on the next Read call.
    /// </summary>
    private byte _extendedKeyScanCodeCache;

    /// <summary>
    /// Creates the CON device, wiring it to the VGA subsystem and keyboard handler
    /// for ANSI.SYS-compatible console I/O.
    /// </summary>
    /// <param name="memory">Memory reader/writer used by the device header.</param>
    /// <param name="baseAddress">Physical address of the DOS device header in memory.</param>
    /// <param name="loggerService">Logger for ANSI diagnostics and warnings.</param>
    /// <param name="state">CPU register state, used to read keystroke results from INT 16h.</param>
    /// <param name="biosDataArea">BIOS data area for screen dimensions, cursor positions, and active video page.</param>
    /// <param name="keyboardInt16Handler">INT 16h handler that provides blocking keystroke reads.</param>
    /// <param name="vgaFunctionality">VGA BIOS interface for cursor movement, scrolling, and character output.</param>
    /// <param name="biosKeyboardBuffer">Keyboard ring buffer used to check input availability without blocking.</param>
    public ConsoleDevice(IByteReaderWriter memory, uint baseAddress,
        ILoggerService loggerService, State state, BiosDataArea biosDataArea,
        KeyboardInt16Handler keyboardInt16Handler, IVgaFunctionality vgaFunctionality,
        BiosKeyboardBuffer biosKeyboardBuffer)
        : base(memory, baseAddress, CON,
            DeviceAttributes.CurrentStdin | DeviceAttributes.CurrentStdout) {
        _loggerService = loggerService;
        _biosKeyboardBuffer = biosKeyboardBuffer;
        _keyboardInt16Handler = keyboardInt16Handler;
        _cpuState = state;
        _biosDataArea = biosDataArea;
        _vga = vgaFunctionality;
        _currentMode = vgaFunctionality.GetCurrentMode();
        vgaFunctionality.VideoModeChanged += OnVideoModeChanged;
        _ansiHandler = new AnsiSequenceHandler(loggerService, biosDataArea, vgaFunctionality, _ansiState, biosKeyboardBuffer);
    }

    private void OnVideoModeChanged(object? sender, VideoModeChangedEventArgs e) {
        _currentMode = e.NewMode;
    }

    private bool IsTextMode => _currentMode.MemoryModel == MemoryModel.Text;

    /// <summary>
    /// When true, output uses the ANSI attribute even without an explicit ESC sequence.
    /// Set by the DOS kernel for internal screen writes.
    /// </summary>
    public bool InternalOutput { get; set; }

    /// <summary>
    /// Whether characters read from the keyboard are echoed back to the screen.
    /// </summary>
    public bool Echo { get; set; } = true;

    /// <summary>
    /// When true, tab expansion is suppressed (used by INT 21h AH=06h direct console I/O).
    /// </summary>
    public bool DirectOutput { get; set; }

    /// <inheritdoc />
    public override string Name => CON;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanRead => _stuffAheadBuffer.Count > 0 || _extendedKeyScanCodeCache != 0 || _biosKeyboardBuffer.IsEmpty is false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override void Flush() {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException("Console device does not support seeking.");
    }

    /// <inheritdoc />
    public override void SetLength(long value) {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException("Console device does not have a length.");

    /// <inheritdoc />
    public override long Position {
        get => throw new NotSupportedException("Console device does not support getting position.");
        set => throw new NotSupportedException("Console device does not support setting position.");
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) {
        if (count == 0 || offset >= buffer.Length || buffer.Length == 0) {
            return 0;
        }
        ushort savedAx = _cpuState.AX;
        int index = offset;
        int bytesRead = 0;

        bytesRead = DrainExtendedKeyCache(buffer, ref index, bytesRead);

        while (index < buffer.Length && bytesRead < count) {
            // Drain stuffahead buffer (DSR responses, key reassignment) first.
            // NANSI: searchbuf checks fnkey/cprseq/brkkey/xlatseq before keyboard.
            if (_stuffAheadBuffer.Count > 0) {
                buffer[index++] = _stuffAheadBuffer.Dequeue();
                bytesRead++;
                continue;
            }

            _keyboardInt16Handler.GetKeystroke();
            byte asciiCode = _cpuState.AL;
            byte scanCode = _cpuState.AH;

            // Key reassignment lookup (NANSI: lookup in getchar).
            // Normal keys: lookup by ASCII code. Function/extended keys (AL=0 or 0xE0): lookup by scanCode<<8.
            // NANSI keyE0fixup: treats AL=0xE0 the same as AL=0.
            ushort keyCode = asciiCode != 0 && asciiCode != 0xE0 ? asciiCode : (ushort)(scanCode << 8);
            if (_ansiState.KeyRedefinitions.TryGetValue(keyCode, out byte[]? replacement)) {
                foreach (byte b in replacement) {
                    _stuffAheadBuffer.Enqueue(b);
                }
                continue;
            }

            switch (asciiCode) {
                case (byte)AsciiControlCodes.CarriageReturn:
                    bytesRead = HandleCarriageReturn(buffer, ref index, bytesRead);
                    break;
                case (byte)AsciiControlCodes.Backspace:
                    bytesRead = HandleBackspace(buffer, ref index, bytesRead);
                    continue;
                case (byte)AsciiControlCodes.Null:
                    bytesRead = HandleExtendedKey(buffer, ref index, bytesRead, scanCode);
                    break;
                case (byte)AsciiControlCodes.Extended:
                    bytesRead = HandleExtendedKey0xE0(buffer, ref index, bytesRead, asciiCode, scanCode);
                    break;
                default:
                    buffer[index++] = asciiCode;
                    bytesRead++;
                    break;
            }
            if (Echo) {
                EchoCharacter(asciiCode);
            }
        }
        _cpuState.AX = savedAx;
        return bytesRead;
    }

    private int DrainExtendedKeyCache(byte[] buffer, ref int index, int bytesRead) {
        if (_extendedKeyScanCodeCache == 0) {
            return bytesRead;
        }
        buffer[index++] = _extendedKeyScanCodeCache;
        if (Echo) {
            EchoCharacter(_extendedKeyScanCodeCache);
        }
        _extendedKeyScanCodeCache = 0;
        return bytesRead + 1;
    }

    private int HandleCarriageReturn(byte[] buffer, ref int index, int bytesRead) {
        buffer[index++] = (byte)AsciiControlCodes.CarriageReturn;
        bytesRead++;
        if (index < buffer.Length) {
            buffer[index++] = (byte)AsciiControlCodes.LineFeed;
            bytesRead++;
        }
        if (Echo) {
            EchoCharacter((byte)AsciiControlCodes.CarriageReturn);
            EchoCharacter((byte)AsciiControlCodes.LineFeed);
        }
        return bytesRead;
    }

    private int HandleBackspace(byte[] buffer, ref int index, int bytesRead) {
        if (buffer.Length == 1) {
            buffer[index++] = (byte)AsciiControlCodes.Backspace;
            return bytesRead + 1;
        }
        if (index > 0) {
            buffer[index--] = 0;
            bytesRead--;
            EchoCharacter((byte)AsciiControlCodes.Backspace);
            EchoCharacter((byte)' ');
        }
        return bytesRead;
    }

    private int HandleExtendedKey(byte[] buffer, ref int index, int bytesRead, byte scanCode) {
        buffer[index++] = 0;
        bytesRead++;
        if (index < buffer.Length) {
            buffer[index++] = scanCode;
            bytesRead++;
        } else {
            _extendedKeyScanCodeCache = scanCode;
        }
        return bytesRead;
    }

    private int HandleExtendedKey0xE0(byte[] buffer, ref int index, int bytesRead,
        byte asciiCode, byte scanCode) {
        if (scanCode != 0) {
            buffer[index++] = asciiCode;
            return bytesRead + 1;
        }
        buffer[index++] = 0;
        bytesRead++;
        if (index < buffer.Length) {
            buffer[index++] = scanCode;
            bytesRead++;
        } else {
            _extendedKeyScanCodeCache = scanCode;
        }
        return bytesRead;
    }

    private void EchoCharacter(byte character) {
        if (!IsTextMode) {
            return;
        }
        _vga.WriteTextInTeletypeMode(new CharacterPlusAttribute((char)character, _ansiState.Attribute, UseAttribute: true));
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) {
        for (int i = offset; i < offset + count; i++) {
            byte chr = buffer[i];
            switch (_ansiState.Phase) {
                case AnsiParserPhase.Normal:
                    WriteNormalCharacter(chr);
                    break;
                case AnsiParserPhase.EscapeReceived:
                    WriteAfterEscape(chr);
                    break;
                case AnsiParserPhase.CsiCollecting:
                    WriteCsiParameter(chr);
                    break;
                case AnsiParserPhase.StringCollecting:
                    WriteStringCharacter(chr);
                    break;
            }
        }
    }

    /// <summary>
    /// Normal phase: output characters directly, or start an escape sequence on ESC.
    /// Corresponds to NANSI's main loop (f_not_ANSI path).
    /// </summary>
    private void WriteNormalCharacter(byte chr) {
        if (chr == (byte)AsciiControlCodes.Escape) {
            _ansiState.Reset();
            _ansiState.Phase = AnsiParserPhase.EscapeReceived;
            return;
        }
        if (chr == '\t' && !DirectOutput) {
            ExpandTab();
            return;
        }
        OutputCharacter((char)chr);
    }

    /// <summary>
    /// ESC received phase: expect '[' to form CSI. Anything else is a syntax error
    /// (NANSI: f_bracket → syntax_error → f_not_ANSI, prints offending char).
    /// </summary>
    private void WriteAfterEscape(byte chr) {
        if (chr == '[') {
            _ansiState.Phase = AnsiParserPhase.CsiCollecting;
            _ansiState.PrefixAllowed = true;
            return;
        }
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("ANSI: expected '[' after ESC, got 0x{Char:X2}", chr);
        }
        _ansiState.Reset();
        OutputCharacter((char)chr);
    }

    /// <summary>
    /// CSI collecting phase: accumulate digits into parameters, advance on ';',
    /// and dispatch on a command letter (@..Z, a..z).
    /// Matches NANSI's f_get_param / f_in_param states.
    /// </summary>
    private void WriteCsiParameter(byte chr) {
        if (chr >= '0' && chr <= '9') {
            _ansiState.PrefixAllowed = false;
            int paramIndex = _ansiState.ParameterCount;
            if (paramIndex < AnsiState.MaxParameters) {
                _ansiState.Parameters[paramIndex] = (byte)(10 * _ansiState.Parameters[paramIndex] + (chr - '0'));
            }
            return;
        }
        if (chr == ';') {
            _ansiState.PrefixAllowed = false;
            if (_ansiState.EatNextSemicolon) {
                _ansiState.EatNextSemicolon = false;
                return;
            }
            if (_ansiState.ParameterCount < AnsiState.MaxParameters - 1) {
                _ansiState.ParameterCount++;
            }
            return;
        }
        // '=' and '?' only accepted as first char after '[' (NANSI: f_get_args one-shot).
        if (chr == '=' || chr == '?') {
            if (_ansiState.PrefixAllowed) {
                _ansiState.PrefixAllowed = false;
                return;
            }
            // Fall through to syntax error.
        }
        _ansiState.PrefixAllowed = false;
        // Quoted strings for keyboard reassignment (NANSI: f_get_string).
        if (chr == '"' || chr == '\'') {
            _ansiState.StringTerminator = chr;
            _ansiState.Phase = AnsiParserPhase.StringCollecting;
            return;
        }
        // Command letters: @..Z (0x40..0x5A) and a..z (0x61..0x7A)
        // NANSI: after calling the command subroutine, sets escvector=0
        // to return to the main loop (no longer parsing).
        if ((chr >= '@' && chr <= 'Z') || (chr >= 'a' && chr <= 'z')) {
            _ansiHandler.Execute((char)chr);
            _ansiState.Phase = AnsiParserPhase.Normal;
            return;
        }
        // Anything else is a syntax error
        // NANSI: syntax_error → jmp f_not_ANSI, which prints the offending char.
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("ANSI: unexpected char 0x{Char:X2} in CSI sequence", chr);
        }
        _ansiState.Reset();
        OutputCharacter((char)chr);
    }

    /// <summary>
    /// String collecting phase: store each character as a parameter byte until the
    /// matching terminator quote is found. NANSI: f_get_string.
    /// </summary>
    private void WriteStringCharacter(byte chr) {
        if (chr == _ansiState.StringTerminator) {
            // Ending quote: initialize next parameter slot, eat optional semicolon.
            // NANSI: fgs_init_next_param → f_eat_semi.
            if (_ansiState.ParameterCount < AnsiState.MaxParameters - 1) {
                _ansiState.ParameterCount++;
            }
            _ansiState.EatNextSemicolon = true;
            _ansiState.Phase = AnsiParserPhase.CsiCollecting;
            return;
        }
        int idx = _ansiState.ParameterCount;
        if (idx < AnsiState.MaxParameters) {
            _ansiState.Parameters[idx] = chr;
            if (_ansiState.ParameterCount < AnsiState.MaxParameters - 1) {
                _ansiState.ParameterCount++;
            }
        }
    }

    /// <summary>
    /// Expands a tab character to spaces up to the next 8-column boundary.
    /// </summary>
    private void ExpandTab() {
        byte page = _biosDataArea.CurrentVideoPage;
        do {
            OutputCharacter(' ');
        } while (_vga.GetCursorPosition(page).X % 8 != 0);
    }

    /// <summary>
    /// Outputs a single character to the screen using <see cref="AnsiState.Attribute"/>.
    /// NANSI's text-mode inner loop unconditionally writes every character with
    /// <c>cur_attrib</c> (STOSW where AH = cur_attrib). There is no separate
    /// "plain" path — the attribute (default 0x07) is always applied.
    /// Handles scroll-at-bottom-of-screen before writing so the vacated row
    /// uses the ANSI attribute instead of zero.
    /// When wrap is disabled (ESC[7l), characters at end-of-line overwrite in place
    /// instead of wrapping. NANSI: f_at_eol checks wrap_flag.
    /// </summary>
    private void OutputCharacter(char chr) {
        if (!IsTextMode) {
            return;
        }
        byte page = _biosDataArea.CurrentVideoPage;
        ushort pos = _biosDataArea.CursorPosition[page];
        byte col = ConvertUtils.ReadLsb(pos);
        byte row = ConvertUtils.ReadMsb(pos);
        ushort ncols = _biosDataArea.ScreenColumns;
        ushort nrows = _biosDataArea.ScreenRows;

        bool atEndOfLine = ncols == col + 1 && chr != '\r' && chr != '\n' && chr != 8 && chr != 7;

        // When wrap is disabled and cursor is at the last column, overwrite in place
        // without advancing. NANSI f_at_eol: !wrap_flag → dec di, inc dx.
        if (atEndOfLine && !_ansiState.WrapFlag) {
            _vga.WriteCharacterAtCursor(
                new CharacterPlusAttribute(chr, _ansiState.Attribute, true), page, 1);
            return;
        }

        // NANSI f_lf: cmp cur_y, max_y / jb flf_noscroll — scroll when cur_y >= max_y.
        // ScreenRows is the last row index (24 for 25-row mode).
        bool atBottomRow = row == nrows;
        bool needsScroll = chr == '\n' || atEndOfLine;
        if (atBottomRow && needsScroll) {
            // NANSI scroll_up: AH=6 (direction=1), AL=1, lower-right = (max_x, max_y).
            byte blankAttribute = (byte)(_ansiState.Attribute & 0x7F);
            _vga.SetActivePage(page);
            _vga.VerifyScroll(1, 0, 0, (byte)(ncols - 1), (byte)nrows, 1, blankAttribute);
            _vga.SetCursorPosition(new CursorPosition(col, row - 1, page));
        }
        _vga.WriteTextInTeletypeMode(new CharacterPlusAttribute(chr, _ansiState.Attribute, UseAttribute: true));
    }



    /// <inheritdoc />
    public override ushort Information {
        get {
            if (_stuffAheadBuffer.Count > 0 || _extendedKeyScanCodeCache != 0) {
                return InputAvailable;
            }
            if (_biosKeyboardBuffer.IsEmpty) {
                return NoInputAvailable;
            }
            if (_biosKeyboardBuffer.PeekKeyCode() is not null) {
                return InputAvailable;
            }
            _biosKeyboardBuffer.Flush();
            return NoInputAvailable;
        }
    }

    /// <inheritdoc />
    public override bool TryReadFromControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }

    /// <inheritdoc />
    public override bool TryWriteToControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        returnCode = null;
        return false;
    }
}