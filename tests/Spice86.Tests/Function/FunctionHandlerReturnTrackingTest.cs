namespace Spice86.Tests.Function;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Linq;

using Xunit;

public class FunctionHandlerReturnTrackingTest {
    [Theory]
    [InlineData(CallType.NEAR16)]
    [InlineData(CallType.NEAR32)]
    [InlineData(CallType.FAR16)]
    [InlineData(CallType.FAR32)]
    [InlineData(CallType.INTERRUPT)]
    [InlineData(CallType.EXTERNAL_INTERRUPT)]
    public void RetToDeeperMatchingFrameRecordsUnalignedReturnAndPopsThroughMatch(CallType callType) {
        TestContext context = CreateContext();
        SegmentedAddress firstEntry = EntryAddress(0x1000);
        SegmentedAddress secondEntry = EntryAddress(0x2000);
        SegmentedAddress thirdEntry = EntryAddress(0x3000);

        context.FunctionHandler.Call(callType, firstEntry, ReturnAddress(0x0101), null, "first");
        context.FunctionHandler.Call(callType, secondEntry, ReturnAddress(0x0201), null, "second");
        context.FunctionHandler.Call(callType, thirdEntry, ReturnAddress(0x0301), null, "third");
        // The callee pushed extra data, so the stack no longer sits at any frame's post-call level.
        context.State.SP = (ushort)(context.State.SP - 2);
        WriteReturnAddressOnStack(context, callType, ReturnAddress(0x0201));

        context.FunctionHandler.Ret(callType, null);

        FunctionInformation thirdFunction = context.FunctionCatalogue.FunctionInformations[thirdEntry];
        FunctionInformation secondFunction = context.FunctionCatalogue.FunctionInformations[secondEntry];
        thirdFunction.UnalignedReturns.Should().ContainSingle();
        secondFunction.Returns.Should().BeEmpty();
        context.FunctionHandler.DumpCallStack().Should().Contain("first").And.NotContain("second").And.NotContain("third");
    }

    [Theory]
    [InlineData(CallType.NEAR16)]
    [InlineData(CallType.NEAR32)]
    [InlineData(CallType.FAR16)]
    [InlineData(CallType.FAR32)]
    [InlineData(CallType.INTERRUPT)]
    [InlineData(CallType.EXTERNAL_INTERRUPT)]
    public void RetWithoutMatchingFrameRecordsUnalignedReturnAndKeepsTrackerCandidates(CallType callType) {
        TestContext context = CreateContext();
        SegmentedAddress firstEntry = EntryAddress(0x1000);
        SegmentedAddress secondEntry = EntryAddress(0x2000);

        context.FunctionHandler.Call(callType, firstEntry, ReturnAddress(0x0101), null, "first");
        context.FunctionHandler.Call(callType, secondEntry, ReturnAddress(0x0201), null, "second");
        // The callee pushed extra data, so the stack no longer sits at any frame's post-call level.
        context.State.SP = (ushort)(context.State.SP - 2);
        WriteReturnAddressOnStack(context, callType, ReturnAddress(0x0301));

        context.FunctionHandler.Ret(callType, null);

        FunctionInformation secondFunction = context.FunctionCatalogue.FunctionInformations[secondEntry];
        secondFunction.UnalignedReturns.Should().ContainSingle();
        context.FunctionHandler.DumpCallStack().Should().Contain("first").And.Contain("second");
    }

    [Theory]
    [InlineData(CallType.NEAR16)]
    [InlineData(CallType.NEAR32)]
    [InlineData(CallType.FAR16)]
    [InlineData(CallType.FAR32)]
    [InlineData(CallType.INTERRUPT)]
    [InlineData(CallType.EXTERNAL_INTERRUPT)]
    public void RetWithMatchingReturnAddressRecordsAlignedReturn(CallType callType) {
        // Arrange
        TestContext context = CreateContext();
        SegmentedAddress callerEntry = EntryAddress(0x1000);
        SegmentedAddress calleeEntry = EntryAddress(0x2000);
        SegmentedAddress expectedReturn = ReturnAddress(0x0101);

        context.FunctionHandler.Call(callType, callerEntry, ReturnAddress(0x0001), null, "caller");
        context.FunctionHandler.Call(callType, calleeEntry, expectedReturn, null, "callee");
        WriteReturnAddressOnStack(context, callType, expectedReturn);

        // Act
        context.FunctionHandler.Ret(callType, null);

        // Assert
        FunctionInformation calleeFunction = context.FunctionCatalogue.FunctionInformations[calleeEntry];
        calleeFunction.Returns.Should().ContainSingle();
        calleeFunction.UnalignedReturns.Should().BeEmpty();
        context.FunctionHandler.DumpCallStack().Should().Contain("caller").And.NotContain("callee");
    }

