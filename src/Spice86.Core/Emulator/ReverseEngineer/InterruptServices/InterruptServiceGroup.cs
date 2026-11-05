namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Utils;

/// <summary>
/// Base class of the typed interrupt service helpers generated code calls instead of
/// <see cref="CSharpOverrideHelper.InterruptCall(ushort, ushort, byte)"/>: one subclass per
/// interrupt vector, one method per service. Each method checks the vector still points
/// to the built-in handler's stub, writes its parameters into the registers they read,
/// writes the selector register and performs the interrupt call. Outputs stay in the
/// registers and flags, as after the <c>int</c> instruction.
/// </summary>
public abstract class InterruptServiceGroup {
    private readonly CSharpOverrideHelper _helper;
    private readonly byte _vector;
    private readonly ServiceSelector? _selector;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterruptServiceGroup"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt call goes through.</param>
    /// <param name="vector">The interrupt vector number of the group.</param>
    /// <param name="selector">The selector register of the vector's handler, or
    /// <see langword="null"/> for a selector-less vector.</param>
    protected InterruptServiceGroup(CSharpOverrideHelper helper, byte vector, ServiceSelector? selector) {
        _helper = helper;
        _vector = vector;
        _selector = selector;
    }

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
        _helper.CheckInterruptServiceVector(_vector);
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

    /// <summary>
    /// Writes the selector register for <paramref name="selectorValue"/> and performs
    /// the interrupt call.
    /// </summary>
    /// <param name="expectedReturnCs">The expected value of the CS register after the interrupt call.</param>
    /// <param name="expectedReturnIp">The expected value of the IP register after the interrupt call.</param>
    /// <param name="selectorValue">The value of the selector register for the called service.</param>
    protected void Call(ushort expectedReturnCs, ushort expectedReturnIp, int selectorValue) {
        switch (_selector) {
            case ServiceSelector.Ah:
                State.AH = (byte)selectorValue;
                break;
            case ServiceSelector.Al:
                State.AL = (byte)selectorValue;
                break;
            case ServiceSelector.Ax:
                State.AX = (ushort)selectorValue;
                break;
        }
        _helper.InterruptServiceCall(expectedReturnCs, expectedReturnIp, _vector);
    }
}
