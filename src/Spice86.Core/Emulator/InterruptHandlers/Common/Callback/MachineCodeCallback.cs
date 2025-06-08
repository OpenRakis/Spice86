namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

public class MachineCodeCallback {
    private readonly NonLinearFlow _nonLinearFlow;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly SegmentedAddress _biosKeyboardCallback;
    private readonly EmulationLoop _emulationLoop;
    private readonly State _state;

    public MachineCodeCallback(InterruptVectorTable ivt, State state, Stack stack, EmulationLoop emulationLoop) {
        _nonLinearFlow = new(state, stack);
        _state = state;
        _emulationLoop = emulationLoop;
        _interruptVectorTable = ivt;
        _biosKeyboardCallback = _interruptVectorTable[0x16];
        
    }

    internal byte ReadBiosInt16HGetKeyStroke() {
        SegmentedAddress expectedReturnAddress = _state.IpSegmentedAddress;
        // Wait for keypress
        ushort keyStroke;
        do {
            _nonLinearFlow.InterruptCall(_biosKeyboardCallback, expectedReturnAddress);
            _state.AH = 0x00; // Function 0x0: GetKeyStroke
            _emulationLoop.RunFromUntil(_biosKeyboardCallback, expectedReturnAddress);
            keyStroke = _state.AX;
        } while (keyStroke is 0 && _state.IsRunning);
        return _state.AL;
    }
}