    [Fact]
    public void InterruptRetToRewrittenStackedIpPopsFrameAndRecordsRedirectedCorrespondingCall() {
        // Arrange: an int call whose handler moved the stacked return address one byte past the
        // expected return, with the stack back at its call-time level.
        TestContext context = CreateContext();
        SegmentedAddress handlerEntry = EntryAddress(0x2000);
        CfgInstruction intCall = CreateInstruction(0x1000, 0xCD, InstructionKind.Call);
        CfgInstruction iret = CreateInstruction(0x3000, 0xCF, InstructionKind.Return);

        context.FunctionHandler.Call(CallType.NEAR16, EntryAddress(0x0500), ReturnAddress(0x0001), null, "caller");
        context.FunctionHandler.Call(CallType.INTERRUPT, handlerEntry, ReturnAddress(0x1002), intCall, "handler");
        WriteReturnAddressOnStack(context, CallType.INTERRUPT, ReturnAddress(0x1003));

        // Act
        context.FunctionHandler.Ret(CallType.INTERRUPT, iret);

        // Assert
        context.FunctionHandler.DumpCallStack().Should().Contain("caller").And.NotContain("handler");
        CorrespondingCall? corresponding = iret.CurrentCorrespondingCall;
        corresponding.Should().NotBeNull();
        corresponding!.Value.Call.Should().BeSameAs(intCall);
        corresponding.Value.Type.Should().Be(InstructionSuccessorType.CallToRedirectedReturn);
        context.FunctionCatalogue.FunctionInformations[handlerEntry].UnalignedReturns.Should().ContainSingle();
    }

    [Fact]
    public void RepeatedRedirectedReturnsDoNotAccumulateCallStackDepth() {
        // Arrange: the same redirected return 1100 times, past the depth limit that reset the
        // tracker when redirected returns were never popped.
        TestContext context = CreateContext();
        CfgInstruction intCall = CreateInstruction(0x1000, 0xCD, InstructionKind.Call);
        CfgInstruction iret = CreateInstruction(0x3000, 0xCF, InstructionKind.Return);
        context.FunctionHandler.Call(CallType.NEAR16, EntryAddress(0x0500), ReturnAddress(0x0001), null, "caller");

        // Act
        for (int i = 0; i < 1100; i++) {
            context.FunctionHandler.Call(CallType.INTERRUPT, EntryAddress(0x2000), ReturnAddress(0x1002), intCall, "handler");
            WriteReturnAddressOnStack(context, CallType.INTERRUPT, ReturnAddress(0x1003));
            context.FunctionHandler.Ret(CallType.INTERRUPT, iret);
        }

        // Assert: the base frame survives, so no depth-exceeded reset wiped the stack.
        context.FunctionHandler.DumpCallStack().Should().Contain("caller").And.NotContain("handler");
    }

    [Fact]
    public void NearRetToUnrelatedAddressWithExtraWordOnStackKeepsFrameAndLinksNoCall() {
        // Arrange: a near call, then an extra word pushed below the frame's post-call level, then a
        // ret to an address no frame expects.
        TestContext context = CreateContext();
        SegmentedAddress calleeEntry = EntryAddress(0x2000);
        CfgInstruction nearCall = CreateInstruction(0x1000, 0xE8, InstructionKind.Call);
        CfgInstruction retInstruction = CreateInstruction(0x3000, 0xC3, InstructionKind.Return);

        context.FunctionHandler.Call(CallType.NEAR16, calleeEntry, ReturnAddress(0x1003), nearCall, "callee");
        context.State.SP = (ushort)(context.State.SP - 2);
        WriteReturnAddressOnStack(context, CallType.NEAR16, ReturnAddress(0x4000));

        // Act
        context.FunctionHandler.Ret(CallType.NEAR16, retInstruction);

        // Assert
        context.FunctionHandler.DumpCallStack().Should().Contain("callee");
        retInstruction.CurrentCorrespondingCall.Should().BeNull();
        context.FunctionCatalogue.FunctionInformations[calleeEntry].UnalignedReturns.Should().ContainSingle();
    }

