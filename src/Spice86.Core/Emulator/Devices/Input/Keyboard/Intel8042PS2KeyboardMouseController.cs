namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

/// <summary>
/// C# port of intel8042.cpp with PS/2 keyboard logic from keyboard.cpp.
/// The controller buffer is shared with the inner public PS2Keyboard class.
/// </summary>
public sealed class Intel8042PS2KeyboardMouseController : DefaultIOPortHandler {
    private const int BufferSize = 64;

    // Add a constant for key acceptance delay
    private const int KeyAcceptDelayMs = 30; // Adjust as needed for realism

    // Stopwatch for key acceptance timing
    private readonly Stopwatch _keyAcceptStopwatch = new();

    // Controller buffer entry (maps C++ internal struct)
    private struct BufferEntry {
        public byte Data;
        public bool IsFromAux;
        public bool IsFromKbd;
        public bool SkipDelay;
    }

    private readonly BufferEntry[] _buffer = new BufferEntry[BufferSize];
    private int _bufferStartIdx = 0;
    private int _bufferNumUsed = 0;

    // waiting bytes counters (C++)
    private int _waitingBytesFromAux = 0;
    private int _waitingBytesFromKbd = 0;

    // status/config bits (simplified view mapped from C++)
    private byte _configByte = 0b0000_0111; // controller memory byte 0
    private byte _statusByte = 0b0001_1100; // port 0x64 returned value
    private byte _dataByte = 0x00;

    // status convenience flags mirrored from unions in C++
    private bool IsDataNew {
        get => (_statusByte & (1 << 0)) != 0;
        set {
            if (value) _statusByte |= (1 << 0);
            else _statusByte &= unchecked((byte)~(1 << 0));
        }
    }
    private bool WasLastWriteCmd {
        get => (_statusByte & (1 << 3)) != 0;
        set {
            if (value) _statusByte |= (1 << 3);
            else _statusByte &= unchecked((byte)~(1 << 3));
        }
    }
    private bool IsDataFromAux {
        get => (_statusByte & (1 << 5)) != 0;
        set {
            if (value) _statusByte |= (1 << 5);
            else _statusByte &= unchecked((byte)~(1 << 5));
        }
    }
    private bool IsTransmitTimeout {
        get => (_statusByte & (1 << 6)) != 0;
        set {
            if (value) _statusByte |= (1 << 6);
            else _statusByte &= unchecked((byte)~(1 << 6));
        }
    }

    private bool IsKeyboardDisabled {
        get => (_configByte & (1 << 4)) != 0;
        set {
            if (value) _configByte |= (1 << 4);
            else _configByte &= unchecked((byte)~(1 << 4));
        }
    }
    private bool IsAuxDisabled {
        get => (_configByte & (1 << 5)) != 0;
        set {
            if (value) _configByte |= (1 << 5);
            else _configByte &= unchecked((byte)~(1 << 5));
        }
    }
    private bool UsesKbdTranslation {
        get => (_configByte & (1 << 6)) != 0;
        set {
            if (value) _configByte |= (1 << 6);
            else _configByte &= unchecked((byte)~(1 << 6));
        }
    }

    // command currently executed (waiting for parameter)
    private KeyboardCommand _currentCommand = KeyboardCommand.None;
    private bool _shouldSkipDeviceNotify = false;

    // diagnostic dump flag
    private bool _isDiagnosticDump = false;

    // helpers & services
    private readonly A20Gate _a20Gate;
    private readonly DualPic _dualPic;

    // Public PS/2 keyboard emulation (inner class) using the controller's buffer
    public PS2Keyboard KeyboardDevice { get; }

