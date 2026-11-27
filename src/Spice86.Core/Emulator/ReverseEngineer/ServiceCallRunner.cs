namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The interrupt-service-call bodies of <see cref="CSharpOverrideHelper"/>: the same stack
/// traffic the interpreted <c>int</c> + stub + <c>iret</c> path performs, around a typed service
/// implementation invoked directly by generated code.
/// </summary>
internal sealed class ServiceCallRunner {
    private readonly State _state;
    private readonly Stack _stack;
    private readonly Func<string, UnrecoverableException> _failAsUntested;

    public ServiceCallRunner(State state, Stack stack,
        Func<string, UnrecoverableException> failAsUntested) {
        _state = state;
        _stack = stack;
        _failAsUntested = failAsUntested;
    }

    public T ServiceCallCore<T>(ushort expectedReturnCs, ushort expectedReturnIp,
        Func<T> invocation, Action<T> stackedFlags, InterruptHandler? handler) {
        _stack.PushInterruptFrame(new SegmentedAddress(expectedReturnCs, expectedReturnIp));
        _state.InterruptFlag = false;
        handler?.OnInterruptEntry();
        T result = invocation();
        stackedFlags(result);
        (SegmentedAddress returnAddress, ushort flags) = _stack.PopInterruptFrame();
        _state.Flags.FlagRegister16 = flags;
        if (returnAddress.Offset != expectedReturnIp || returnAddress.Segment != expectedReturnCs) {
            throw _failAsUntested(
                $"Service call returned to {returnAddress} instead of the expected {expectedReturnCs:X4}:{expectedReturnIp:X4}");
        }
        return result;
    }

    /// <summary>A typed service call that does not write the interrupt frame: IF is saved and
    /// cleared for the call, SP is lowered by six around the handler's
    /// <see cref="InterruptHandler.OnInterruptEntry"/> so the entry bookkeeping sees the stack
    /// pointer the frame would have left, and the invocation runs frameless.</summary>
    public T ServiceCallFramelessCore<T>(InterruptHandler? handler, Func<T> invocation) {
        bool callerInterruptFlag = _state.InterruptFlag;
        _state.InterruptFlag = false;
        _state.SP -= Stack.InterruptFrameSize;
        handler?.OnInterruptEntry();
        _state.SP += Stack.InterruptFrameSize;
        T result = invocation();
        _state.InterruptFlag = callerInterruptFlag;
        return result;
    }

    /// <summary>Positions CS:IP at the <c>int</c>'s return address so the implementation pushes
    /// the same frame bytes the interpreted <c>int</c> pushed, then never returns to the call
    /// site - it halts or unwinds.</summary>
    public Action TerminateCall(ushort expectedReturnCs, ushort expectedReturnIp,
        Func<Action> invocation) {
        _state.CS = expectedReturnCs;
        _state.IP = expectedReturnIp;
        return invocation.Invoke();
    }
}
