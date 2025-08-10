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
/// Crude implementation of BIOS keyboard buffer handler (hardware interrupt 0x9, IRQ1)
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly Keyboard _keyboard;
    private readonly DualPic _dualPic;

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
        _keyboard = keyboard;
        _dualPic = dualPic;
        BiosKeyboardBuffer = biosKeyboardBuffer;
    }

    /// <summary>
    /// Gets the BIOS keyboard buffer.
    /// </summary>
    public BiosKeyboardBuffer BiosKeyboardBuffer { get; }

    /// <inheritdoc />
    public override byte VectorNumber => 0x9;

    /// <inheritdoc />
    public override void Run() {
        // Get the keyboard event directly rather than through port reads
        // This ensures we're handling a single coherent event
        KeyboardEventArgs keyboardEvent = _keyboard.GetNextKeyboardEvent();
        
        if (keyboardEvent.Equals(KeyboardEventArgs.None)) {
            // No key event available
            _dualPic.AcknowledgeInterrupt(1);
            return;
        }
        
        byte? scanCode = keyboardEvent.ScanCode;
        byte ascii = keyboardEvent.AsciiCode ?? 0;
        
        if (!scanCode.HasValue) {
            // No valid scan code
            _dualPic.AcknowledgeInterrupt(1);
            return;
        }

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("{BiosInt9KeyReceived} ScanCode={ScanCode:X2} ASCII={ASCII:X2}", 
                scanCode.Value, ascii);
        }
        
        // Enqueue the key code into the BIOS keyboard buffer (as scancode << 8 | ascii)
        BiosKeyboardBuffer.EnqueueKeyCode((ushort)(scanCode.Value << 8 | ascii));
        _dualPic.AcknowledgeInterrupt(1);
    }
}