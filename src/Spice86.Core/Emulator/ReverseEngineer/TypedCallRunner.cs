namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The typed-call bodies of <see cref="CSharpOverrideHelper"/>: pushes the return frame the
/// untyped helpers push, runs the typed callee, then requires it to have returned to the
/// expected return address with the stack at or above the level it had before the pushes.
/// </summary>
internal sealed class TypedCallRunner {
    private readonly State _state;
    private readonly Stack _stack;
    private readonly Func<string, UnrecoverableException> _failAsUntested;

    public TypedCallRunner(State state, Stack stack,
        Func<string, UnrecoverableException> failAsUntested) {
        _state = state;
        _stack = stack;
        _failAsUntested = failAsUntested;
    }

    /// <summary>The shared body of <c>CallWithStackArguments</c> and <c>CallWithStackSpace</c>:
    /// pushes <paramref name="arguments"/>, reserves <paramref name="reserveBytes"/> of unwritten
    /// stack space, performs the typed call, then steps SP by <paramref name="cleanupBytes"/>.
    /// </summary>
    public T CallWithStackArgumentsCore<T>(ushort expectedReturnCs, ushort expectedReturnIp,
        int cleanupBytes, Func<T> typedCall, ushort[] arguments, int reserveBytes) {
        for (int index = arguments.Length - 1; index >= 0; index--) {
            _stack.Push16(arguments[index]);
        }
        _state.SP = (ushort)(_state.SP - reserveBytes);
        T result = ExecuteTypedCall(expectedReturnCs, expectedReturnIp, expectedReturnCs, CallDistance.Near,
            BitWidth.WORD_16, typedCall);
        _state.SP = (ushort)(_state.SP + cleanupBytes);
        return result;
    }

    /// <summary>The shared void body of <c>CallWithStackArguments</c> and
    /// <c>CallWithStackSpace</c>.</summary>
    public void CallWithStackArgumentsCore(ushort expectedReturnCs, ushort expectedReturnIp,
        int cleanupBytes, Action typedCall, ushort[] arguments, int reserveBytes) {
        for (int index = arguments.Length - 1; index >= 0; index--) {
            _stack.Push16(arguments[index]);
        }
        _state.SP = (ushort)(_state.SP - reserveBytes);
        ExecuteTypedCall(expectedReturnCs, expectedReturnIp, expectedReturnCs, CallDistance.Near,
            BitWidth.WORD_16, typedCall);
        _state.SP = (ushort)(_state.SP + cleanupBytes);
    }

    /// <summary>
    /// Runs a typed call dispatch: pushes the return frame the untyped helpers push, then requires the
    /// callee to have returned to the expected return address with the stack at or above the level it had
    /// before the pushes.
    /// </summary>
    public T ExecuteTypedCall<T>(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs,
        CallDistance distance, BitWidth returnWidth, Func<T> typedCall) {
        uint expectedStackAddress = PrepareTypedCall(expectedReturnCs, expectedReturnIp, targetCs, distance,
            returnWidth);
        T result = typedCall.Invoke();
        CheckTypedCallReturn(expectedReturnCs, expectedReturnIp, expectedStackAddress);
        return result;
    }

    public void ExecuteTypedCall(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs,
        CallDistance distance, BitWidth returnWidth, Action typedCall) {
        uint expectedStackAddress = PrepareTypedCall(expectedReturnCs, expectedReturnIp, targetCs, distance,
            returnWidth);
        typedCall.Invoke();
        CheckTypedCallReturn(expectedReturnCs, expectedReturnIp, expectedStackAddress);
    }

    private uint PrepareTypedCall(ushort expectedReturnCs, ushort expectedReturnIp, ushort targetCs,
        CallDistance distance, BitWidth returnWidth) {
        uint expectedStackAddress = _state.StackPhysicalAddress;
        _stack.PushReturnAddress(distance, returnWidth,
            new SegmentedAddress(expectedReturnCs, expectedReturnIp));
        _state.CS = targetCs;
        _state.IP = expectedReturnIp;
        return expectedStackAddress;
    }

    private void CheckTypedCallReturn(ushort expectedReturnCs, ushort expectedReturnIp, uint expectedStackAddress) {
        if (_state.StackPhysicalAddress < expectedStackAddress) {
            throw _failAsUntested($"Typed call consumed the stack down to {_state.StackPhysicalAddress:X5}, below {expectedStackAddress:X5}");
        }
        if (_state.CS != expectedReturnCs || _state.IP != expectedReturnIp) {
            SegmentedAddress actual = new(_state.CS, _state.IP);
            throw new CallStackUnwindException(actual,
                $"Typed call returned to {actual} instead of {expectedReturnCs:X4}:{expectedReturnIp:X4}");
        }
    }
}
