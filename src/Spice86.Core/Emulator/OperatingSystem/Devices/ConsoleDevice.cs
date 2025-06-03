namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.IO;

/// <summary>
/// Represents the console device.
/// </summary>
public class ConsoleDevice : CharacterDevice, IAssemblyRoutineWriter {
    private byte _readCache = 0;
    public const int InputAvailable = 0x80D3;
    public const int NoInputAvailable = 0x8093;
    private readonly EmulationLoop _emulationLoop;
    private readonly BiosDataArea _biosDataArea;
    private readonly SegmentedAddress? _biosKeyboardCallback;
    private readonly BiosKeyboardBuffer _biosKeybardBuffer;
    private readonly IVgaFunctionality _vgaFunctionality;
    private readonly State _state;
    private readonly Stack _stack;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly NonLinearFlow _nonLinearFlow;
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
    public ConsoleDevice(ILoggerService loggerService, State state, Stack stack,
        SegmentedAddress? biosKeyboardCallback, EmulationLoop emulationLoop,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        BiosDataArea biosDataArea, IVgaFunctionality vgaFunctionality,
        BiosKeyboardBuffer biosKeyboardBuffer, DeviceAttributes attributes)
        : base(loggerService, attributes, "CON") {
        _biosKeybardBuffer = biosKeyboardBuffer;
        _biosKeyboardCallback = biosKeyboardCallback;
        _state = state;
        _stack = stack;
        _emulationLoop = emulationLoop;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _biosDataArea = biosDataArea;
        _vgaFunctionality = vgaFunctionality;
        _nonLinearFlow = new NonLinearFlow(_state, _stack);
    }

    public bool InternalOutput { get; set; }

    public bool Echo { get; set; } = true;

    public bool DirectOutput { get; set; }

    public override string Name => "CON";

    public override bool CanSeek => false;

    public override bool CanRead => true;

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

    private class NonLinearFlow {
        private readonly Stack _stack;
        private readonly State _state;

        public NonLinearFlow(State state, Stack stack) {
            _state = state;
            _stack = stack;
        }

        public void InterruptCall(SegmentedAddress targetAddress, SegmentedAddress expectedReturnAddress) {
            _stack.Push16(_state.Flags.FlagRegister16);
            FarCall(targetAddress, expectedReturnAddress);

        }

        void FarCall(SegmentedAddress targetAddress, SegmentedAddress expectedReturnAddress) {
            _stack.PushSegmentedAddress(expectedReturnAddress);
            _state.IpSegmentedAddress = targetAddress;
        }

        void NearCall(ushort targetOffset, ushort expectedReturnOffset) {
            _state.IP = targetOffset;
            _stack.Push16(expectedReturnOffset);
        }
    }

    private (byte ScanCode, byte AsciiCharacter) ReadKeyboardInterrupt() {
        if (_biosKeyboardCallback is null) {
            return (0,0); // No keyboard callback defined
        }
        SegmentedAddress expectedReturnAddress = _state.IpSegmentedAddress;
        // Wait for keypress
        ushort keyStroke;
        do {
            _nonLinearFlow.InterruptCall(_biosKeyboardCallback.Value, expectedReturnAddress);
            _state.AH = 0x00; // Function 0: Read keystroke
            _emulationLoop.RunFromUntil(_biosKeyboardCallback.Value, expectedReturnAddress);
            keyStroke = _state.AX;
        } while (keyStroke is 0 && _state.IsRunning);
        return (_state.AH, _state.AL);
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if(count == 0 || offset > buffer.Length || buffer.Length == 0 || _biosKeyboardCallback is null) {
            return 0;
        }
        ushort oldAx = _state.AX;
        int index = offset;
        int readCount = 0;
        if ((_readCache > 0) && (buffer.Length > 0)) {
            buffer[index++] = _readCache;
            if (Echo) {
                _vgaFunctionality.WriteTextInTeletypeMode(new CharacterPlusAttribute(
                    (char)_readCache, 7, UseAttribute: false));
            }
            _readCache = 0;
        }
        while(index < buffer.Length && readCount < count) {
            _state.AH = 0x10;
            (byte ScanCode, byte AsciiCharacter) = ReadKeyboardInterrupt();
            //TOOD: Continue this implementation.
            switch (AsciiCharacter) {
                case (byte)AsciiControlCodes.CarriageReturn:
                    break;
                default:
                    break;
            }
        }
        _state.AX = oldAx; // Restore AX
        return readCount;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        for (int i = offset; i < count; i++) {
            byte chr = buffer[i];
            OutputNonAnsi((char)chr);
        }
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
            OutputNonAnsi(chr);
        }
    }

    private void OutputNonAnsi(char chr) {
        _vgaFunctionality.WriteTextInTeletypeMode(new(chr, 7, UseAttribute: false));
    }

    private void ClearAnsi() {
        _ansi.Esc = false;
        _ansi.Esi = false;
        Array.Clear(_ansi.Data, 0, Ansi.ANSI_DATA_LENGTH);
        _ansi.NumberOfArg = 0;
    }

    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        throw new NotImplementedException();
    }
}