    public Intel8042PS2KeyboardMouseController(State state,
        IOPortDispatcher ioPortDispatcher, A20Gate a20Gate, DualPic dualPic,
        ILoggerService loggerService, bool failOnUnhandledPort,
        IGuiKeyboardEvents? gui = null)
        : base(state, failOnUnhandledPort, loggerService) {
        _a20Gate = a20Gate;
        _dualPic = dualPic;

        // expose the keyboard implementation (translated from keyboard.cpp)
        KeyboardDevice = new PS2Keyboard(this, loggerService);

        InitPortHandlers(ioPortDispatcher);

        // register GUI keyboard events to the PS2Keyboard (keyboard.cpp behavior)
        if (gui is not null) {
            gui.KeyDown += KeyboardDevice.OnKeyEvent;
            gui.KeyUp += KeyboardDevice.OnKeyEvent;
        }

        // initialize as in I8042_Init() -> flush_buffer()
        FlushBuffer();
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Data, this);
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Command, this);
    }

    private void FlushBuffer() {
        IsDataNew = false;
        IsDataFromAux = false;
        // controller does track is_data_from_kbd separately in C++; we model by local counters
        _bufferStartIdx = 0;
        _bufferNumUsed = 0;
        _waitingBytesFromAux = 0;
        _waitingBytesFromKbd = 0;
        _isDiagnosticDump = false;

        // No direct callbacks to other devices here. In C++ flush_buffer triggers notify to
        // devices if their frames are now accepted. The PS2Keyboard exposes notification entry points:
        // If previously the controller couldn't accept kbd frames, notify the keyboard that it can send more.
        // This mirrors C++ logic guarded by should_skip_device_notify; keep simple: always notify.
        KeyboardDevice?.NotifyReadyForFrame();
    }

    private void EnforceBufferSpace(int numBytes = 1) {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numBytes, BufferSize);
        if (BufferSize < _bufferNumUsed + numBytes) {
            WarnBufferFull();
            FlushBuffer();
        }
    }

    private void MaybeTransferBuffer() {
        // mirrors C++ maybe_transfer_buffer
        if (IsDataNew || _bufferNumUsed == 0) {
            return;
        }

        int idx = _bufferStartIdx;

        // take next byte out of internal buffer and put to output register
        _bufferStartIdx = (_bufferStartIdx + 1) % BufferSize;
        --_bufferNumUsed;

        _dataByte = _buffer[idx].Data;
        IsDataFromAux = _buffer[idx].IsFromAux;
        // C++ tracks is_data_from_kbd separately; we approximate:
        if (_buffer[idx].IsFromKbd) {
            // no status bit for kbd origin, but we must decrement waiting bytes
        }
        IsDataNew = true;

        ActivateIrqsIfNeeded();
    }

    private void ActivateIrqsIfNeeded() {
        // Use configuration bits from config byte
        if (IsDataFromAux && ((_configByte & (1 << 1)) != 0)) {
            // aux irq bit set
            _dualPic.ProcessInterruptRequest(12); // IRQ12 in C++
        }
        if ((IsDataFromAux == false) && ((_configByte & (1 << 0)) != 0)) {
            // keyboard irq enabled bit 0 used differently in C++; emulate IRQ1 activation
            _dualPic.ProcessInterruptRequest(1); // IRQ1 for keyboard typical
        }
    }

    private void BufferAdd(byte data, bool isFromAux = false, bool isFromKbd = false, bool skipDelay = false) {
        if ((isFromAux && IsAuxDisabled) || (isFromKbd && IsKeyboardDisabled)) {
            return;
        }

        if (_bufferNumUsed >= BufferSize) {
            WarnBufferFull();
            FlushBuffer();
            return;
        }

        int idx = (_bufferStartIdx + _bufferNumUsed) % BufferSize;

        _buffer[idx].Data = UsesKbdTranslation && isFromKbd
            ? GetTranslated(data)
            : data;
        _buffer[idx].IsFromAux = isFromAux;
        _buffer[idx].IsFromKbd = isFromKbd;
        _buffer[idx].SkipDelay = skipDelay || (!isFromAux && !isFromKbd);

        _bufferNumUsed++;
        if (isFromAux) _waitingBytesFromAux++;
        if (isFromKbd) _waitingBytesFromKbd++;
        MaybeTransferBuffer();
    }

    // Called by PS2Keyboard to add single kbd byte
    internal void AddKbdByte(byte b) => BufferAdd(b, isFromAux: false, isFromKbd: true);

    // Called by PS2Keyboard to add a whole frame. The C++ keyboard code skips delay timer between subsequent
    // bytes of a mouse frame; for keyboard frames follow default behavior (skipDelay true for subsequent bytes).
    internal void AddKbdFrame(IReadOnlyList<byte> bytes) {
        if (bytes == null || bytes.Count == 0) return;
        EnforceBufferSpace(bytes.Count);
        bool skipDelay = false;
        foreach (byte b in bytes) {
            BufferAdd(b, isFromAux: false, isFromKbd: true, skipDelay: skipDelay);
            // PS/2 mouse frame optimization: skip timer between subsequent bytes — for keyboard frames we keep skipDelay false for first then true for next to match C++ attempt
            skipDelay = true;
        }
    }

    // Aux helpers (mirrors I8042_AddAuxByte / Frame)
    internal void AddAuxByte(byte b) => BufferAdd(b, isFromAux: true, isFromKbd: false);
    internal void AddAuxFrame(IReadOnlyList<byte> bytes) {
        if (bytes == null || bytes.Count == 0) return;
        EnforceBufferSpace(bytes.Count);
        bool skipDelay = false;
        foreach (byte b in bytes) {
            BufferAdd(b, isFromAux: true, isFromKbd: false, skipDelay: skipDelay);
            skipDelay = true;
        }
    }

    // translation table stub (C++ has large table). Per request we skip scan tables; keep identity.
    private static byte GetTranslated(byte b) => b;

    private void WarnBufferFull() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("I8042: Internal buffer overflow");
        }
    }

    // --------------------------
    // I/O port handlers (port 0x60/0x64)
    // --------------------------

    public override byte ReadByte(ushort port) {
        switch (port) {
            case KeyboardPorts.Data:
                if (!IsDataNew) {
                    // Byte already read - return previous
                    return _dataByte;
                }

                if (_isDiagnosticDump && _bufferNumUsed == 0) {
                    _isDiagnosticDump = false;
                    // notify devices that they can send frames again
                    KeyboardDevice.NotifyReadyForFrame();
                }

                if (IsDataFromAux) {
                    if (_waitingBytesFromAux > 0) --_waitingBytesFromAux;
                    // notify aux device if ready
                }

                if (_waitingBytesFromKbd > 0) {
                    // decrement waiting bytes from keyboard
                    --_waitingBytesFromKbd;
                }

                byte retVal = _dataByte;
                IsDataNew = false;
                IsDataFromAux = false;
                // no separate flag for kbd; handled by counters

                return retVal;

            case KeyboardPorts.StatusRegister:
                return _statusByte;

            default:
                return base.ReadByte(port);
        }
    }

    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case KeyboardPorts.Data: // 0x60
                WasLastWriteCmd = false;
                if (_currentCommand != KeyboardCommand.None) {
                    // a controller command waiting for parameter -> execute
                    KeyboardCommand cmd = _currentCommand;
                    _currentCommand = KeyboardCommand.None;

                    bool shouldNotifyAux = !IsReadyForAuxFrame();
                    bool shouldNotifyKbd = !IsReadyForKbdFrame();

                    _shouldSkipDeviceNotify = true;
                    FlushBuffer(); // flush_buffer called in C++ prior to execute_command
                    ExecuteCommand(cmd, value);
                    _shouldSkipDeviceNotify = false;

                    if (shouldNotifyAux && IsReadyForAuxFrame()) {
                        // In C++ this notifies mouse device
                    }
                    if (shouldNotifyKbd && IsReadyForKbdFrame()) {
                        KeyboardDevice.NotifyReadyForFrame();
                    }
                } else {
                    // Send this byte to the keyboard (KEYBOARD_PortWrite)
                    IsTransmitTimeout = false;
                    IsKeyboardDisabled = false; // auto-enable keyboard port on write
                    FlushBuffer();
                    KeyboardDevice.PortWrite(value);
                }
                break;

            case KeyboardPorts.Command: // 0x64 (command port)
                _shouldSkipDeviceNotify = true;

                if (_isDiagnosticDump) {
                    _isDiagnosticDump = false;
                    FlushBuffer();
                }

                WasLastWriteCmd = true;

                _currentCommand = KeyboardCommand.None;
                // handle AMI alias special cases like C++ did (byte ranges)
                if (value is <= 0x1f or >= 0x40 and <= 0x5f) {
                    // alias: add 0x20
                    ExecuteCommand((KeyboardCommand)(value + 0x20));
                } else {
                    ExecuteCommand((KeyboardCommand)value);
                }

                _shouldSkipDeviceNotify = false;

                if (!IsReadyForAuxFrame()) {
                    // nothing; just keep behavior equivalent
                }
                if (!IsReadyForKbdFrame()) {
                    // nothing; keyboard is notified from code paths above
                }
                break;

            default:
                base.WriteByte(port, value);
                break;
        }
    }

    private void ExecuteCommand(KeyboardCommand command) {
        // Partial mapping of C++ I8042 execute_command, adapted to available KeyboardCommand enum.
        switch (command) {
            case KeyboardCommand.ReadByteConfig:
                FlushBuffer();
                BufferAdd(_configByte);
                break;
            case KeyboardCommand.WriteByteConfig:
            case KeyboardCommand.WriteOutputPort:
            case KeyboardCommand.SimulateInputKbd:
            case KeyboardCommand.SimulateInputAux:
            case KeyboardCommand.WriteAux:
                // require parameter
                _currentCommand = command;
                break;
            case KeyboardCommand.DisablePortAux:
                IsAuxDisabled = true;
                break;
            case KeyboardCommand.EnablePortAux:
                IsAuxDisabled = false;
                break;
            case KeyboardCommand.TestPortAux:
                IsAuxDisabled = true;
                FlushBuffer();
                BufferAdd(0x00);
                break;
            case KeyboardCommand.TestController:
                // emulate test controller
                IsAuxDisabled = true;
                IsKeyboardDisabled = true;
                UsesKbdTranslation = true;
                // mark passed self-test in config byte bit 2
                _configByte |= (1 << 2);
                FlushBuffer();
                BufferAdd(0x55); // passed
                break;
            case KeyboardCommand.TestPortKbd:
                IsKeyboardDisabled = true;
                FlushBuffer();
                BufferAdd(0x00);
                break;
            case KeyboardCommand.DiagnosticDump:
                // simplified: produce a diagnostic dump (C++ sends 3 bytes per memory byte)
                WarnInternalRamAccess();
                if (BufferSize < 20 * 3) {
                    // same assert check in C++ (no-op in C#)
                }
                FlushBuffer();
                _isDiagnosticDump = true;
                // add a few bytes simulating dump
                DiagDumpByte(_configByte);
                for (byte i = 1; i <= 16; ++i) {
                    DiagDumpByte(0);
                }
                DiagDumpByte(GetInputPort());
                DiagDumpByte(GetOutputPort());
                WarnReadTestInputs();
                DiagDumpByte(0);
                DiagDumpByte(_statusByte);
                break;
            case KeyboardCommand.DisablePortKbd:
                IsKeyboardDisabled = true;
                break;
            case KeyboardCommand.EnablePortKbd:
                IsKeyboardDisabled = false;
                break;
            case KeyboardCommand.ReadKeyboardVersion:
                FlushBuffer();
                BufferAdd(0x00);
                break;
            case KeyboardCommand.ReadInputPort:
                FlushBuffer();
                BufferAdd(GetInputPort());
                break;
            case KeyboardCommand.ReadControllerMode:
                FlushBuffer();
                BufferAdd(0x01); // claim PS/2 (C++)
                break;
            case KeyboardCommand.ReadOutputPort:
                FlushBuffer();
                BufferAdd(GetOutputPort());
                break;
            case KeyboardCommand.DisableA20:
                _a20Gate.IsEnabled = false;
                break;
            case KeyboardCommand.EnableA20:
                _a20Gate.IsEnabled = true;
                break;
            case KeyboardCommand.ReadTestInputs:
                WarnReadTestInputs();
                FlushBuffer();
                BufferAdd(0x00);
                break;
            default:
                // handle ranges and unknowns:
                if (IsCmdMemRead(command)) {
                    WarnInternalRamAccess();
                    BufferAdd(0x00);
                } else if (IsCmdMemWrite(command) || IsCmdPulseLine(command) || IsCmdVendorLines(command)) {
                    // requires parameter or vendor lines - store as current command
                    _currentCommand = command;
                } else {
                    WarnUnknownCommand(command);
                }
                break;
        }
    }

    private void ExecuteCommand(KeyboardCommand command, byte param) {
        switch (command) {
            case KeyboardCommand.WriteByteConfig:
                _configByte = param;
                SanitizeConfigByte();
                break;
            case KeyboardCommand.WriteControllerMode:
                WarnControllerMode();
                // not implemented: leave as-is
                break;
            case KeyboardCommand.WriteOutputPort:
                _a20Gate.IsEnabled = (param & 2) != 0;
                if ((param & 1) == 0) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("I8042: Clearing P2 bit 0 locks a real PC");
                    }
                }
                break;
            case KeyboardCommand.SimulateInputKbd:
                FlushBuffer();
                AddKbdByte(param);
                break;
            case KeyboardCommand.SimulateInputAux:
                FlushBuffer();
                AddAuxByte(param);
                break;
            case KeyboardCommand.WriteAux:
                // MOUSEPS2_PortWrite would be called; we don't have mouse impl here.
                IsTransmitTimeout = false;
                break;
            default:
                if (IsCmdMemWrite(command)) {
                    // internal mem write - not implemented
                } else if (IsCmdPulseLine(command)) {
                    byte lines = (byte)(param & 0b0000_1111);
                    byte code = (byte)command;
                    if ((code == 0xF0 && param != 0b1111 && param != 0b1110) ||
                        (code != 0xF0 && param != 0b1111)) {
                        WarnLinePulse();
                    }
                    if (code == 0xF0 && (lines & 0b0001) == 0) {
                        //TODO
                    }
                } else {
                    // no-op
                }
                break;
        }
    }

    // --------------------------
    // Helper methods / warnings from C++
    // --------------------------

    private void DiagDumpByte(byte b) {
        // produces 3 bytes per nibble similar to C++ diag_dump_byte
        // small translation table for hex (mirrors C++ table partially)
        byte[] table = {
            0x0b, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0a, 0x1e, 0x30, 0x2e, 0x20, 0x12, 0x21
        };
        int hi = (b & 0xF0) >> 4;
        int lo = (b & 0x0F);
        BufferAdd(table[hi]);
        BufferAdd(table[lo]);
        BufferAdd(0x39); // space
    }

    private byte GetInputPort() {
        // simplified implementation similar to get_input_port() in C++
        byte port = 0b1010_0000;
        // bit 6: lacks_cga (example; we cannot call is_machine_cga_or_better here)
        // keep default
        return port;
    }

    private byte GetOutputPort() {
        // simplified implementation similar to get_output_port() in C++
        byte port = 0b0000_0001;
        if (_a20Gate != null) {
            // store A20 state in bit 1
            if (_a20Gate.IsEnabled) port |= (1 << 1);
        }
        // set IRQ active bits if present in config
        if ((_configByte & (1 << 0)) != 0) port |= (1 << 4);
        if ((_configByte & (1 << 1)) != 0) port |= (1 << 5);
        return port;
    }

    private static bool IsCmdMemRead(KeyboardCommand command) {
        byte code = (byte)command;
        return code is >= 0x20 and <= 0x3F;
    }
    private static bool IsCmdMemWrite(KeyboardCommand command) {
        byte code = (byte)command;
        return code is >= 0x60 and <= 0x7F;
    }
    private static bool IsCmdPulseLine(KeyboardCommand command) {
        byte code = (byte)command;
        return code >= 0xF0;
    }
    private static bool IsCmdVendorLines(KeyboardCommand command) {
        byte code = (byte)command;
        return code is >= 0xB0 and <= 0xBD;
    }

    private void SanitizeConfigByte() {
        // force passed self-test bit and reserved bits zero (mirror sanitize_config_byte)
        _configByte |= (1 << 2); // mark self test passed
        _configByte &= unchecked((byte)~(1 << 3));
        _configByte &= unchecked((byte)~(1 << 7));
    }

    private void WarnInternalRamAccess() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("I8042: Accessing internal RAM (other than byte 0x00) gives vendor-specific results");
        }
    }
    private void WarnControllerMode() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("I8042: Switching controller to AT mode not emulated");
        }
    }
    private void WarnLinePulse() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("I8042: Pulsing line other than RESET not emulated");
        }
    }
    private void WarnReadTestInputs() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("I8042: Reading test inputs not implemented");
        }
    }
    private void WarnUnknownCommand(KeyboardCommand command) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("I8042: Unknown command 0x{Command:X2}", (byte)command);
        }
    }

    internal bool IsReadyForAuxFrame() {
        return _waitingBytesFromAux == 0 && !IsAuxDisabled && !_isDiagnosticDump;
    }
    internal bool IsReadyForKbdFrame() {
        return _waitingBytesFromKbd == 0 && !IsKeyboardDisabled && !_isDiagnosticDump;
    }
    internal void NotifyReadyForFrame() {
        // when GUI indicates the controller is ready for frame transfer (mirror KEYBOARD_NotifyReadyForFrame)
        // clear buffer overflow flag logic exists in keyboard.cpp; we don't keep separate overflow flag here,
        // but could be extended. For now call MaybeTransferBuffer to attempt push of buffered data to data register.
        MaybeTransferBuffer();
    }

    /// <summary>
    /// PS/2 keyboard emulation. Public so C# overrides of ASM code can access it.
    /// It shares the controller's buffer and exposes keyboard-specific commands.
    /// </summary>
    public sealed class PS2Keyboard {
        private readonly Intel8042PS2KeyboardMouseController _controller;
        private readonly ILoggerService _loggerService;
        private readonly KeyboardScancodeConverter _scancodeConverter = new();

        // keyboard-internal scancode buffer (keyboard.cpp had vector<uint8_t> buffer[buffer_size])
        private const int KbdFrameBufferSize = 8; // number of scancode frames
        private readonly List<byte>[] _kbdFrames = Enumerable.Range(0, KbdFrameBufferSize).Select(_ => new List<byte>()).ToArray();
        private int _kbdBufferStartIdx = 0;
        private int _kbdBufferNumUsed = 0;
        private bool _kbdBufferOverflowed = false;

        // typematic/repeat mechanism (simplified)
        private KeyboardEventArgs _repeatKey = KeyboardEventArgs.None;
        private int _typematicWait = 0; // countdown for repeat in ms ticks (kept for compatibility)
        private int _typematicPause = 500; // default ms
        private int _typematicRate = 33; // default ms
        private Stopwatch? _typematicStopwatch;
        private long _typematicNextDueMs = -1;
        private readonly object _kbdLock = new();

        // keyboard state
        private byte _ledState = 0;
        private bool _ledsAllOn = false;
        private bool _isScanning = true;
        private byte _codeSet = 0x01; // default codeset 1 as in C++

        // command state waiting for parameter within keyboard domain
        private KeyboardCommand _currentKbdCommand = KeyboardCommand.None;

        // LED reset state tracking
        private enum ResetState {
            Idle,
            LedsAllOnPending
        }
        private ResetState _resetState = ResetState.Idle;
        private Stopwatch? _resetStopwatch;
        private const int LedsAllOnExpireMs = 666;

        public PS2Keyboard(Intel8042PS2KeyboardMouseController controller, ILoggerService loggerService) {
            _controller = controller;
            _loggerService = loggerService;
            Init();
        }

        // Initialization equivalent (keyboard.cpp KEYBOARD_Init)
        private void Init() {
            // hook typematic timer tick handler if necessary
            // On native DOSBox this registers TIMER_AddTickHandler(&typematic_tick)
            // here we rely on the emulation loop calling Tick().
            KeyboardReset(true);
            ScancodeSet(0x01);
        }

        public void OnKeyEvent(object? sender, KeyboardEventArgs e) {
            if (e.Key == PhysicalKey.None) {
                return;
            }

            // Check if enough time has passed since last accepted key
            if (!_controller._keyAcceptStopwatch.IsRunning ||
                _controller._keyAcceptStopwatch.ElapsedMilliseconds >= KeyAcceptDelayMs) {

                // Accept the key
                _controller._keyAcceptStopwatch.Restart();

                // Convert Avalonia Key to KbdKey
                KbdKey kbdKey = _scancodeConverter.ConvertToKbdKey(e.Key);

                // Get all scancodes for this key using current codeset
                List<byte> scancodes = _scancodeConverter.GetScancodes(kbdKey, e.IsPressed, _codeSet);

                // Add each scancode byte to the keyboard buffer
                if (scancodes.Count > 0) {
                    // Send all bytes as a frame
                    AddKeyFrame(scancodes);

                    // Set up typematic for this key if pressed
                    if (e.IsPressed) {
                        _repeatKey = e;
                        TypematicUpdate(_repeatKey);
                    } else if (e.Key == _repeatKey.Key) {
                        // Stop typematic if this key was being repeated
                        _repeatKey = KeyboardEventArgs.None;
                    }
                }
            }
        }

        private void AddKeyFrame(List<byte> scancodes) {
            if (!_isScanning || scancodes.Count == 0) {
                return;
            }

            // Add frame to controller
            _controller.AddKbdFrame(scancodes);
        }

        // Mirror keyboard.cpp maybe_transfer_buffer that transfers a frame to I8042_AddKbdFrame
        private void MaybeTransferFrameToController() {
            lock (_kbdLock) {
                if (_kbdBufferNumUsed == 0) return;
                if (!_controller.IsReadyForKbdFrame()) return;

                int idx = _kbdBufferStartIdx;
                byte[] frame = _kbdFrames[idx].ToArray();

                // transfer whole frame to controller
                _controller.AddKbdFrame(frame);

                _kbdBufferNumUsed--;
                _kbdBufferStartIdx = (_kbdBufferStartIdx + 1) % KbdFrameBufferSize;

                _kbdBufferOverflowed = false;
            }
        }

        internal void NotifyReadyForFrame() {
            // Keyboard is notified controller can accept frames again
            MaybeTransferFrameToController();
        }

        // Add a single byte as keyboard would do (I8042_AddKbdByte)
        internal void AddKbdByte(byte b) {
            _controller.AddKbdByte(b);
        }

        // Port write to keyboard (KEYBOARD_PortWrite)
        public void PortWrite(byte value) {
            // Highest bit usually indicates a keyboard command (mirror keyboard.cpp logic);
            // keyboard-level commands are processed here. For now handle a subset.
            bool isCommand = (value & 0x80) != 0 &&
                             _currentKbdCommand != KeyboardCommand.Set3KeyTypematic &&
                             _currentKbdCommand != KeyboardCommand.Set3KeyMakeBreak &&
                             _currentKbdCommand != KeyboardCommand.Set3KeyMakeOnly;

            if (isCommand) {
                _currentKbdCommand = KeyboardCommand.None;
            }

            KeyboardCommand command = _currentKbdCommand;
            if (command != KeyboardCommand.None) {
                _currentKbdCommand = KeyboardCommand.None;
                ExecuteKeyboardCommand(command, value);
            } else if (isCommand) {
                ExecuteKeyboardCommand((KeyboardCommand)value);
            }
        }

        private void ExecuteKeyboardCommand(KeyboardCommand command) {
            // simplified copy of keyboard.cpp execute_command(KbdCommand)
            switch (command) {
                case KeyboardCommand.SetLeds:
                case KeyboardCommand.SetTypeRate:
                    // ack to controller: 0xFA
                    _controller.AddKbdByte(0xFA);
                    _currentKbdCommand = command;
                    break;
                case KeyboardCommand.CodeSet:
                case KeyboardCommand.Set3KeyTypematic:
                case KeyboardCommand.Set3KeyMakeBreak:
                case KeyboardCommand.Set3KeyMakeOnly:
                    _controller.AddKbdByte(0xFA);
                    ClearInternalBuffer();
                    _currentKbdCommand = command;
                    break;
                case KeyboardCommand.Echo:
                    _controller.AddKbdByte(0xEE); // echo response
                    break;
                case KeyboardCommand.Identify:
                    _controller.AddKbdByte(0xFA);
                    _controller.AddKbdByte(0xAB);
                    _controller.AddKbdByte(0x83);
                    break;
                case KeyboardCommand.ClearEnable:
                    _controller.AddKbdByte(0xFA);
                    ClearInternalBuffer();
                    _isScanning = true;
                    break;
                case KeyboardCommand.DefaultDisable:
                    _controller.AddKbdByte(0xFA);
                    ClearInternalBuffer();
                    SetDefaults();
                    _isScanning = false;
                    break;
                case KeyboardCommand.ResetEnable:
                    _controller.AddKbdByte(0xFA);
                    ClearInternalBuffer();
                    SetDefaults();
                    _isScanning = true;
                    break;
                case KeyboardCommand.Set3AllTypematic:
                    _controller.AddKbdByte(0xFA);
                    ClearInternalBuffer();
                    // set code3 properties - skipped (no codeset3 support)
                    break;
                case KeyboardCommand.Resend:
                    WarnResend();
                    _controller.AddKbdByte(0xFA);
                    break;
                case KeyboardCommand.Reset:
                    _controller.AddKbdByte(0xFA);
                    KeyboardReset(false);
                    _controller.AddKbdByte(0xAA);
                    break;
                default:
                    WarnUnknownKeyboardCommand(command);
                    _controller.AddKbdByte(0xFE); // request resend
                    break;
            }
        }

        private void ExecuteKeyboardCommand(KeyboardCommand command, byte param) {
            switch (command) {
                case KeyboardCommand.SetLeds:
                    _controller.AddKbdByte(0xFA);
                    _ledState = param;
                    MaybeNotifyLedState();
                    break;
                case KeyboardCommand.CodeSet:
                    if (param != 0) {
                        if (ScancodeSet(param)) {
                            _controller.AddKbdByte(0xFA);
                        } else {
                            _currentKbdCommand = command;
                            _controller.AddKbdByte(0xFE);
                        }
                    } else {
                        _controller.AddKbdByte(0xFA);
                        _controller.AddKbdByte(_codeSet);
                    }
                    break;
                case KeyboardCommand.SetTypeRate:
                    _controller.AddKbdByte(0xFA);
                    SetTypeRate(param);
                    break;
                case KeyboardCommand.Set3KeyTypematic:
                case KeyboardCommand.Set3KeyMakeBreak:
                case KeyboardCommand.Set3KeyMakeOnly:
                    _controller.AddKbdByte(0xFA);
                    ClearInternalBuffer();
                    // set per-key properties - skipped
                    break;
                default:
                    // assert false in C++, ignore here
                    break;
            }
        }

        private void TypematicUpdate(KeyboardEventArgs e) {
            // Use Key identity to manage repeat. Pause/rate are in ms.
            // Exclude Pause and PrintScreen from repeat
            if (e.Key is PhysicalKey.Pause or PhysicalKey.PrintScreen) {
                // no-op
                return;
            }

            if (e.IsPressed) {
                // pressed
                if (_repeatKey.Key == e.Key && _repeatKey.IsPressed) {
                    _typematicWait = _typematicRate;
                } else {
                    _typematicWait = _typematicPause;
                }
                _repeatKey = e;
                StartTypematicTimer();
            } else {
                // released
                if (_repeatKey.Key == e.Key) {
                    _repeatKey = KeyboardEventArgs.None;
                    StopTypematicTimer();
                }
            }
        }

        private void StartTypematicTimer() {
            // Stop existing
            StopTypematicTimer();
            // Start stopwatch-based schedule; emulation loop must call Tick()
            _typematicStopwatch = Stopwatch.StartNew();
            _typematicNextDueMs = _typematicPause;
        }

        private void StopTypematicTimer() {
            _typematicStopwatch = null;
            _typematicNextDueMs = -1;
        }

        private void SetTypeRate(byte b) {
            // tables from keyboard.cpp
            int[] pauseTable = { 250, 500, 750, 1000 };
            int[] rateTable = {
                33,37,42,46,50,54,58,63,67,75,83,92,100,109,118,125,
                133,149,167,182,200,217,233,250,270,303,333,370,400,435,476,500
            };
            int pauseIdx = (b & 0b0110_0000) >> 5;
            int rateIdx = (b & 0b0001_1111);
            _typematicPause = pauseTable[pauseIdx];
            if (rateIdx < rateTable.Length) _typematicRate = rateTable[rateIdx];
        }

        private void SetDefaults() {
            _repeatKey = KeyboardEventArgs.None;
            _typematicPause = 500;
            _typematicRate = 33;
            _kbdBufferStartIdx = 0;
            _kbdBufferNumUsed = 0;
            _kbdBufferOverflowed = false;
            _isScanning = true;
            _codeSet = 0x01;
            _resetState = ResetState.Idle;
            _resetStopwatch = null;
        }

        private void KeyboardReset(bool isStartup = false) {
            SetDefaults();
            ClearInternalBuffer();
            _isScanning = true;
            _ledState = 0;
            _ledsAllOn = !isStartup;
            if (_ledsAllOn) {
                // start stopwatch and mark pending reset expiry; emulation loop must call Tick()
                _resetStopwatch = Stopwatch.StartNew();
                _resetState = ResetState.LedsAllOnPending;
            } else {
                _resetStopwatch = null;
                _resetState = ResetState.Idle;
            }
            MaybeNotifyLedState();
        }

        private void MaybeNotifyLedState() {
            // no-op for now; BIOS doesn't set leds in this project
        }

        // scancode set management
        private bool ScancodeSet(byte requested) {
            if (requested is < 0x01 or > 0x03) {
                WarnUnknownScancodeSet();
                return false;
            }
            _codeSet = requested;
            ClearInternalBuffer();
            return true;
        }

        private void ClearInternalBuffer() {
            lock (_kbdLock) {
                _kbdBufferStartIdx = 0;
                _kbdBufferNumUsed = 0;
                _kbdBufferOverflowed = false;
                _repeatKey = KeyboardEventArgs.None;
            }
        }

        private void WarnResend() {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("KEYBOARD: Resend command not implemented");
            }
        }
        private void WarnUnknownKeyboardCommand(KeyboardCommand cmd) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("KEYBOARD: Unknown command 0x{Cmd:X2}", (byte)cmd);
            }
        }
        private void WarnUnknownScancodeSet() {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("KEYBOARD: Guest requested unknown scancode set");
            }
        }

        public byte GetLedState() {
            // support only 3 LEDs
            return (byte)(_ledsAllOn ? 0xFF : _ledState & 0b0000_0111);
        }

        public void ClrBuffer() {
            ClearInternalBuffer();
        }
    }
}