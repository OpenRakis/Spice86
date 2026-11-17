namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Xunit;

/// <summary>
/// Tests of <see cref="CalleeReturnScanner"/> for the <c>push bp; mov bp,sp</c> frame: <c>mov sp,bp</c>
/// restores the stack only while BP still holds the frame.
/// </summary>
public class CalleeReturnScannerTest : SpeculativeTestBase {
    private CalleeReturnScanner.Outcome Scan(ushort entry) =>
        new CalleeReturnScanner(NodeIndex).Scan(At(entry), _ => true);

    [Fact]
    public void FrameRestoredFromBpReturnsBalanced() {
        // push bp; mov bp,sp; sub sp,4; mov sp,bp; pop bp; ret
        DecodeRoutine(0x100, 0x55, 0x89, 0xE5, 0x83, 0xEC, 0x04, 0x89, 0xEC, 0x5D, 0xC3);
        Assert.Equal(CalleeReturnScanner.Outcome.Near, Scan(0x100));
    }

    [Fact]
    public void BpReloadedFromAnotherRegisterBreaksTheFrame() {
        // push bp; mov bp,sp; mov bp,ax; mov sp,bp; pop bp; ret - mov sp,bp reads AX, not the frame
        DecodeRoutine(0x100, 0x55, 0x89, 0xE5, 0x89, 0xC5, 0x89, 0xEC, 0x5D, 0xC3);
        Assert.Equal(CalleeReturnScanner.Outcome.Refused, Scan(0x100));
    }
}
