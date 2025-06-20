namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Emulator.Memory;

internal class NonLinearFlow {
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