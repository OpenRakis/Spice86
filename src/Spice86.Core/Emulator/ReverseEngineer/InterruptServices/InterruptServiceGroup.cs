namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Utils;

/// <summary>
/// Base class of the typed interrupt service helpers generated code calls instead of
/// <see cref="CSharpOverrideHelper.InterruptCall(ushort, ushort, byte)"/>: one subclass per
/// interrupt handler type, one method per service. Each method checks the vector still points
/// to the built-in handler's stub and then either performs the interrupt call on the stub
/// (the service's parameters and selector are written to the registers first and its outputs
/// stay in registers and flags, as after the <c>int</c> instruction) or invokes the service's
/// implementation directly inside an <c>int</c>-equivalent frame for the services declared
/// <see cref="ServiceExecutionPath.Direct"/>.
/// </summary>
/// <typeparam name="THandler">The interrupt handler the group serves.</typeparam>
public abstract class InterruptServiceGroup<THandler> where THandler : class, IInterruptHandler {
    private readonly CSharpOverrideHelper _helper;
    private readonly Lazy<THandler> _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterruptServiceGroup{THandler}"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    protected InterruptServiceGroup(CSharpOverrideHelper helper) {
        _helper = helper;
        _handler = new Lazy<THandler>(helper.GetInterruptHandler<THandler>);
    }

    /// <summary>
    /// The installed interrupt handler the group serves, resolved on the first actual call:
    /// an optional handler can be absent while the group is unused.
    /// </summary>
    protected THandler Handler => _handler.Value;

    /// <summary>
    /// The emulated CPU state the service parameters are written to.
    /// </summary>
    protected State State => _helper.State;

    /// <summary>
    /// Fails as untested when the vector no longer points to the stub of the built-in
    /// handler (the program hooked it). Called before any register is written.
    /// </summary>
    /// <exception cref="UnrecoverableException">The vector does not point to the installed stub.</exception>
    protected void CheckVector() {
        _helper.CheckInterruptServiceVector(Handler.VectorNumber);
    }

    /// <summary>
    /// Fails as untested when the bytes at <paramref name="segment"/>:<paramref name="offset"/>
    /// are not <paramref name="text"/> followed by <paramref name="terminator"/>. The check runs
    /// before any register is written and reads with no memory breakpoint, so the service then
    /// reads each byte exactly once, as in the interpreter.
    /// </summary>
    /// <param name="segment">The segment of the checked string.</param>
    /// <param name="offset">The offset of the checked string.</param>
    /// <param name="text">The expected bytes before the terminator.</param>
    /// <param name="terminator">The expected byte right after the text.</param>
    /// <exception cref="UnrecoverableException">A byte differs from the expected one.</exception>
    protected void CheckConstantString(ushort segment, ushort offset, string text, byte terminator) {
        for (int i = 0; i <= text.Length; i++) {
            byte expected = i < text.Length ? (byte)text[i] : terminator;
            uint address = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + i));
            byte actual = _helper.Memory.SneakilyRead(
                _helper.Memory.A20Gate.TransformAddress(address));
            if (actual != expected) {
                throw _helper.FailAsUntested(
                    $"Constant string byte {i} at {segment:X4}:{offset + i:X4}: "
                    + $"expected 0x{expected:X2}, found 0x{actual:X2}");
            }
        }
    }

    /// <summary>The declared entry of the service named <paramref name="serviceName"/>.</summary>
    protected InterruptServiceEntry EntryOf(string serviceName) {
        switch (Handler.Services) {
            case HandlerServices.SingleService single:
                return single.Service;
            case HandlerServices.SelectedServices selected:
                foreach (InterruptServiceEntry entry in selected.Services.Values) {
                    if (entry.Name == serviceName) {
                        return entry;
                    }
                }
                break;
        }
        throw new InvalidOperationException(
            $"{Handler.GetType().Name}: no declared service named {serviceName}");
    }

    /// <summary>
    /// Writes the selector register the handler's services declare for the service named
    /// <paramref name="serviceName"/> and performs the interrupt call on the stub.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the interrupt call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the interrupt call.</param>
    /// <param name="serviceName">The name of the called service (the declaring method's name).</param>
    protected void Call(ushort expectedReturnCs, ushort expectedReturnIp, string serviceName) {
        InterruptServiceEntry entry = EntryOf(serviceName);
        if (Handler.Services is HandlerServices.SelectedServices selected) {
            switch (selected.Selector) {
                case ServiceSelector.Ah:
                    State.AH = (byte)entry.SelectorValue;
                    break;
                case ServiceSelector.Al:
                    State.AL = (byte)entry.SelectorValue;
                    break;
                case ServiceSelector.Ax:
                    State.AX = (ushort)entry.SelectorValue;
                    break;
            }
        }
        _helper.InterruptServiceCall(expectedReturnCs, expectedReturnIp, Handler.VectorNumber);
    }

    /// <summary>
    /// Runs the service named <paramref name="serviceName"/> by invoking
    /// <paramref name="invoke"/> inside an <c>int</c>-equivalent frame, and returns its result.
    /// </summary>
    /// <typeparam name="T">The result type of the service implementation.</typeparam>
    protected T CallDirect<T>(ushort expectedReturnCs, ushort expectedReturnIp,
        string serviceName, Func<T> invoke) {
        return _helper.DirectInterruptServiceCall(expectedReturnCs, expectedReturnIp,
            Handler, EntryOf(serviceName), invoke, _ => { });
    }

    /// <summary>
    /// Runs the service named <paramref name="serviceName"/> by invoking
    /// <paramref name="invoke"/> inside an <c>int</c>-equivalent frame, writes the Carry bit of
    /// the stacked flags from <paramref name="carry"/>, and returns the result.
    /// </summary>
    /// <typeparam name="T">The result type of the service implementation.</typeparam>
    protected T CallDirectWithCarry<T>(ushort expectedReturnCs, ushort expectedReturnIp,
        string serviceName, Func<T> invoke, Func<T, bool?> carry) {
        return _helper.DirectInterruptServiceCall(expectedReturnCs, expectedReturnIp,
            Handler, EntryOf(serviceName), invoke,
            result => {
                if (carry(result) is { } carryFlag) {
                    _helper.Stack.SetFlagOnInterruptStack(Flags.Carry, carryFlag);
                }
            });
    }

    /// <summary>
    /// Runs the service named <paramref name="serviceName"/> by invoking
    /// <paramref name="invoke"/> inside an <c>int</c>-equivalent frame.
    /// </summary>
    protected void CallDirect(ushort expectedReturnCs, ushort expectedReturnIp,
        string serviceName, Action invoke) {
        _helper.DirectInterruptServiceCall(expectedReturnCs, expectedReturnIp,
            Handler, EntryOf(serviceName), () => {
                invoke();
                return true;
            }, _ => { });
    }
}
