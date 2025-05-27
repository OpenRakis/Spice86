namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Represents the console device.
/// </summary>
public class ConsoleDevice : CharacterDevice {
    public const int InputAvailable = 0x80D3;
    public const int NoInputAvailable = 0x8093;
    private readonly BiosDataArea _biosDataArea;
    private readonly BiosKeyboardBuffer _biosKeybardBuffer;
    private readonly IVgaFunctionality _vgaFunctionality;
    private readonly State _state;
    private readonly Ansi _ansi = new Ansi();
    private class Ansi {
        public const int ANSI_DATA_LENGTH = 10;
        public bool Esc { get; set; }
        public bool Esi { get; set; }
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
        BiosDataArea biosDataArea, IVgaFunctionality vgaFunctionality,
        BiosKeyboardBuffer biosKeyboardBuffer, DeviceAttributes attributes)
        : base(loggerService, attributes, "CON") {
        _biosKeybardBuffer = biosKeyboardBuffer;
        _state = state;
        _biosDataArea = biosDataArea;
        _vgaFunctionality = vgaFunctionality;
    }

    public bool InternalOutput { get; set; }

    public bool Echo { get; set; } = true;

    public bool DirectOutput { get; set; }

    public override string Name => "CON";

    public override bool CanSeek => true;

    public override bool CanRead => Information == InputAvailable;

    public override bool CanWrite => true;

    public override long Length => 1;

    public override long Position { get; set; }

    public override void SetLength(long value) {
        //NOP
    }

    public override void Flush() {
        //NOP
    }

    public override long Seek(long offset, SeekOrigin origin) {
        if (origin == SeekOrigin.Begin) {
            Position = offset;
        } else if (origin == SeekOrigin.Current) {
            Position += offset;
        } else if (origin == SeekOrigin.End) {
            Position = Length - offset;
        }
        return Position;
    }

    public override int Read(byte[] buffer, int offset, int count) {
        throw new NotImplementedException();

    }

    public override void Write(byte[] buffer, int offset, int count) {
        throw new NotImplementedException();
    }

    public override ushort Information {
        get {
            if (_biosKeybardBuffer.IsEmpty) {
                return NoInputAvailable; // No input available
            } else {
                return InputAvailable; // Input available
            }
        }
    }

    private void Output(char chr) {
        if (InternalOutput || _ansi.IsEnabled) {
            if (_vgaFunctionality.GetCurrentMode().MemoryModel == MemoryModel.Text) {
                byte page = _biosDataArea.CurrentVideoPage;
                ushort pos = _biosDataArea.CursorPosition[page];
                byte col = ConvertUtils.ReadLsb(pos);
                byte row = ConvertUtils.ReadMsb(pos);
                ushort ncols = _biosDataArea.ScreenColumns;
                ushort nrows = _biosDataArea.ScreenRows;

                if (nrows == row + 1 &&
                (chr == '\n' || (ncols == col + 1 && chr != '\r' &&
                                chr != 8 && chr != 7))) {
                    _vgaFunctionality.VerifyScroll(-1,
                                        0,
                                        (byte)(nrows - 1),
                                        (byte)(ncols - 1),
                                        0,
                                        1,
                                        _ansi.Attribute);
                    _vgaFunctionality.SetCursorPosition(
                        new CursorPosition(col, row - 1, page));
                }

                CharacterPlusAttribute characterPlusAttribute = new CharacterPlusAttribute(chr, _ansi.Attribute, UseAttribute: true);
                _vgaFunctionality.WriteTextInTeletypeMode(characterPlusAttribute);
            }

        } else {
            _vgaFunctionality.WriteTextInTeletypeMode(new(chr, 7, UseAttribute: false));
        }
    }

    private void ClearAnsi() {
        _ansi.Esc = false;
        _ansi.Esi = false;
        Array.Clear(_ansi.Data, 0, Ansi.ANSI_DATA_LENGTH);
        _ansi.NumberOfArg = 0;
    }
}