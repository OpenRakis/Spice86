namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Stack helpers the generated override code calls: batched pushes, tuple pops and discarded pops,
/// printed as <c>Stack.Save(...)</c>, <c>(T1, T2) = Stack.Restore2();</c> and
/// <c>Stack.Release(n);</c>.
/// </summary>
public static class GeneratedCodeStackExtensions {
    /// <summary>Pushes the return address like the interpreter's call of that distance and
    /// operand size.</summary>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="distance">The call distance.</param>
    /// <param name="width">The call operand size.</param>
    /// <param name="returnAddress">The return address to push.</param>
    public static void PushReturnAddress(this Stack stack, CallDistance distance, BitWidth width,
        SegmentedAddress returnAddress) {
        switch ((distance, width)) {
            case (CallDistance.Near, BitWidth.WORD_16):
                stack.Push16(returnAddress.Offset);
                break;
            case (CallDistance.Near, BitWidth.DWORD_32):
                stack.Push32(returnAddress.Offset);
                break;
            case (CallDistance.Far, BitWidth.WORD_16):
                stack.PushSegmentedAddress(returnAddress);
                break;
            case (CallDistance.Far, BitWidth.DWORD_32):
                stack.PushFarPointer32(new SegmentedAddress32(returnAddress.Segment,
                    returnAddress.Offset));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(width), width,
                    "Unsupported call return address width");
        }
    }

    /// <summary>Pushes each value in order, like the register pushes that open a function.</summary>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="values">The values to push, in push order.</param>
    public static void Save(this Stack stack, params ReadOnlySpan<ushort> values) {
        foreach (ushort value in values) {
            stack.Push16(value);
        }
    }

    /// <summary>Pops two values, returning them in pop order.</summary>
    public static (ushort, ushort) Restore2(this Stack stack) => (stack.Pop16(), stack.Pop16());

    /// <summary>Pops three values, returning them in pop order.</summary>
    public static (ushort, ushort, ushort) Restore3(this Stack stack) =>
        (stack.Pop16(), stack.Pop16(), stack.Pop16());

    /// <summary>Pops four values, returning them in pop order.</summary>
    public static (ushort, ushort, ushort, ushort) Restore4(this Stack stack) =>
        (stack.Pop16(), stack.Pop16(), stack.Pop16(), stack.Pop16());

    /// <summary>Pops five values, returning them in pop order.</summary>
    public static (ushort, ushort, ushort, ushort, ushort) Restore5(this Stack stack) =>
        (stack.Pop16(), stack.Pop16(), stack.Pop16(), stack.Pop16(), stack.Pop16());

    /// <summary>Pops six values, returning them in pop order.</summary>
    public static (ushort, ushort, ushort, ushort, ushort, ushort) Restore6(this Stack stack) =>
        (stack.Pop16(), stack.Pop16(), stack.Pop16(), stack.Pop16(), stack.Pop16(), stack.Pop16());

    /// <summary>Pops and discards <paramref name="count"/> values, like the discarded pops of a
    /// restore nobody reads.</summary>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="count">The number of values to discard.</param>
    public static void Release(this Stack stack, int count) {
        for (int index = 0; index < count; index++) {
            stack.Pop16();
        }
    }
}