    [Fact]
    public void StackMatchedDeeperFramePopsThroughItWithRedirectedCorrespondingCall() {
        // Arrange: two near calls, then the stack back at the outer frame's post-call level and a
        // return address that frame does not expect.
        TestContext context = CreateContext();
        CfgInstruction outerCall = CreateInstruction(0x1000, 0xE8, InstructionKind.Call);
        CfgInstruction innerCall = CreateInstruction(0x1010, 0xE8, InstructionKind.Call);
        CfgInstruction retInstruction = CreateInstruction(0x3000, 0xC3, InstructionKind.Return);
        SegmentedAddress outerEntry = EntryAddress(0x2000);

        context.FunctionHandler.Call(CallType.NEAR16, outerEntry, ReturnAddress(0x1003), outerCall, "outer");
        context.State.SP = (ushort)(context.State.SP - 2);
        context.FunctionHandler.Call(CallType.NEAR16, EntryAddress(0x3000), ReturnAddress(0x1013), innerCall, "inner");
        context.State.SP = (ushort)(context.State.SP + 2);
        WriteReturnAddressOnStack(context, CallType.NEAR16, ReturnAddress(0x4000));

        // Act
        context.FunctionHandler.Ret(CallType.NEAR16, retInstruction);

        // Assert
        context.FunctionHandler.DumpCallStack().Should().NotContain("outer").And.NotContain("inner");
        CorrespondingCall? corresponding = retInstruction.CurrentCorrespondingCall;
        corresponding.Should().NotBeNull();
        corresponding!.Value.Call.Should().BeSameAs(outerCall);
        corresponding.Value.Type.Should().Be(InstructionSuccessorType.CallToRedirectedReturn);
    }

    [Fact]
    public void RedirectedStackMatchedReturnLogsNoWarning() {
        // Arrange: a stack-matched return to another address is a redirected return, not a lost frame.
        TestContext context = CreateContext();
        SegmentedAddress handlerEntry = EntryAddress(0x2000);
        CfgInstruction intCall = CreateInstruction(0x1000, 0xCD, InstructionKind.Call);
        CfgInstruction iret = CreateInstruction(0x3000, 0xCF, InstructionKind.Return);

        context.FunctionHandler.Call(CallType.INTERRUPT, handlerEntry, ReturnAddress(0x1002), intCall, "handler");
        WriteReturnAddressOnStack(context, CallType.INTERRUPT, ReturnAddress(0x1003));

        // Act
        context.FunctionHandler.Ret(CallType.INTERRUPT, iret);

        // Assert
        WarningLogCalls(context.LoggerService).Should().Be(0);
    }

    [Fact]
    public void ReturnMatchingNoFrameStillLogsWarning() {
        // Arrange: a near call, then an extra word pushed below the frame's post-call level, then a
        // ret to an address no frame expects.
        TestContext context = CreateContext();
        context.FunctionHandler.Call(CallType.NEAR16, EntryAddress(0x2000), ReturnAddress(0x1003), null, "callee");
        context.State.SP = (ushort)(context.State.SP - 2);
        WriteReturnAddressOnStack(context, CallType.NEAR16, ReturnAddress(0x4000));

        // Act
        context.FunctionHandler.Ret(CallType.NEAR16, null);

        // Assert
        WarningLogCalls(context.LoggerService).Should().Be(1);
    }

