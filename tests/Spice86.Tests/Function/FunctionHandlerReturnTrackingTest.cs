namespace Spice86.Tests.Function;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using Xunit;

public class FunctionHandlerReturnTrackingTest {
    [Theory]
    [InlineData(CallType.NEAR16, false)]
    [InlineData(CallType.NEAR16, true)]
    [InlineData(CallType.NEAR32, false)]
    [InlineData(CallType.NEAR32, true)]
    [InlineData(CallType.FAR16, false)]
    [InlineData(CallType.FAR16, true)]
    [InlineData(CallType.FAR32, false)]
    [InlineData(CallType.FAR32, true)]
    public void ProtectedReturnTrackingUsesCachedStackBaseAndActivePointerWidth(CallType callType, bool wideStack) {
        // Arrange
        const uint stackBase = 0x50000;
        State state = new(CpuModel.INTEL_80386) {
            CS = 0x10,
            SS = 0x18,
            ESP = 0x10100
        };
        state.ControlRegisters.ProtectionEnable = true;
        state.SegmentDescriptorCaches[SegmentRegisterIndex.SsIndex] =
            new SegmentDescriptorCache(stackBase, 0xFFFFF, 0x93, wideStack, false, true);
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        Memory memory = new(new AddressReadWriteBreakpoints(), ram, new A20Gate(),
            CpuMmuFactory.Create(CpuModel.INTEL_80386, state, ram), false);
        FunctionCatalogue catalogue = new();
        FunctionHandler handler = new(memory, state, catalogue, false, Substitute.For<ILogger>());
        TestContext context = new(memory, state, catalogue, handler);
        SegmentedAddress entry = new(state.CS, 0x2000);
        SegmentedAddress expectedReturn = new(state.CS, 0x0101);
        Stack stack = new(memory, state);
        handler.Call(callType, entry, expectedReturn, null, "callee");
        uint stackAddress = stackBase + stack.GetStackPointer();
        if (callType is CallType.FAR16 or CallType.FAR32) {
            memory.UInt32[stackAddress] = expectedReturn.Offset;
            uint selectorOffset = callType == CallType.FAR32 ? 4u : 2u;
            memory.UInt32[stackAddress + selectorOffset] = expectedReturn.Segment;
        } else {
            memory.UInt32[stackAddress] = expectedReturn.Offset;
        }

        // Act
        context.FunctionHandler.Ret(callType, null);

        // Assert
        catalogue.FunctionInformations[entry].Returns.Should().ContainSingle();
        catalogue.FunctionInformations[entry].UnalignedReturns.Should().BeEmpty();
        handler.DumpCallStack().Should().BeEmpty();
    }

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
        FunctionHandler functionHandler = new(memory, state, functionCatalogue, false, loggerService);
        return new TestContext(memory, state, functionCatalogue, functionHandler);
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
        FunctionHandler FunctionHandler);
}