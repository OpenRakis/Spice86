namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Tests of <see cref="ObservedCallPreservation"/>: the registers a callee gives back unchanged
/// on balanced returns are recorded per callee entry, and nothing else is.
/// </summary>
public class ObservedCallPreservationTest : SpeculativeTestBase {
    private static readonly SegmentedAddress Callee = new(0xF000, 0x0200);
    private const ushort AllButAx = (ushort)(RegisterBits.All & ~RegisterBits.Ax);

    private readonly ObservedCallPreservation _preservation;

    public ObservedCallPreservationTest() {
        _preservation = new ObservedCallPreservation(State);
        State.SS = 0x1000;
        State.SP = 0x0100;
    }

    private void CallCalleeOnce(Action inside, ushort extraPop = 0) {
        CfgInstruction continuation = EnterObservedCall(_preservation, 0x0100, Callee.Offset);
        inside();
        ReturnFromObservedCall(_preservation, continuation, extraPop);
    }

    [Fact]
    public void CalleeWritingAxTwiceKeepsEveryOtherRegister() {
        CallCalleeOnce(() => State.AX = 1);
        CallCalleeOnce(() => State.AX = 2);

        Assert.True(_preservation.TryGetPreserved(Callee, out ushort preserved, out int count));
        Assert.Equal(AllButAx, preserved);
        Assert.Equal(2, count);
    }

    [Fact]
    public void RegisterChangedOnOneReturnInThreeIsNotPreserved() {
        CallCalleeOnce(() => { });
        CallCalleeOnce(() => State.DI = (ushort)(State.DI + 1));
        CallCalleeOnce(() => { });

        Assert.True(_preservation.TryGetPreserved(Callee, out ushort preserved, out int count));
        Assert.Equal((ushort)(RegisterBits.All & ~RegisterBits.Di), preserved);
        Assert.Equal(3, count);
    }

    [Fact]
    public void UnbalancedReturnRecordsNothing() {
        CallCalleeOnce(() => { }, extraPop: 2);

        Assert.False(_preservation.TryGetPreserved(Callee, out _, out _));
    }

    [Fact]
    public void StaleFrameIsDroppedAtTheNextCall() {
        SegmentedAddress outer = new(0xF000, 0x0300);
        CfgInstruction outerContinuation = EnterObservedCall(_preservation, 0x0100, outer.Offset);
        EnterObservedCall(_preservation, 0x0310, 0x0400);
        // The inner callee unwinds back into the outer one without reaching its continuation.
        State.SP = (ushort)(State.SP + 2);
        State.IP = 0x0320;
        CfgInstruction nextContinuation = EnterObservedCall(_preservation, 0x0320, 0x0500);
        ReturnFromObservedCall(_preservation, nextContinuation);
        State.AX = 7;
        ReturnFromObservedCall(_preservation, outerContinuation);

        Assert.True(_preservation.TryGetPreserved(outer, out ushort preserved, out int count));
        Assert.Equal(AllButAx, preserved);
        Assert.Equal(1, count);
    }

    [Fact]
    public void RemovedCalleeEntryForgetsItsEvidence() {
        SpeculativeExplorer explorer = CreateExplorer();
        ObservedCallPreservation preservation = explorer.CallPreservation;
        CfgInstruction entry = CreateObservedNode(Callee);
        CfgInstruction continuation = EnterObservedCall(preservation, 0x0100, Callee.Offset);
        ReturnFromObservedCall(preservation, continuation);
        Assert.True(preservation.TryGetPreserved(Callee, out _, out _));

        ReplacerRegistry.RemoveInstruction(entry);

        Assert.False(preservation.TryGetPreserved(Callee, out _, out _));
    }
}
