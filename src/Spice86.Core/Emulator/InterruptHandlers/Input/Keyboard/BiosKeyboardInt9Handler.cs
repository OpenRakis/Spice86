namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

/// <summary>
/// Hardware interrupt handler for the BIOS keyboard interrupt (INT 0x09).
/// <remarks>Acknowledges IRQ 1 (keyboard) after writing the key code to the BIOS keyboard buffer.</remarks>
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly Keyboard _keyboard;
    private readonly DualPic _dualPic;
    private readonly ILoggerService _loggerService;
    private KeyboardEventArgs? _keyboardEvent;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="keyboard">The keyboard controller.</param>
    /// <param name="biosKeyboardBuffer">The structure in emulated memory this interrupt handler writes to.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public BiosKeyboardInt9Handler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, DualPic dualPic, Keyboard keyboard,
        BiosKeyboardBuffer biosKeyboardBuffer, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _loggerService = loggerService;
        _keyboard = keyboard;
        _dualPic = dualPic;
        BiosKeyboardBuffer = biosKeyboardBuffer;
        _keyboard.KeyUp += OnKeyUp;
        _keyboard.KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Gets the BIOS keyboard buffer.
    /// </summary>
    public BiosKeyboardBuffer BiosKeyboardBuffer { get; }

    /// <inheritdoc />
    public override byte VectorNumber => 0x9;

    private void OnKeyDown(KeyboardEventArgs e) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BIOS hardware INT9H, key down event: {BiosInt9KeyDownEvent}", e);
        }
        _keyboardEvent = e;
        RunInternal(e);
    }

    private void OnKeyUp(KeyboardEventArgs e) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BIOS hardware INT9H, key up event: {BiosInt9KeyUpEvent}", e);
        }
        _keyboardEvent = e;
        RunInternal(e);
    }

    /// <inheritdoc />
    public override void Run() {
        if (_keyboardEvent is null) {
            return;
        }

        RunInternal(_keyboardEvent.Value);
    }

    private void RunInternal(KeyboardEventArgs keyboardEvent) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BIOS hardware INT9H, running keyboard interrupt handler.");
        }

        byte ascii = keyboardEvent.AsciiCode ?? 0;
        byte scanCode = keyboardEvent.ScanCode ?? 0;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BIOS hardware INT9H, ascii key received: {BiosInt9AsciiKeyKeyReceived}", ascii);
        }

        BiosKeyboardBuffer.EnqueueKeyCode((ushort)(scanCode << 8 | ascii));
        _dualPic.AcknowledgeInterrupt(1);
    }
}