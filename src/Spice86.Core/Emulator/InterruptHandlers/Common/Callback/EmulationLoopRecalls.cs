namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

/// <summary>
/// A class that waits for machine code to run. Called by C# code.
/// </summary>
/// <remarks>Primarly used by the emulator, for example when an interrupt handler needs to wait for another interrupt handler. <br/>
/// This I/O operation has to go through the emulation loop, otherwise the wait would block the emulated program.</remarks>
public class EmulationLoopRecalls {
    private readonly NonLinearFlow _nonLinearFlow;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly EmulationLoop _emulationLoop;
    private readonly State _state;

    public EmulationLoopRecalls(InterruptVectorTable ivt, State state, Stack stack, EmulationLoop emulationLoop) {
        _nonLinearFlow = new(state, stack);
        _state = state;
        _emulationLoop = emulationLoop;
        _interruptVectorTable = ivt;
    }

    /// <summary>
    /// Waits for a keypress, until we can get a keyboard scan code in the AL register from the INT16H BIOS Function 0x0 <see cref="KeyboardInt16Handler.GetKeystroke" />.
    /// </summary>
    /// <returns>Returns the scancode byte.</returns>
    public byte ReadBiosInt16HGetKeyStroke() {
        SegmentedAddress expectedReturnAddress = _state.IpSegmentedAddress;
        // Wait for keypress
        ushort keyStroke;
        byte oldAh = _state.AH;
        SegmentedAddress biosKeyboardCallback = _interruptVectorTable[0x16];
        do {
            _nonLinearFlow.InterruptCall(biosKeyboardCallback, expectedReturnAddress);
            _state.AH = 0x00; // Function 0x0: GetKeyStroke
            _emulationLoop.RunFromUntil(biosKeyboardCallback, expectedReturnAddress);
            keyStroke = _state.AX;
        } while (keyStroke is 0 && _state.IsRunning);
        byte scanCode = _state.AL;
        _state.AH = oldAh;
        return scanCode;
    }
}
