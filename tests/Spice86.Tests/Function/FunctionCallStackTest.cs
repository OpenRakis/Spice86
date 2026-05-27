namespace Spice86.Tests.Function;

using FluentAssertions;

using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class FunctionCallStackTest {
    [Fact]
    public void CurrentReturnsLastPushedCall() {
        FunctionCallStack stack = new();
        FunctionCall firstCall = CreateCall(0x1000, 0x0100, 0x0200);
        FunctionCall secondCall = CreateCall(0x2000, 0x0200, 0x0300);

        stack.Push(firstCall);
        stack.Push(secondCall);

        stack.Current.Should().Be(secondCall);
        stack.Count.Should().Be(2);
    }

    [Fact]
    public void FindReturnMatchReturnsTopMatchFirst() {
        FunctionCallStack stack = new();
        FunctionCall olderCall = CreateCall(0x1000, 0x0100, 0x0200);
        FunctionCall newerCall = CreateCall(0x2000, 0x0200, 0x0200);

        stack.Push(olderCall);
        stack.Push(newerCall);

        FunctionCallStackMatch? match = stack.FindReturnMatch(ReturnAddress(0x0200));

        match.Should().NotBeNull();
        match.Value.FunctionCall.Should().Be(newerCall);
        match.Value.IsTop.Should().BeTrue();
    }

    [Fact]
    public void PopThroughRemovesMatchedCallAndNewerCalls() {
        FunctionCallStack stack = new();
        FunctionCall firstCall = CreateCall(0x1000, 0x0100, 0x0101);
        FunctionCall secondCall = CreateCall(0x2000, 0x0200, 0x0201);
        FunctionCall thirdCall = CreateCall(0x3000, 0x0300, 0x0301);

        stack.Push(firstCall);
        stack.Push(secondCall);
        stack.Push(thirdCall);
        FunctionCallStackMatch? match = stack.FindReturnMatch(ReturnAddress(0x0201));

        match.Should().NotBeNull();
        FunctionCallStackMatch nonNullMatch = match.Value;
        stack.PopThrough(nonNullMatch);

        stack.Count.Should().Be(1);
        stack.Current.Should().Be(firstCall);
    }

    [Fact]
    public void FindReturnMatchReturnsNullWhenNoExpectedReturnMatches() {
        FunctionCallStack stack = new();
        FunctionCall firstCall = CreateCall(0x1000, 0x0100, 0x0101);
        FunctionCall secondCall = CreateCall(0x2000, 0x0200, 0x0201);

        stack.Push(firstCall);
        stack.Push(secondCall);

        FunctionCallStackMatch? match = stack.FindReturnMatch(ReturnAddress(0x0301));

        match.Should().BeNull();
        stack.Count.Should().Be(2);
        stack.Current.Should().Be(secondCall);
    }

    private static FunctionCall CreateCall(ushort entryOffset, ushort stackOffset, ushort expectedReturnOffset) {
        return new FunctionCall(
            CallType.NEAR16,
            new SegmentedAddress(0xF000, entryOffset),
            ReturnAddress(expectedReturnOffset),
            new SegmentedAddress(0x3000, stackOffset),
            null);
    }

    private static SegmentedAddress ReturnAddress(ushort offset) {
        return new SegmentedAddress(0xF000, offset);
    }
}