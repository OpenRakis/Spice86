namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

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

    public EmulationLoopRecalls(
        InterruptVectorTable ivt, State state, Stack stack,
        EmulationLoop emulationLoop) {
        _state = state;
        _emulationLoop = emulationLoop;
        _interruptVectorTable = ivt;
        _nonLinearFlow = new(state, stack);
    }

    /// <summary>
    /// Waits for a keypress, until we can get a keyboard scan code in the AL register from the INT16H BIOS Function 0x0 <see cref="KeyboardInt16Handler.GetKeystroke" />.
    /// </summary>
    /// <returns>Returns the scancode byte.</returns>
    public void WaitForKeybardDataReady() {
        SegmentedAddress expectedReturnAddress = _state.IpSegmentedAddress;
        // Wait for keypress
        SegmentedAddress biosKeyboardCallback = _interruptVectorTable[0x9];
        do {
            _nonLinearFlow.InterruptCall(biosKeyboardCallback, expectedReturnAddress);
            _emulationLoop.RunFromUntil(biosKeyboardCallback, expectedReturnAddress);
        } while (_state.IsRunning);
    }
}
