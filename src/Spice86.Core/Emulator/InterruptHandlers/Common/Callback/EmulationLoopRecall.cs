namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A class that waits for machine code to run. Called by C# code.
/// </summary>
/// <remarks>Primarly used by the emulator, for example when an interrupt handler needs to wait for another interrupt handler. <br/>
/// This I/O operation has to go through the emulation loop, otherwise the wait would block the emulated program.</remarks>
public class EmulationLoopRecall {
    private readonly NonLinearFlow _nonLinearFlow;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly EmulationLoop _emulationLoop;
    private readonly State _state;

    public EmulationLoopRecall(
        InterruptVectorTable ivt, State state, Stack stack, EmulationLoop emulationLoop) {
        _state = state;
        _emulationLoop = emulationLoop;
        _interruptVectorTable = ivt;
        _nonLinearFlow = new(state, stack);
    }

    /// <summary>
    /// Waits for the emulation loop to run an interrupt routine.
    /// </summary>
    /// <param name="interruptVector">The interrupt vector that identifies the interrupt routine to execute.</param>
    public void RunInterrupt(byte interruptVector) {
        SegmentedAddress expectedReturnAddress = _state.IpSegmentedAddress;
        SegmentedAddress biosKeyboardCallback = _interruptVectorTable[interruptVector];
        _nonLinearFlow.InterruptCall(biosKeyboardCallback, expectedReturnAddress);
        _emulationLoop.RunFromUntil(biosKeyboardCallback, expectedReturnAddress);
    }
}