    private static int WarningLogCalls(ILogger logger) =>
        logger.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(ILogger.Log)
            && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning);

    [Fact]
    public void PushRetJumpAtStaleFrameLevelMatchesNothingAndRealRetStaysAligned() {
        // Arrange: A and A2 are stale frames left behind when A2 reset the stack pointer and jumped
        // back to the main loop without returning; B is the live frame of the next iteration.
        // Tracked stack bottom to top: [A(0x0102), A2(0x0100), B(0x0102)].
        TestContext context = CreateContext();
        CfgInstruction callA = CreateInstruction(0x1000, 0xE8, InstructionKind.Call);
        CfgInstruction callA2 = CreateInstruction(0x2000, 0xE8, InstructionKind.Call);
        CfgInstruction callB = CreateInstruction(0x1100, 0xE8, InstructionKind.Call);
        CfgInstruction bJumpRet = CreateInstruction(0x4000, 0xC3, InstructionKind.Return);
        CfgInstruction bRealRet = CreateInstruction(0x4100, 0xC3, InstructionKind.Return);
        SegmentedAddress bExpectedReturn = ReturnAddress(0x1103);

        context.State.SP = 0x0102;
        context.FunctionHandler.Call(CallType.NEAR16, EntryAddress(0x2000), ReturnAddress(0x1003), callA, "outer_a");
        context.State.SP = 0x0100;
        context.FunctionHandler.Call(CallType.NEAR16, EntryAddress(0x3000), ReturnAddress(0x2003), callA2, "inner_a2");
        // A2 resets the stack pointer and jumps away without returning.
        context.State.SP = 0x0102;
        context.FunctionHandler.Call(CallType.NEAR16, EntryAddress(0x4000), bExpectedReturn, callB, "live_b");
        // B jumps inside itself with `push label; ret`, landing at the stale A2 level.
        context.State.SP = (ushort)(context.State.SP - 2);
        WriteReturnAddressOnStack(context, CallType.NEAR16, ReturnAddress(0x4200));

        // Act: B's push/ret is a return to an address no frame expects, at the stale A2 level.
        context.FunctionHandler.Ret(CallType.NEAR16, bJumpRet);

        // Assert: the stale A2 frame does not match, nothing is popped, no call is linked.
        bJumpRet.CurrentCorrespondingCall.Should().BeNull();
        context.FunctionHandler.DumpCallStack().Should().Contain("outer_a").And.Contain("inner_a2").And.Contain("live_b");

        // Act: B's real ret returns to the address B's call expects, with SP back at the frame level.
        context.State.SP = 0x0102;
        WriteReturnAddressOnStack(context, CallType.NEAR16, bExpectedReturn);
        context.FunctionHandler.Ret(CallType.NEAR16, bRealRet);

        // Assert: aligned return on B's call, only B's frame is popped.
        CorrespondingCall? corresponding = bRealRet.CurrentCorrespondingCall;
        corresponding.Should().NotBeNull();
        corresponding!.Value.Call.Should().BeSameAs(callB);
        corresponding.Value.Type.Should().Be(InstructionSuccessorType.CallToReturn);
        context.FunctionHandler.DumpCallStack().Should().Contain("outer_a").And.Contain("inner_a2").And.NotContain("live_b");
    }

    private static CfgInstruction CreateInstruction(ushort offset, byte opcode, InstructionKind kind) =>
        CfgCpu.Blocks.CfgTestHelpers.CreateInstruction(new SegmentedAddress(0xF000, offset), opcode, 1, kind);

    private static void WriteReturnAddressOnStack(TestContext context, CallType callType, SegmentedAddress returnAddress) {
        uint stackAddress = context.State.StackPhysicalAddress;
        switch (callType) {
            case CallType.NEAR16:
                context.Memory.UInt16[stackAddress] = returnAddress.Offset;
                break;
            case CallType.NEAR32:
                context.Memory.UInt32[stackAddress] = returnAddress.Offset;
                break;
            case CallType.FAR16:
            case CallType.INTERRUPT:
            case CallType.EXTERNAL_INTERRUPT:
                context.Memory.SegmentedAddress16[stackAddress] = returnAddress;
                break;
            case CallType.FAR32:
                context.Memory.SegmentedAddress32[stackAddress] = new SegmentedAddress32(returnAddress.Segment, returnAddress.Offset);
                break;
        }
    }

    private static TestContext CreateContext() {
        Memory memory = new(new AddressReadWriteBreakpoints(), new Ram(A20Gate.EndOfHighMemoryArea), new A20Gate(), new RealModeMmu386(), false);
        State state = new(CpuModel.INTEL_80286) {
            CS = 0xF000,
            IP = 0x7777,
            SS = 0x3000,
            SP = 0x0100
        };
        FunctionCatalogue functionCatalogue = new();
        ILogger loggerService = Substitute.For<ILogger>();
        loggerService.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        FunctionHandler functionHandler = new(memory, state, functionCatalogue, false, loggerService);
        return new TestContext(memory, state, functionCatalogue, functionHandler, loggerService);
    }

    private static SegmentedAddress EntryAddress(ushort offset) {
        return new SegmentedAddress(0xF000, offset);
    }

    [Fact]
    public void CallRecordsActualCallerNotSelf() {
        // Arrange
        TestContext context = CreateContext();
        SegmentedAddress firstEntry = EntryAddress(0x1000);
        SegmentedAddress secondEntry = EntryAddress(0x2000);

        // Act
        context.FunctionHandler.Call(CallType.NEAR16, firstEntry, ReturnAddress(0x0101), null, "first");
        context.FunctionHandler.Call(CallType.NEAR16, secondEntry, ReturnAddress(0x0201), null, "second");

        // Assert
        FunctionInformation firstFunction = context.FunctionCatalogue.FunctionInformations[firstEntry];
        FunctionInformation secondFunction = context.FunctionCatalogue.FunctionInformations[secondEntry];

        secondFunction.Callers.Should().ContainSingle()
            .Which.Should().BeSameAs(firstFunction);
        secondFunction.Callers.Should().NotContain(secondFunction, "a function must not record itself as its own caller");
    }

    [Fact]
    public void FirstCallHasNoCaller() {
        // Arrange
        TestContext context = CreateContext();
        SegmentedAddress firstEntry = EntryAddress(0x1000);

        // Act
        context.FunctionHandler.Call(CallType.NEAR16, firstEntry, ReturnAddress(0x0101), null, "first");

        // Assert
        FunctionInformation firstFunction = context.FunctionCatalogue.FunctionInformations[firstEntry];
        firstFunction.Callers.Should().BeEmpty("the first call has no caller on the stack");
        firstFunction.CalledCount.Should().Be(1);
    }

    [Fact]
    public void CallChainRecordsCorrectCallers() {
        // Arrange
        TestContext context = CreateContext();
        SegmentedAddress firstEntry = EntryAddress(0x1000);
        SegmentedAddress secondEntry = EntryAddress(0x2000);
        SegmentedAddress thirdEntry = EntryAddress(0x3000);

        // Act
        context.FunctionHandler.Call(CallType.NEAR16, firstEntry, ReturnAddress(0x0101), null, "first");
        context.FunctionHandler.Call(CallType.NEAR16, secondEntry, ReturnAddress(0x0201), null, "second");
        context.FunctionHandler.Call(CallType.NEAR16, thirdEntry, ReturnAddress(0x0301), null, "third");

        // Assert
        FunctionInformation firstFunction = context.FunctionCatalogue.FunctionInformations[firstEntry];
        FunctionInformation secondFunction = context.FunctionCatalogue.FunctionInformations[secondEntry];
        FunctionInformation thirdFunction = context.FunctionCatalogue.FunctionInformations[thirdEntry];

        firstFunction.Callers.Should().BeEmpty("the first call has no caller");
        secondFunction.Callers.Should().ContainSingle().Which.Should().BeSameAs(firstFunction);
        thirdFunction.Callers.Should().ContainSingle().Which.Should().BeSameAs(secondFunction);
    }

    private static SegmentedAddress ReturnAddress(ushort offset) {
        return new SegmentedAddress(0xF000, offset);
    }

    private sealed record TestContext(
        Memory Memory,
        State State,
        FunctionCatalogue FunctionCatalogue,
        FunctionHandler FunctionHandler,
        ILogger LoggerService);
}