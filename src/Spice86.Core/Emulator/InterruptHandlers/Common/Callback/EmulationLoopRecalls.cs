namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Shared.Utils;

/// <summary>
/// A class that waits for machine code to run. Called by C# code.
/// </summary>
/// <remarks>Primarly used by the emulator, for example when an interrupt handler needs to wait for another interrupt handler. <br/>
/// This I/O operation has to go through the emulation loop, otherwise the wait would block the emulated program.</remarks>
public class EmulationLoopRecalls {
    private readonly NonLinearFlow _nonLinearFlow;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly KeyboardInt16Handler _keyboardInt16Handler;
    private readonly EmulationLoop _emulationLoop;
    private readonly State _state;

    public EmulationLoopRecalls(KeyboardInt16Handler keyboardInt16,
        InterruptVectorTable ivt, State state, Stack stack,
        EmulationLoop emulationLoop) {
        _keyboardInt16Handler = keyboardInt16;
        _state = state;
        _emulationLoop = emulationLoop;
        _interruptVectorTable = ivt;
        _nonLinearFlow = new(state, stack);
    }

    /// <summary>
    /// Waits for a keypress, until we can get a keyboard scan code in the AL register from the INT16H BIOS Function 0x0 <see cref="KeyboardInt16Handler.GetKeystroke" />.
    /// </summary>
    /// <returns>Returns the scancode byte.</returns>
    public byte ReadBiosInt16HGetKeyStroke() {
        SegmentedAddress expectedReturnAddress = _state.IpSegmentedAddress;
        byte oldAh = _state.AH;
        SegmentedAddress biosKeyboardCallback = _interruptVectorTable[0x16];
        if(!_keyboardInt16Handler.TryGetPendingKeyCode(out ushort? keyCode)) {
            while (_state.IsRunning && !_keyboardInt16Handler.HasKeyCodePending()) {
                _nonLinearFlow.InterruptCall(biosKeyboardCallback, expectedReturnAddress);
                _state.AH = 0x00; // Function 0x0: GetKeyStroke
                _emulationLoop.RunFromUntil(biosKeyboardCallback, expectedReturnAddress);
            }
            keyCode = _state.AX;
            _state.AH = oldAh;
        }
        byte scanCode = ConvertUtils.ReadLsb(keyCode.Value);
        return scanCode;
    }
}
