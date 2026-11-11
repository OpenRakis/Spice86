using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Emulator.Errors;

namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

using EmulatedCallback = Spice86.Core.Emulator.InterruptHandlers.Common.Callback.Callback;

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

public class CSharpOverrideHelperTest {
    private const ushort TestCallbackNumber = 0x1000;
    private const ushort SecondTestCallbackNumber = 0x1001;

    private readonly ILogger _loggerServiceMock = Substitute.For<ILogger>();

    [Fact]
    public void ChainRunningAnOverrideBelowCallStackLevelLogsNoWarning() {
        // Arrange: the handler jumps to another override while the callee's frame is still on the
        // stack (like a `push label; ret` inside an interrupt handler). That landing runs the
        // override there, but it is the callee's own control flow, so only a Debug line is logged.
        _loggerServiceMock.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SegmentedAddress inner = new(0x2000, 0x0099);
        int innerRan = 0;
        FunctionInformation innerInformation =
            new(inner, "inner", _ => { innerRan++; return helper.InterruptRet(); })
            { ForbidsInterpreterFallback = true };
        dependencyInjection.FunctionCatalogue.FunctionInformations[inner] = innerInformation;

        // Act
        helper.InterruptCall(0x1000, 0x0010, 0x3456, _ => () => {
            machine.CpuState.CS = inner.Segment;
            machine.CpuState.IP = inner.Offset;
        });

        // Assert: the chain ran the inner override, which iret'ed back to the expected return address
        innerRan.Should().Be(1);
        machine.CpuState.CS.Should().Be(0x1000);
        machine.CpuState.IP.Should().Be(0x0010);
        WarningLogCalls().Should().Be(0);
    }

    [Fact]
    public void ChainRunningAnOverrideAtOrAboveCallStackLevelLogsOncePerPair() {
        // Arrange: the handler consumes its frame and lands at an override that is neither the
        // expected return nor a redirected return. The nested call is logged at Warning level, but
        // only once per (expected return, landing) pair; the second occurrence logs at Debug.
        _loggerServiceMock.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SegmentedAddress landing = new(0x2000, 0x0099);
        int landingRan = 0;
        FunctionInformation landingInformation =
            new(landing, "landing", _ => { landingRan++; return helper.FarJump(0x1000, 0x0010); })
            { ForbidsInterpreterFallback = true };
        dependencyInjection.FunctionCatalogue.FunctionInformations[landing] = landingInformation;

        // Act: the same call site runs the chain twice.
        for (int i = 0; i < 2; i++) {
            helper.InterruptCall(0x1000, 0x0010, 0x3456, _ => () => {
                helper.Stack.Pop16();
                helper.Stack.Pop16();
                helper.Stack.Pop16();
                machine.CpuState.CS = landing.Segment;
                machine.CpuState.IP = landing.Offset;
            });
        }

        // Assert
        landingRan.Should().Be(2);
        machine.CpuState.CS.Should().Be(0x1000);
        machine.CpuState.IP.Should().Be(0x0010);
        WarningLogCalls().Should().Be(1);
    }

    private int WarningLogCalls() =>
        _loggerServiceMock.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(ILogger.Log)
            && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning);

    [Fact]
    public void NearCall_ReturnToAncestorSkipsAbandonedContinuationAndRestoresDispatcher() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        CSharpOverrideHelper helper = new(
            new Dictionary<SegmentedAddress, FunctionInformation>(),
            res.Machine, _loggerServiceMock, new Configuration { HttpApiPort = 0 });
        helper.SS = 0x3000;
        helper.SP = 0x0100;
        JumpDispatcher originalDispatcher = helper.JumpDispatcher;
        bool abandonedContinuationReached = false;
        ushort callerCs = helper.CS;

        // Act
        helper.NearCall(callerCs, 0x0042, _ => {
            helper.NearCall(callerCs, 0x0078, _ => {
                helper.Stack.Pop16();
                return helper.NearRet();
            });
            abandonedContinuationReached = true;
            return helper.NearRet();
        });
        helper.NearCall(callerCs, 0x009A, _ => helper.NearRet());

        // Assert
        abandonedContinuationReached.Should().BeFalse();
        helper.CS.Should().Be(callerCs);
        helper.IP.Should().Be(0x009A);
        helper.SP.Should().Be(0x0100);
        helper.JumpDispatcher.Should().BeSameAs(originalDispatcher);
    }

    [Fact]
    public void TestJumpReturns() {
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        res.Machine.CpuState.SS = 0x3000;
        res.Machine.CpuState.SP = 0x100;
        Machine machine = res.Machine;
        RecursiveJumps recursiveJumps =
            new RecursiveJumps(new Dictionary<SegmentedAddress, FunctionInformation>(),
                machine,
                _loggerServiceMock, new Configuration { HttpApiPort = 0 });
        recursiveJumps.JumpTarget1(0);
        Assert.Equal(RecursiveJumps.MaxNumberOfJumps, recursiveJumps.NumberOfCallsTo1);
        Assert.Equal(RecursiveJumps.MaxNumberOfJumps, recursiveJumps.NumberOfCallsTo2);
    }

    [Fact]
    public void TestMutuallyRecursiveJumpsUnwindWithoutThrowing() {
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        res.Machine.CpuState.SS = 0x3000;
        res.Machine.CpuState.SP = 0x100;
        Machine machine = res.Machine;
        DeepRecursiveJumps deepRecursiveJumps =
            new DeepRecursiveJumps(new Dictionary<SegmentedAddress, FunctionInformation>(),
                machine,
                _loggerServiceMock, new Configuration { HttpApiPort = 0 });
        deepRecursiveJumps.JumpTarget1(0);
        Assert.Equal(DeepRecursiveJumps.MaxNumberOfJumps, deepRecursiveJumps.NumberOfCallsTo1);
        Assert.Equal(DeepRecursiveJumps.MaxNumberOfJumps, deepRecursiveJumps.NumberOfCallsTo2);
    }

    [Fact]
    public void TestSimpleCallsJumps() {
        using Spice86Creator creator = new Spice86Creator(binName: "jump2", overrideSupplierClassName: typeof(SimpleCallsJumpsOverrideSupplier).AssemblyQualifiedName);
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();
        spice86DependencyInjection.Machine.CpuState.SS = 0x3000;
        spice86DependencyInjection.Machine.CpuState.SP = 0x100;
        // Get the instance spice86 created. No elegant way of doing this from outside...
        SimpleCallsJumps? callsJumps = SimpleCallsJumps.CurrentInstance;
        // Reset it right away
        SimpleCallsJumps.CurrentInstance = null;
        spice86DependencyInjection.ProgramExecutor.Run();
        Assert.NotNull(callsJumps);
        Assert.Equal(1, callsJumps.EntryCalled);
        Assert.Equal(1, callsJumps.NearCalled);
        Assert.Equal(1, callsJumps.FarCalled);
        Assert.Equal(1, callsJumps.FarCalled1FromStack);
        Assert.Equal(1, callsJumps.FarCalled2FromStack);
    }

    [Fact]
    public void TestActualInCodeOverrides() {
        using Spice86Creator creator = new Spice86Creator(binName: "jump2", overrideSupplierClassName: typeof(VariousOverrideSupplier).AssemblyQualifiedName);
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();
        spice86DependencyInjection.Machine.CpuState.SS = 0x3000;
        spice86DependencyInjection.Machine.CpuState.SP = 0x100;
        // Get the instance spice86 created. No elegant way of doing this from outside...
        VariousOverrides? overrides = VariousOverrides.CurrentInstance;
        // Reset it right away
        VariousOverrides.CurrentInstance = null;
        spice86DependencyInjection.ProgramExecutor.Run();
        Assert.NotNull(overrides);
        Assert.Equal(1, overrides.FirstFunctionCalled);
        Assert.Equal(1, overrides.SecondFunctionCalled);
        Assert.Equal(1, overrides.ThirdFunctionCalled);
        Assert.Equal(1, overrides.FirstInstructionOverridenCalled);
        Assert.Equal(1, overrides.FirstDoOnTopOfInstructionCalled);
    }

    [Fact]
    public void DispatchCpuFault_FirstExactMatchEntersBeforeInvocationAndReturnsWithoutExecutingAction() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        machine.CpuState.CS = 0x2000;
        machine.CpuState.IP = 0x0042;
        machine.CpuState.InterruptFlag = true;
        ushort originalFlags = machine.CpuState.Flags.FlagRegister16;

        machine.InterruptVectorTable[0] = new SegmentedAddress(0x3456, 0x0078);

        CSharpOverrideHelper helper = new(
            new Dictionary<SegmentedAddress, FunctionInformation>(),
            machine,
            _loggerServiceMock,
            new Configuration { HttpApiPort = 0 });

        int invocationCounter = 0;
        int executionCounter = 0;
        Action markerAction = () => executionCounter++;

        List<CpuFaultTarget> targets = [
            new(new SegmentedAddress(0x3457, 0x0068), () => { invocationCounter++; return helper.NearRet(); }), // same physical address, different segmented
            new(new SegmentedAddress(0x3456, 0x0078), () => {
                invocationCounter++;
                // Fault entry assertions inside the matching delegate
                helper.CS.Should().Be(0x3456);
                helper.IP.Should().Be(0x0078);
                helper.InterruptFlag.Should().BeFalse();
                helper.SP.Should().Be(0x00FA);
                helper.Stack.Peek16(0).Should().Be(0x0042);
                helper.Stack.Peek16(2).Should().Be(0x2000);
                helper.Stack.Peek16(4).Should().Be(originalFlags);
                return markerAction;
            }),
            new(new SegmentedAddress(0x3456, 0x0078), () => { invocationCounter++; return helper.NearRet(); }) // duplicate, should never run
        ];

        CpuDivisionErrorException exception = new("Division by zero");

        // Act
        Action returnedAction = helper.DispatchCpuFault(exception, 0x2000, 0x0042, targets);

        // Assert
        returnedAction.Should().BeSameAs(markerAction);
        invocationCounter.Should().Be(1, "only the first matching delegate should run");
        executionCounter.Should().Be(0, "the returned action should not be executed");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DispatchCpuFault_UnmatchedAndEmptyListsLeaveMachineUntouched(bool useNonEmptyList) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        machine.CpuState.CS = 0x2000;
        machine.CpuState.IP = 0x0042;
        machine.CpuState.InterruptFlag = true;
        ushort originalFlags = machine.CpuState.Flags.FlagRegister16;
        ushort originalCs = machine.CpuState.CS;
        ushort originalIp = machine.CpuState.IP;
        ushort originalSp = machine.CpuState.SP;

        machine.InterruptVectorTable[0] = new SegmentedAddress(0x3456, 0x0078);

        CSharpOverrideHelper helper = new(
            new Dictionary<SegmentedAddress, FunctionInformation>(),
            machine,
            _loggerServiceMock,
            new Configuration { HttpApiPort = 0 });

        // Seed the would-be fault frame at SS:SP-6, SS:SP-4, SS:SP-2 (with initial SP=0x0100, these are 0x00FA, 0x00FC, 0x00FE)
        helper.Stack.Poke16(-6, 0xA1B2);
        helper.Stack.Poke16(-4, 0xC3D4);
        helper.Stack.Poke16(-2, 0xE5F6);
        ushort frameWord0 = helper.Stack.Peek16(-6);
        ushort frameWord2 = helper.Stack.Peek16(-4);
        ushort frameWord4 = helper.Stack.Peek16(-2);

        int invocationCounter = 0;
        List<CpuFaultTarget> targets;
        if (useNonEmptyList) {
            targets = [new(new SegmentedAddress(0x3457, 0x0068), () => { invocationCounter++; return helper.NearRet(); })]; // segmented alias, doesn't match
        } else {
            targets = [];
        }

        CpuDivisionErrorException exception = new("Division by zero");

        // Act & Assert
        Action act = () => helper.DispatchCpuFault(exception, 0x2000, 0x0042, targets);
        act.Should().Throw<UnrecoverableException>()
            .WithMessage("*Untested CPU fault target*")
            .WithMessage("*3456:0078*")
            .WithMessage("*2000:0042*");

        // Machine state should be unchanged
        helper.CS.Should().Be(originalCs);
        helper.IP.Should().Be(originalIp);
        helper.SP.Should().Be(originalSp);
        helper.State.Flags.FlagRegister16.Should().Be(originalFlags);
        helper.InterruptFlag.Should().BeTrue();

        // Invocation counter should be zero
        invocationCounter.Should().Be(0);

        // Would-be stack frame words should be unchanged
        helper.Stack.Peek16(-6).Should().Be(frameWord0);
        helper.Stack.Peek16(-4).Should().Be(frameWord2);
        helper.Stack.Peek16(-2).Should().Be(frameWord4);
    }

    [Fact]
    public void DispatchCpuFault_IvtIsReadAgainOnEachCall() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        machine.CpuState.CS = 0x2000;
        machine.CpuState.IP = 0x0042;
        machine.CpuState.InterruptFlag = true;

        CSharpOverrideHelper helper = new(
            new Dictionary<SegmentedAddress, FunctionInformation>(),
            machine,
            _loggerServiceMock,
            new Configuration { HttpApiPort = 0 });

        Action markerAction1 = () => { };
        Action markerAction2 = () => { };
        int invocationCounter1 = 0;
        int invocationCounter2 = 0;

        List<CpuFaultTarget> targets = [
            new(new SegmentedAddress(0x3456, 0x0078), () => { invocationCounter1++; return markerAction1; }),
            new(new SegmentedAddress(0x4567, 0x0089), () => { invocationCounter2++; return markerAction2; })
        ];

        CpuDivisionErrorException exception = new("Division by zero");

        // Act 1 - IVT points to first handler
        machine.InterruptVectorTable[0] = new SegmentedAddress(0x3456, 0x0078);
        Action returnedAction1 = helper.DispatchCpuFault(exception, 0x2000, 0x0042, targets);

        // Reset CPU state for second call
        machine.CpuState.SP = 0x0100;
        machine.CpuState.CS = 0x2000;
        machine.CpuState.IP = 0x0042;
        machine.CpuState.InterruptFlag = true;

        // Act 2 - IVT points to second handler
        machine.InterruptVectorTable[0] = new SegmentedAddress(0x4567, 0x0089);
        Action returnedAction2 = helper.DispatchCpuFault(exception, 0x2000, 0x0042, targets);

        // Assert
        returnedAction1.Should().BeSameAs(markerAction1);
        returnedAction2.Should().BeSameAs(markerAction2);
        invocationCounter1.Should().Be(1);
        invocationCounter2.Should().Be(1);
    }

    [Theory]
    [InlineData(0x0101, 2, 0x80, 1)]
    [InlineData(0x80, 0xFF, 0, 0x80)]
    [InlineData(byte.MaxValue, 1, byte.MaxValue, 0)]
    public void Div8_ReturnsExpectedQuotientAndRemainder(ushort dividend, byte divisor, byte expectedQuotient, byte expectedRemainder) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act
        (byte Quotient, byte Remainder) result = helper.Div8(dividend, divisor);

        // Assert
        AssertRegistersAndFlagsUnchanged(machine, before);
        result.Quotient.Should().Be(expectedQuotient);
        result.Remainder.Should().Be(expectedRemainder);
        result.Quotient.Should().Be(helper.Alu8.Div(dividend, divisor));
    }

    [Theory]
    [InlineData(0x00010001, 2, 0x8000, 1)]
    [InlineData(0x8000, 0xFFFF, 0, 0x8000)]
    [InlineData(ushort.MaxValue, 1, ushort.MaxValue, 0)]
    public void Div16_ReturnsExpectedQuotientAndRemainder(uint dividend, ushort divisor, ushort expectedQuotient, ushort expectedRemainder) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act
        (ushort Quotient, ushort Remainder) result = helper.Div16(dividend, divisor);

        // Assert
        AssertRegistersAndFlagsUnchanged(machine, before);
        result.Quotient.Should().Be(expectedQuotient);
        result.Remainder.Should().Be(expectedRemainder);
        result.Quotient.Should().Be(helper.Alu16.Div(dividend, divisor));
    }

    [Theory]
    [InlineData(0x0000000100000001UL, 2U, 0x80000000U, 1U)]
    [InlineData(0x80000000UL, 0xFFFFFFFFU, 0U, 0x80000000U)]
    [InlineData(uint.MaxValue, 1U, uint.MaxValue, 0U)]
    public void Div32_ReturnsExpectedQuotientAndRemainder(ulong dividend, uint divisor, uint expectedQuotient, uint expectedRemainder) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act
        (uint Quotient, uint Remainder) result = helper.Div32(dividend, divisor);

        // Assert
        AssertRegistersAndFlagsUnchanged(machine, before);
        result.Quotient.Should().Be(expectedQuotient);
        result.Remainder.Should().Be(expectedRemainder);
        result.Quotient.Should().Be(helper.Alu32.Div(dividend, divisor));
    }

    [Theory]
    [InlineData(-7, 2, 0xFD, 0xFF)]
    [InlineData(7, -2, 0xFD, 1)]
    [InlineData(-7, -2, 3, 0xFF)]
    [InlineData(sbyte.MinValue, 1, 0x80, 0)]
    [InlineData(sbyte.MaxValue, 1, 0x7F, 0)]
    public void IDiv8_ReturnsExpectedQuotientAndRemainder(short dividend, sbyte divisor, byte expectedQuotient, byte expectedRemainder) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act
        (byte Quotient, byte Remainder) result = helper.IDiv8(dividend, divisor);

        // Assert
        AssertRegistersAndFlagsUnchanged(machine, before);
        result.Quotient.Should().Be(expectedQuotient);
        result.Remainder.Should().Be(expectedRemainder);
        result.Quotient.Should().Be(unchecked((byte)helper.Alu8.Idiv(dividend, divisor)));
    }

    [Theory]
    [InlineData(-7, 2, 0xFFFD, 0xFFFF)]
    [InlineData(7, -2, 0xFFFD, 1)]
    [InlineData(-7, -2, 3, 0xFFFF)]
    [InlineData(short.MinValue, 1, 0x8000, 0)]
    [InlineData(short.MaxValue, 1, 0x7FFF, 0)]
    public void IDiv16_ReturnsExpectedQuotientAndRemainder(int dividend, short divisor, ushort expectedQuotient, ushort expectedRemainder) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act
        (ushort Quotient, ushort Remainder) result = helper.IDiv16(dividend, divisor);

        // Assert
        AssertRegistersAndFlagsUnchanged(machine, before);
        result.Quotient.Should().Be(expectedQuotient);
        result.Remainder.Should().Be(expectedRemainder);
        result.Quotient.Should().Be(unchecked((ushort)helper.Alu16.Idiv(dividend, divisor)));
    }

    [Theory]
    [InlineData(-7L, 2, 0xFFFFFFFDU, 0xFFFFFFFFU)]
    [InlineData(7L, -2, 0xFFFFFFFDU, 1U)]
    [InlineData(-7L, -2, 3U, 0xFFFFFFFFU)]
    [InlineData(int.MinValue, 1, 0x80000000U, 0U)]
    [InlineData(int.MaxValue, 1, 0x7FFFFFFFU, 0U)]
    public void IDiv32_ReturnsExpectedQuotientAndRemainder(long dividend, int divisor, uint expectedQuotient, uint expectedRemainder) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act
        (uint Quotient, uint Remainder) result = helper.IDiv32(dividend, divisor);

        // Assert
        AssertRegistersAndFlagsUnchanged(machine, before);
        result.Quotient.Should().Be(expectedQuotient);
        result.Remainder.Should().Be(expectedRemainder);
        result.Quotient.Should().Be(unchecked((uint)helper.Alu32.Idiv(dividend, divisor)));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0x0100, 1)]
    public void Div8_ThrowsCpuDivisionErrorException(ushort dividend, byte divisor) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act & Assert
        helper.Invoking(h => {
            (h.AL, h.AH) = h.Div8(dividend, divisor);
        }).Should().Throw<CpuDivisionErrorException>();
        AssertRegistersAndFlagsUnchanged(machine, before);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0x00010000, 1)]
    public void Div16_ThrowsCpuDivisionErrorException(uint dividend, ushort divisor) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act & Assert
        helper.Invoking(h => {
            (h.AX, h.DX) = h.Div16(dividend, divisor);
        }).Should().Throw<CpuDivisionErrorException>();
        AssertRegistersAndFlagsUnchanged(machine, before);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0x0000000100000000UL, 1)]
    public void Div32_ThrowsCpuDivisionErrorException(ulong dividend, uint divisor) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act & Assert
        helper.Invoking(h => {
            (h.EAX, h.EDX) = h.Div32(dividend, divisor);
        }).Should().Throw<CpuDivisionErrorException>();
        AssertRegistersAndFlagsUnchanged(machine, before);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(128, 1)]
    [InlineData(-129, 1)]
    [InlineData(short.MinValue, -1)]
    public void IDiv8_ThrowsCpuDivisionErrorException(short dividend, sbyte divisor) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act & Assert
        helper.Invoking(h => {
            (h.AL, h.AH) = h.IDiv8(dividend, divisor);
        }).Should().Throw<CpuDivisionErrorException>();
        AssertRegistersAndFlagsUnchanged(machine, before);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(32768, 1)]
    [InlineData(-32769, 1)]
    public void IDiv16_ThrowsCpuDivisionErrorException(int dividend, short divisor) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act & Assert
        helper.Invoking(h => {
            (h.AX, h.DX) = h.IDiv16(dividend, divisor);
        }).Should().Throw<CpuDivisionErrorException>();
        AssertRegistersAndFlagsUnchanged(machine, before);
    }

    [Fact]
    public void IDiv16_IntMinValueDivNeg1_ThrowsOverflowException() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act & Assert
        helper.Invoking(h => h.Alu16.Idiv(int.MinValue, -1)).Should().Throw<OverflowException>();
        helper.Invoking(h => {
            (h.AX, h.DX) = h.IDiv16(int.MinValue, -1);
        }).Should().Throw<OverflowException>();
        AssertRegistersAndFlagsUnchanged(machine, before);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2147483648L, 1)]
    [InlineData(-2147483649L, 1)]
    public void IDiv32_ThrowsCpuDivisionErrorException(long dividend, int divisor) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act & Assert
        helper.Invoking(h => {
            (h.EAX, h.EDX) = h.IDiv32(dividend, divisor);
        }).Should().Throw<CpuDivisionErrorException>();
        AssertRegistersAndFlagsUnchanged(machine, before);
    }

    [Fact]
    public void IDiv32_LongMinValueDivNeg1_ThrowsOverflowException() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SeedRegistersAndFlags(machine);
        (uint Eax, uint Edx, uint Flags) before = CaptureRegistersAndFlags(machine);

        // Act & Assert
        helper.Invoking(h => h.Alu32.Idiv(long.MinValue, -1)).Should().Throw<OverflowException>();
        helper.Invoking(h => {
            (h.EAX, h.EDX) = h.IDiv32(long.MinValue, -1);
        }).Should().Throw<OverflowException>();
        AssertRegistersAndFlagsUnchanged(machine, before);
    }

    [Fact]
    public void Div8_OverlappingRegisterSetters_PreservesAX() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);

        machine.CpuState.AX = 0x0000;

        // Act
        (machine.CpuState.AL, machine.CpuState.AH) = helper.Div8(0x0101, 2);

        // Assert
        machine.CpuState.AX.Should().Be(0x0180);
    }

    [Fact]
    public void CallbackThatKeepsCsIpRunsHandlerAndLeavesCsIpAtCallback() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        ushort number = TestCallbackNumber;
        bool ran = false;
        machine.CallbackHandler.AddCallback(new EmulatedCallback(number, () => ran = true, new SegmentedAddress(0xF000, 0x1000)));

        // Act
        helper.Callback(0xF000, 0x1000, number);

        // Assert
        ran.Should().BeTrue();
        machine.CpuState.CS.Should().Be(0xF000);
        machine.CpuState.IP.Should().Be(0x1000);
    }

    [Fact]
    public void CallbackThatMovesCsIpFailsAsUntested() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        ushort number = TestCallbackNumber;
        machine.CallbackHandler.AddCallback(new EmulatedCallback(number, () => {
            machine.CpuState.CS = 0x1234;
            machine.CpuState.IP = 0x0100;
        }, new SegmentedAddress(0xF000, 0x1000)));

        // Act & Assert
        Action act = () => helper.Callback(0xF000, 0x1000, number);
        act.Should().Throw<UnrecoverableException>()
            .WithMessage("*Untested callback CS:IP change at F000:1000*1234:0100*");
    }

    [Fact]
    public void VerifySpeculativeRunOrFailNamesTheFirstChangedInstruction() {
        // Arrange: a two-instruction run at F000:0010 (lengths 2 and 3). Memory matches the first
        // signature but differs on the second, so the failure must name F000:0012, not the run entry.
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        machine.Memory.UInt8[0xF000, 0x0010] = 0xAA;
        machine.Memory.UInt8[0xF000, 0x0011] = 0xBB;
        machine.Memory.UInt8[0xF000, 0x0012] = 0xCC;
        machine.Memory.UInt8[0xF000, 0x0013] = 0xDD;
        machine.Memory.UInt8[0xF000, 0x0014] = 0x00;

        // Act & Assert
        Action act = () => helper.VerifySpeculativeRunOrFail(0xF000, 0x0010,
            [new byte?[] { 0xAA, 0xBB }, new byte?[] { 0xCC, 0xDD, 0xEE }]);
        act.Should().Throw<UnrecoverableException>()
            .WithMessage("*Speculative code at F000:0012 no longer matches memory*");
    }

    [Fact]
    public void VerifySpeculativeRunOrFailPassesWhenAllMatch() {
        // Arrange: the same two-instruction run with every byte matching.
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        machine.Memory.UInt8[0xF000, 0x0010] = 0xAA;
        machine.Memory.UInt8[0xF000, 0x0011] = 0xBB;
        machine.Memory.UInt8[0xF000, 0x0012] = 0xCC;
        machine.Memory.UInt8[0xF000, 0x0013] = 0xDD;
        machine.Memory.UInt8[0xF000, 0x0014] = 0xEE;

        // Act & Assert
        Action act = () => helper.VerifySpeculativeRunOrFail(0xF000, 0x0010,
            [new byte?[] { 0xAA, 0xBB }, new byte?[] { 0xCC, 0xDD, 0xEE }]);
        act.Should().NotThrow();
    }

    [Fact]
    public void CallbackThatStopsTheMachineExits() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        ushort number = TestCallbackNumber;
        machine.CallbackHandler.AddCallback(new EmulatedCallback(number, () => machine.CpuState.IsRunning = false,
            new SegmentedAddress(0xF000, 0x1000)));

        // Act & Assert
        Action act = () => helper.Callback(0xF000, 0x1000, number);
        act.Should().Throw<HaltRequestedException>();
    }

    [Fact]
    public void ReturnToAnOuterExpectedAddressUnwindsToThatCall() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        CSharpOverrideHelper helper = CreateHelper(machine);

        // Act
        helper.InterruptCall(0x1000, 0x0010, 0x2000, _ => {
            helper.NearCall(0x2000, 0x0050, _ => () => {
                machine.CpuState.CS = 0x1000;
                machine.CpuState.IP = 0x0010;
            });
            return () => { };
        });

        // Assert
        machine.CpuState.CS.Should().Be(0x1000);
        machine.CpuState.IP.Should().Be(0x0010);
    }

    [Fact]
    public void ANestedCallReturningToAnOverriddenAddressResumesTheWaitingCaller() {
        // Arrange: the return address of the nested call is itself a registered override
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        Dictionary<SegmentedAddress, FunctionInformation> functionInformations = new();
        SegmentedAddress overriddenReturnAddress = new(0x2000, 0x0050);
        bool returnAddressOverrideInvoked = false;
        functionInformations[overriddenReturnAddress] =
            new FunctionInformation(overriddenReturnAddress, "return_address_partition",
                _ => () => returnAddressOverrideInvoked = true);
        CSharpOverrideHelper helper = new(functionInformations, machine, _loggerServiceMock,
            new Configuration { HttpApiPort = 0 });

        // Act
        helper.InterruptCall(0x1000, 0x0010, 0x2000, _ => {
            helper.NearCall(0x2000, 0x0050, _ => () => {
                machine.CpuState.CS = 0x2000;
                machine.CpuState.IP = 0x0050;
            });
            machine.CpuState.CS = 0x1000;
            machine.CpuState.IP = 0x0010;
            return () => { };
        });

        // Assert: the generated caller waiting at the return address resumes, the override there does not run
        returnAddressOverrideInvoked.Should().BeFalse();
        machine.CpuState.CS.Should().Be(0x1000);
        machine.CpuState.IP.Should().Be(0x0010);
    }

    [Fact]
    public void ReturnToAnAddressNoCallExpectsFailsAsAnUnwind() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        CSharpOverrideHelper helper = CreateHelper(machine);

        // Act & Assert
        Action act = () => helper.InterruptCall(0x1000, 0x0010, 0x2000, _ => {
            helper.NearCall(0x2000, 0x0050, _ => () => {
                machine.CpuState.CS = 0x1000;
                machine.CpuState.IP = 0x0099;
            });
            return () => { };
        });
        CallStackUnwindException exception = act.Should().Throw<CallStackUnwindException>().Which;
        exception.Target.Should().Be(new SegmentedAddress(0x1000, 0x0099));
        exception.Message.Should().Contain("no C# override");
    }

    [Fact]
    public void ANestedCallLandingOnACatalogueOverrideRunsIt() {
        // Arrange: the helper's own dictionary is empty, only the catalogue knows the override
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SegmentedAddress cataloguedOverride = new(0x2000, 0x0080);
        bool overrideInvoked = false;
        dependencyInjection.FunctionCatalogue.FunctionInformations[cataloguedOverride] =
            new FunctionInformation(cataloguedOverride, "catalogued_partition", _ => () => {
                overrideInvoked = true;
                machine.CpuState.CS = 0x2000;
                machine.CpuState.IP = 0x0050;
            });

        // Act
        helper.NearCall(0x2000, 0x0050, _ => () => {
            machine.CpuState.CS = 0x2000;
            machine.CpuState.IP = 0x0080;
        });

        // Assert: the catalogue override ran and the chain resumed the waiting caller after it
        overrideInvoked.Should().BeTrue();
        machine.CpuState.CS.Should().Be(0x2000);
        machine.CpuState.IP.Should().Be(0x0050);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANestedCallLandingOnAnAddressWithoutAnOverrideFailsAsAnUnwind(bool registerTargetWithoutOverride) {
        // Arrange: the callee transfers control to an address no call expects to return to
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        Dictionary<SegmentedAddress, FunctionInformation> helperFunctions = new();
        CSharpOverrideHelper helper = new(helperFunctions, machine, _loggerServiceMock,
            new Configuration { HttpApiPort = 0 });
        SegmentedAddress target = new(0x2000, 0x0099);
        if (registerTargetWithoutOverride) {
            FunctionInformation knownWithoutOverride = new(target, "known_without_override");
            helperFunctions.Add(target, knownWithoutOverride);
            dependencyInjection.FunctionCatalogue.FunctionInformations[target] = knownWithoutOverride;
        }

        // Act
        Action act = () => helper.NearCall(0x2000, 0x0050, _ => () => {
            machine.CpuState.CS = target.Segment;
            machine.CpuState.IP = target.Offset;
        });

        // Assert
        CallStackUnwindException exception = act.Should().Throw<CallStackUnwindException>().Which;
        exception.Target.Should().Be(target);
        exception.Message.Should().Contain("no C# override");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANestedCallThatStopsTheMachineExits(bool returnsToExpectedAddress) {
        // Arrange: the callee stops the machine, with CS:IP at the expected return or elsewhere
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        CSharpOverrideHelper helper = CreateHelper(machine);

        // Act
        Action act = () => helper.NearCall(0x2000, 0x0050, _ => () => {
            machine.CpuState.IsRunning = false;
            if (returnsToExpectedAddress) {
                machine.CpuState.CS = 0x2000;
                machine.CpuState.IP = 0x0050;
            } else {
                machine.CpuState.CS = 0x2000;
                machine.CpuState.IP = 0x0099;
            }
        });

        // Assert: a stopped machine must not let the waiting C# caller continue
        act.Should().Throw<HaltRequestedException>();
    }

    [Fact]
    public void CallbackMovedCsIpReportsTheMove() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        ushort movingNumber = TestCallbackNumber;
        machine.CallbackHandler.AddCallback(new EmulatedCallback(movingNumber, () => {
            machine.CpuState.CS = 0x1234;
            machine.CpuState.IP = 0x0100;
        }, new SegmentedAddress(0xF000, 0x1000)));
        ushort stayingNumber = SecondTestCallbackNumber;
        machine.CallbackHandler.AddCallback(new EmulatedCallback(stayingNumber, () => { },
            new SegmentedAddress(0xF000, 0x1000)));

        // Act
        bool moved = helper.CallbackMovedCsIp(0xF000, 0x1000, movingNumber);
        bool stayed = helper.CallbackMovedCsIp(0xF000, 0x1000, stayingNumber);

        // Assert
        moved.Should().BeTrue("the handler set CS:IP to another address");
        stayed.Should().BeFalse("the handler left CS:IP at the callback");
    }

    [Fact]
    public void CheckExternalEventsProcessesPendingInput() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        bool processed = false;
        machine.InputEventHub.PostToEmulatorThread(() => processed = true);

        // Act
        helper.CheckExternalEvents(machine.CpuState.CS, machine.CpuState.IP);

        // Assert
        processed.Should().BeTrue();
    }

    [Fact]
    public void DefineFunctionRegistersAnOverrideThatAllowsInterpreterFallback() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SegmentedAddress address = new(0x1000, 0x0010);

        // Act
        helper.DefineFunction(address.Segment, address.Offset, _ => () => { }, name: "hand_written");

        // Assert: a hand-written override is permissive, the interpreter continues at its CS:IP
        helper.FunctionInformations[address].ForbidsInterpreterFallback.Should().BeFalse();
    }

    [Fact]
    public void DefineFunctionWithoutInterpreterFallbackRegistersAnOverrideThatForbidsInterpreterFallback() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SegmentedAddress address = new(0x1000, 0x0010);

        // Act
        helper.DefineFunctionWithoutInterpreterFallback(address.Segment, address.Offset, _ => () => { }, "generated");

        // Assert: generated code owns the complete control flow of its partitions
        helper.FunctionInformations[address].ForbidsInterpreterFallback.Should().BeTrue();
    }

    [Fact]
    public void DefineFunctionWithoutInterpreterFallbackFailsOnAnExistingRegistration() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        CSharpOverrideHelper helper = CreateHelper(machine);
        SegmentedAddress address = new(0x1000, 0x0010);
        helper.DefineFunctionWithoutInterpreterFallback(address.Segment, address.Offset, _ => () => { }, "generated");

        // Act
        Action act = () => helper.DefineFunctionWithoutInterpreterFallback(address.Segment, address.Offset, _ => () => { }, "generated_again");

        // Assert
        act.Should().Throw<UnrecoverableException>();
    }

    [Fact]
    public void InterruptRet32PopsEipCsAndEFlags() {
        // Arrange: a 32-bit interrupt frame is eip (dword), padded cs (dword), eflags (dword)
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        CSharpOverrideHelper helper = CreateHelper(machine);
        machine.Stack.Push32(0x7202);
        machine.Stack.Push32(0x500);
        machine.Stack.Push32(0x1234);

        // Act
        helper.InterruptRet32()();

        // Assert
        machine.CpuState.IpSegmentedAddress.Should().Be(new SegmentedAddress(0x500, 0x1234));
        machine.CpuState.Flags.FlagRegister.Should().Be(0x7202);
        machine.CpuState.SP.Should().Be(0x100);
    }

    [Fact]
    public void InterruptCallReturnedElsewhere_RedirectedReturnSkipsLandingOverrideAndReturnsTrue() {
        // Arrange: an interrupt handler that bumps the stacked return IP by one, like the 8087
        // emulator, and an override registered at the landing address that must not run.
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        SegmentedAddress handlerAddress = new(0x3456, 0x0078);
        SegmentedAddress redirectedReturn = new(0x2000, 0x0101);
        CSharpOverrideHelper? helper = null;
        Dictionary<SegmentedAddress, FunctionInformation> overrides = new() {
            [handlerAddress] = new FunctionInformation(handlerAddress, "handler", _ => {
                helper!.Stack.Poke16(0, (ushort)(helper.Stack.Peek16(0) + 1));
                return helper.InterruptRet();
            })
        };
        helper = CreateHelper(machine, overrides);
        machine.InterruptVectorTable[0x80] = handlerAddress;
        int landingRan = 0;
        res.FunctionCatalogue.FunctionInformations[redirectedReturn] = new FunctionInformation(redirectedReturn, "landing",
            _ => { landingRan++; return helper.NearRet(); }) { ForbidsInterpreterFallback = true };

        // Act
        bool result = helper.InterruptCallReturnedElsewhere(0x2000, 0x0100, 0x80, [redirectedReturn]);

        // Assert
        result.Should().BeTrue();
        landingRan.Should().Be(0, "a redirected return lands without running the override registered there");
        helper.CS.Should().Be(redirectedReturn.Segment);
        helper.IP.Should().Be(redirectedReturn.Offset);
        helper.SP.Should().Be(0x0100, "the interrupt frame is fully consumed");
    }

    [Fact]
    public void InterruptCallReturnedElsewhere_AlignedReturnReturnsFalse() {
        // Arrange: a handler that irets without touching the stacked return address.
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        SegmentedAddress handlerAddress = new(0x3456, 0x0078);
        CSharpOverrideHelper? helper = null;
        Dictionary<SegmentedAddress, FunctionInformation> overrides = new() {
            [handlerAddress] = new FunctionInformation(handlerAddress, "handler", _ => helper!.InterruptRet())
        };
        helper = CreateHelper(machine, overrides);
        machine.InterruptVectorTable[0x80] = handlerAddress;

        // Act
        bool result = helper.InterruptCallReturnedElsewhere(0x2000, 0x0100, 0x80, [new SegmentedAddress(0x2000, 0x0101)]);

        // Assert
        result.Should().BeFalse();
        helper.CS.Should().Be(0x2000);
        helper.IP.Should().Be(0x0100);
    }

    [Fact]
    public void InterruptCallReturnedElsewhere_LandingBelowCallStackLevelRunsTheOverrideThere() {
        // Arrange: a handler that leaves its interrupt frame on the stack and jumps to the listed
        // address; the stack is below the pre-call level, so the listed address is not accepted and
        // the override registered there runs as today's chain does.
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        SegmentedAddress handlerAddress = new(0x3456, 0x0078);
        SegmentedAddress landing = new(0x2000, 0x0101);
        SegmentedAddress expectedReturn = new(0x2000, 0x0100);
        CSharpOverrideHelper? helper = null;
        Dictionary<SegmentedAddress, FunctionInformation> overrides = new() {
            [handlerAddress] = new FunctionInformation(handlerAddress, "handler",
                _ => () => {
                    helper!.State.CS = landing.Segment;
                    helper.State.IP = landing.Offset;
                })
        };
        helper = CreateHelper(machine, overrides);
        machine.InterruptVectorTable[0x80] = handlerAddress;
        int landingRan = 0;
        res.FunctionCatalogue.FunctionInformations[landing] = new FunctionInformation(landing, "landing",
            _ => { landingRan++; return helper.FarJump(expectedReturn.Segment, expectedReturn.Offset); }) { ForbidsInterpreterFallback = true };

        // Act
        bool result = helper.InterruptCallReturnedElsewhere(0x2000, 0x0100, 0x80, [landing]);

        // Assert
        result.Should().BeFalse("the call ended at the expected return after the nested override ran");
        landingRan.Should().Be(1, "the landing address is not accepted while the stack stays below the call level");
    }

    [Fact]
    public void NearCallReturnedElsewhere_Ret2AboveCallStackLevelReturnsTrue() {
        // Arrange: a callee that rewrites its stacked return IP and returns with ret 2, so the stack
        // ends above the call level at the listed landing address.
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        machine.CpuState.CS = 0x2000;
        SegmentedAddress redirectedReturn = new(0x2000, 0x0101);
        CSharpOverrideHelper helper = CreateHelper(machine);

        // Act
        bool result = helper.NearCallReturnedElsewhere(0x2000, 0x0100, _ => {
            helper.Stack.Poke16(0, redirectedReturn.Offset);
            return helper.NearRet(2);
        }, [redirectedReturn]);

        // Assert
        result.Should().BeTrue();
        helper.IP.Should().Be(redirectedReturn.Offset);
        helper.SP.Should().Be(0x0102, "ret 2 leaves the stack above the call level");
    }

    [Fact]
    public void InterruptCallToProgramHandler_PushesFlagsCsIpAndRunsTheHandler() {
        // Arrange: the vector points at the program handler the generated code calls directly.
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        machine.CpuState.InterruptFlag = true;
        ushort originalFlags = machine.CpuState.Flags.FlagRegister16;
        SegmentedAddress handlerAddress = new(0x3456, 0x0078);
        machine.InterruptVectorTable[0x62] = handlerAddress;
        CSharpOverrideHelper helper = CreateHelper(machine);
        int handlerRan = 0;

        // Act
        helper.InterruptCall(0x2000, 0x0100, 0x62, handlerAddress.Segment, handlerAddress.Offset, _ => {
            handlerRan++;
            helper.SP.Should().Be(0x00FA, "the interrupt frame pushed FLAGS, CS and IP");
            helper.Stack.Peek16(0).Should().Be(0x0100, "the stacked return IP is the expected return");
            helper.Stack.Peek16(2).Should().Be(0x2000, "the stacked return CS is the expected return");
            helper.Stack.Peek16(4).Should().Be(originalFlags);
            machine.CpuState.InterruptFlag.Should().BeFalse("an int clears IF for the handler");
            return helper.InterruptRet();
        });

        // Assert
        handlerRan.Should().Be(1);
        helper.CS.Should().Be(0x2000);
        helper.IP.Should().Be(0x0100);
        helper.SP.Should().Be(0x0100, "the interrupt frame is fully consumed");
    }

    [Fact]
    public void InterruptCallToProgramHandler_VectorChanged_FailsAsUntested() {
        // Arrange: the vector no longer points at the handler the generated code calls.
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        machine.InterruptVectorTable[0x62] = new SegmentedAddress(0x9999, 0x0001);
        CSharpOverrideHelper helper = CreateHelper(machine);
        int handlerRan = 0;

        // Act
        Action act = () => helper.InterruptCall(0x2000, 0x0100, 0x62, 0x3456, 0x0078,
            _ => { handlerRan++; return helper.InterruptRet(); });

        // Assert
        act.Should().Throw<UnrecoverableException>().Which.Message.Should().Contain("0x62");
        handlerRan.Should().Be(0);
        helper.SP.Should().Be(0x0100, "nothing was pushed");
    }

    [Fact]
    public void TypedNearCall_MatchesUntypedCallAndRet() {
        // Arrange: two machines with the same state; one runs the untyped call + ret pair,
        // the other the typed call + return pair.
        using Spice86Creator creator1 = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res1 = creator1.Create();
        Machine machine1 = res1.Machine;
        machine1.CpuState.SS = 0x3000;
        machine1.CpuState.SP = 0x0100;
        machine1.CpuState.AX = 0x0005;
        machine1.CpuState.BX = 0x0007;
        CSharpOverrideHelper helper1 = CreateHelper(machine1);

        using Spice86Creator creator2 = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res2 = creator2.Create();
        Machine machine2 = res2.Machine;
        machine2.CpuState.SS = 0x3000;
        machine2.CpuState.SP = 0x0100;
        machine2.CpuState.AX = 0x0005;
        machine2.CpuState.BX = 0x0007;
        CSharpOverrideHelper helper2 = CreateHelper(machine2);

        // Act
        helper1.NearCall(0x2000, 0x0100, _ => {
            helper1.AX = (ushort)(helper1.AX + helper1.BX);
            return helper1.NearRet(4);
        });
        ushort returned = helper2.NearCall(0x2000, 0x0100, () => {
            helper2.AX = (ushort)(helper2.AX + helper2.BX);
            helper2.NearReturn(4);
            return helper2.AX;
        });

        // Assert
        returned.Should().Be(helper2.AX, "the typed call returns the callee's value");
        helper2.SP.Should().Be(helper1.SP, "ret 4 / NearReturn(4) leaves the same SP");
        helper2.CS.Should().Be(helper1.CS);
        helper2.IP.Should().Be(helper1.IP);
        helper2.AX.Should().Be(helper1.AX);
        for (int offset = -2; offset >= -8; offset -= 2) {
            helper2.Stack.Peek16(offset).Should().Be(helper1.Stack.Peek16(offset),
                $"the pushed return frame below SP matches at {offset}");
        }
    }

    [Fact]
    public void TypedFarCall_MatchesUntypedCallAndRet() {
        // Arrange
        using Spice86Creator creator1 = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res1 = creator1.Create();
        Machine machine1 = res1.Machine;
        machine1.CpuState.SS = 0x3000;
        machine1.CpuState.SP = 0x0100;
        machine1.CpuState.AX = 0x0005;
        machine1.CpuState.BX = 0x0007;
        CSharpOverrideHelper helper1 = CreateHelper(machine1);

        using Spice86Creator creator2 = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res2 = creator2.Create();
        Machine machine2 = res2.Machine;
        machine2.CpuState.SS = 0x3000;
        machine2.CpuState.SP = 0x0100;
        machine2.CpuState.AX = 0x0005;
        machine2.CpuState.BX = 0x0007;
        CSharpOverrideHelper helper2 = CreateHelper(machine2);

        // Act
        helper1.FarCall(0x2000, 0x0100, 0x3456, _ => {
            helper1.AX = (ushort)(helper1.AX + helper1.BX);
            return helper1.FarRet(2);
        });
        ushort returned = helper2.FarCall(0x2000, 0x0100, 0x3456, () => {
            helper2.AX = (ushort)(helper2.AX + helper2.BX);
            helper2.FarReturn(2);
            return helper2.AX;
        });

        // Assert
        returned.Should().Be(helper2.AX, "the typed call returns the callee's value");
        helper2.SP.Should().Be(helper1.SP, "retf 2 / FarReturn(2) leaves the same SP");
        helper2.CS.Should().Be(helper1.CS);
        helper2.IP.Should().Be(helper1.IP);
        helper2.AX.Should().Be(helper1.AX);
        for (int offset = -2; offset >= -8; offset -= 2) {
            helper2.Stack.Peek16(offset).Should().Be(helper1.Stack.Peek16(offset),
                $"the pushed return frame below SP matches at {offset}");
        }
    }

    [Fact]
    public void TypedCall_ReturningElsewhere_FailsAsUntested() {
        // Arrange: a typed callee that rewrites its stacked return address before NearReturn(0).
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x0100;
        CSharpOverrideHelper helper = CreateHelper(machine);

        // Act
        Action act = () => helper.NearCall(0x2000, 0x0100, () => {
            helper.Stack.Poke16(0, 0x0101);
            helper.NearReturn(0);
        });

        // Assert
        act.Should().Throw<UnrecoverableException>().Which.Message.Should().Contain("2000:0101");
    }

    [Fact]
    public void EntryWrapper_UntypedCallOfTypedMethod_MatchesClassicReturn() {
        // Arrange: an untyped NearCall of the entry wrapper a typed method gets
        // ({ AX = Typed(AX, BX); return Returned; }) must leave the same state as a classic
        // NearRet callee, because the typed method already performed the return.
        using Spice86Creator creator1 = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res1 = creator1.Create();
        Machine machine1 = res1.Machine;
        machine1.CpuState.SS = 0x3000;
        machine1.CpuState.SP = 0x0100;
        machine1.CpuState.AX = 0x0005;
        machine1.CpuState.BX = 0x0007;
        CSharpOverrideHelper helper1 = CreateHelper(machine1);

        using Spice86Creator creator2 = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res2 = creator2.Create();
        Machine machine2 = res2.Machine;
        machine2.CpuState.SS = 0x3000;
        machine2.CpuState.SP = 0x0100;
        machine2.CpuState.AX = 0x0005;
        machine2.CpuState.BX = 0x0007;
        CSharpOverrideHelper helper2 = CreateHelper(machine2);

        // Act
        helper1.NearCall(0x2000, 0x0100, _ => {
            helper1.AX = (ushort)(helper1.AX + helper1.BX);
            return helper1.NearRet();
        });
        helper2.NearCall(0x2000, 0x0100, _ => {
            helper2.AX = Typed(helper2.AX, helper2.BX, helper2);
            return CSharpOverrideHelper.Returned;
        });

        // Assert
        helper2.SP.Should().Be(helper1.SP);
        helper2.CS.Should().Be(helper1.CS);
        helper2.IP.Should().Be(helper1.IP);
        helper2.AX.Should().Be(helper1.AX);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedCallLeavesTheSameCsIpConventionAsTheUntypedHelper(bool isFar) {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        machine.CpuState.SS = 0x3000;
        machine.CpuState.SP = 0x100;
        CSharpOverrideHelper helper = CreateHelper(machine);
        ushort seenCs = 0;
        ushort seenIp = 0;

        // Act: the typed callee records the CS:IP it was entered with, then returns through the
        // helper - which pops the pushed frame and lands CS:IP on the expected return address.
        Func<int> typedCall = () => {
            seenCs = machine.CpuState.CS;
            seenIp = machine.CpuState.IP;
            if (isFar) {
                helper.FarReturn(0);
            } else {
                helper.NearReturn(0);
            }
            return 7;
        };
        int result = isFar ? helper.FarCall(0x1000, 0x0010, 0x3456, typedCall) : helper.NearCall(0x1000, 0x0010, typedCall);

        // Assert: the callee runs with CS set to its segment and IP holding the expected return
        // offset - the same convention the untyped call helpers use.
        result.Should().Be(7);
        seenCs.Should().Be(isFar ? (ushort)0x3456 : (ushort)0x1000);
        seenIp.Should().Be(0x0010);
    }

    private static ushort Typed(ushort ax, ushort bx, CSharpOverrideHelper helper) {
        ushort result = (ushort)(ax + bx);
        helper.NearReturn(0);
        return result;
    }

    [Fact]
    public void SingleEntryMutualCycle_ThroughEntry_UnwindsWithoutThrowing() {
        // Arrange
        using Spice86Creator creator = new Spice86Creator(binName: "jump2");
        using Spice86DependencyInjection res = creator.Create();
        res.Machine.CpuState.SS = 0x3000;
        res.Machine.CpuState.SP = 0x100;
        Machine machine = res.Machine;
        SingleEntryCycle cycle = new(new Dictionary<SegmentedAddress, FunctionInformation>(),
            machine, _loggerServiceMock, new Configuration { HttpApiPort = 0 });

        // Act
        cycle.JumpTarget1();

        // Assert
        Assert.Equal(SingleEntryCycle.MaxNumberOfJumps, cycle.NumberOfCallsTo1);
        Assert.Equal(SingleEntryCycle.MaxNumberOfJumps, cycle.NumberOfCallsTo2);
    }

    [Fact]
    public void SsIndexers_ReadAndWriteWhatTheCpuStackPathAccesses() {
        // Arrange: a 286 model gives the real-mode 386 MMU that checks segment limits
        using Spice86Creator creator = new(new AsmFixtures.AsmRunSettings {
            BinName = "jump2", CpuModel = Spice86.Core.Emulator.CPU.CpuModel.INTEL_80286 },
            JitMode.InterpretedOnly, null);
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x2000;
        CSharpOverrideHelper helper = CreateHelper(machine);
        machine.Memory.UInt16[0x2000, (ushort)0x1000, SegmentAccessKind.Stack] = 0x4321;

        // Act + Assert: reads observe the stack-kind writes, writes are seen by the stack path
        helper.SsUInt16[0x1000].Should().Be(0x4321);
        helper.SsUInt16[0x1000] = 0xBEEF;
        machine.Memory.UInt16[0x2000, (ushort)0x1000, SegmentAccessKind.Stack].Should().Be(0xBEEF);
    }

    [Fact]
    public void SsIndexers_WordAtSegmentLimitRaisesStackFault() {
        // Arrange
        using Spice86Creator creator = new(new AsmFixtures.AsmRunSettings {
            BinName = "jump2", CpuModel = Spice86.Core.Emulator.CPU.CpuModel.INTEL_80286 },
            JitMode.InterpretedOnly, null);
        using Spice86DependencyInjection res = creator.Create();
        Machine machine = res.Machine;
        machine.CpuState.SS = 0x2000;
        CSharpOverrideHelper helper = CreateHelper(machine);

        // Act
        Action ssRead = () => _ = helper.SsUInt16[0xFFFF];
        Action dsRead = () => _ = helper.UInt16[0x2000, 0xFFFF];

        // Assert: the SS-relative access faults #SS where the same offset as data faults #GP
        ssRead.Should().Throw<CpuStackSegmentFaultException>();
        dsRead.Should().Throw<CpuGeneralProtectionFaultException>();
    }

    private CSharpOverrideHelper CreateHelper(Machine machine) =>
        CreateHelper(machine, new Dictionary<SegmentedAddress, FunctionInformation>());

    private CSharpOverrideHelper CreateHelper(Machine machine, IDictionary<SegmentedAddress, FunctionInformation> functionInformations) {
        return new CSharpOverrideHelper(
            functionInformations,
            machine,
            _loggerServiceMock,
            new Configuration { HttpApiPort = 0 });
    }

    private static void SeedRegistersAndFlags(Machine machine) {
        machine.CpuState.EAX = 0x12345678;
        machine.CpuState.EDX = 0x9ABCDEF0;
        machine.CpuState.Flags.FlagRegister = 0xDEADBEEF;
    }

    private static (uint Eax, uint Edx, uint Flags) CaptureRegistersAndFlags(Machine machine) {
        return (machine.CpuState.EAX, machine.CpuState.EDX, machine.CpuState.Flags.FlagRegister);
    }

    private static void AssertRegistersAndFlagsUnchanged(Machine machine, (uint Eax, uint Edx, uint Flags) before) {
        machine.CpuState.EAX.Should().Be(before.Eax, "the division helper must not modify EAX");
        machine.CpuState.EDX.Should().Be(before.Edx, "the division helper must not modify EDX");
        machine.CpuState.Flags.FlagRegister.Should().Be(before.Flags, "the division helper must not modify flags");
    }
}

class RecursiveJumps : CSharpOverrideHelper {
    public const int MaxNumberOfJumps = 1;
    public int NumberOfCallsTo1 { get; set; }
    public int NumberOfCallsTo2 { get; set; }

    public RecursiveJumps(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILogger loggerService, Configuration configuration) : base(functionInformations, machine, loggerService, new()) {
    }

    public Action JumpTarget1(int loadOffset) {
    entrydispatcher:
        NumberOfCallsTo1++;
        if (JumpDispatcher.Jump(JumpTarget2, 0)) {
            loadOffset = JumpDispatcher.NextEntryAddress;
            goto entrydispatcher;
        }

        return JumpDispatcher.RequiredJumpAsmReturn;
    }

    public Action JumpTarget2(int loadOffset) {
    entrydispatcher:
        NumberOfCallsTo2++;
        if (NumberOfCallsTo2 == MaxNumberOfJumps) {
            return NearRet();
        }

        if (JumpDispatcher.Jump(JumpTarget1, 0)) {
            loadOffset = JumpDispatcher.NextEntryAddress;
            goto entrydispatcher;
        }

        return JumpDispatcher.RequiredJumpAsmReturn;
    }
}

class DeepRecursiveJumps : CSharpOverrideHelper {
    public const int MaxNumberOfJumps = 2;
    public int NumberOfCallsTo1 { get; set; }
    public int NumberOfCallsTo2 { get; set; }

    public DeepRecursiveJumps(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILogger loggerService, Configuration configuration) : base(functionInformations, machine, loggerService, new()) {
    }

    public Action JumpTarget1(int loadOffset) {
    entrydispatcher:
        NumberOfCallsTo1++;
        if (JumpDispatcher.Jump(JumpTarget2, 0)) {
            loadOffset = JumpDispatcher.NextEntryAddress;
            goto entrydispatcher;
        }

        return JumpDispatcher.RequiredJumpAsmReturn;
    }

    public Action JumpTarget2(int loadOffset) {
    entrydispatcher:
        NumberOfCallsTo2++;
        if (NumberOfCallsTo2 == MaxNumberOfJumps) {
            return NearRet();
        }

        if (JumpDispatcher.Jump(JumpTarget1, 0)) {
            loadOffset = JumpDispatcher.NextEntryAddress;
            goto entrydispatcher;
        }

        return JumpDispatcher.RequiredJumpAsmReturn;
    }
}

class SingleEntryCycle : CSharpOverrideHelper {
    public const int MaxNumberOfJumps = 2;
    private readonly Func<int, Action> _jumpTarget1Entry;
    private readonly Func<int, Action> _jumpTarget2Entry;
    public int NumberOfCallsTo1 { get; set; }
    public int NumberOfCallsTo2 { get; set; }

    public SingleEntryCycle(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILogger loggerService, Configuration configuration) : base(functionInformations, machine, loggerService, new()) {
        _jumpTarget1Entry = _ => JumpTarget1();
        _jumpTarget2Entry = _ => JumpTarget2();
    }

    public Action JumpTarget1() {
    entrydispatcher:
        NumberOfCallsTo1++;
        if (JumpDispatcher.Jump(_jumpTarget2Entry, 0)) {
            goto entrydispatcher;
        }

        return JumpDispatcher.RequiredJumpAsmReturn;
    }

    public Action JumpTarget2() {
    entrydispatcher:
        NumberOfCallsTo2++;
        if (NumberOfCallsTo2 == MaxNumberOfJumps) {
            return NearRet();
        }

        if (JumpDispatcher.Jump(_jumpTarget1Entry, 0)) {
            goto entrydispatcher;
        }

        return JumpDispatcher.RequiredJumpAsmReturn;
    }
}

class SimpleCallsJumpsOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILogger loggerService,
        Configuration configuration,
        ushort programStartSegment,
        Machine machine) {
        return SimpleCallsJumps.Create(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration).FunctionInformations;
    }
}

class SimpleCallsJumps : CSharpOverrideHelper {
    public static SimpleCallsJumps? CurrentInstance;
    public int EntryCalled { get; set; }
    public int NearCalled { get; set; }
    public int FarCalled { get; set; }
    public int FarCalled1FromStack { get; set; }
    public int FarCalled2FromStack { get; set; }

    public SimpleCallsJumps(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILogger loggerService, Configuration configuration) : base(functionInformations, machine, loggerService, configuration) {
        // this is the real entry point but other functions are fictional.
        // This is a synthetic test for control flow with stack modification where the whole program is overridden
        DefineFunction(0xF000, 0xFFF0, Entry_F000_FFF0_FFFF0);
        DefineFunction(0, 0x200, Far_callee1_from_stack_0000_0200_00200);
        DefineFunction(0, 0x300, Far_callee2_from_stack_0000_0300_00300);
    }

    public static SimpleCallsJumps Create(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILogger loggerService, Configuration configuration) {
        SimpleCallsJumps created = new(functionInformations, machine, loggerService, configuration);
        CurrentInstance = created;
        return created;
    }

    // bios entry point
    public Action Entry_F000_FFF0_FFFF0(int loadOffset) {
        EntryCalled++;
        NearCall(0xF000, 0xFFF0, Near_F000_0100_F0100);
        FarCall(0xF000, 0xFFF0, 0x2000, Far_2000_0100_20100);
        FarCall(0xF000, 0xFFF0, 0x0000, Far_calls_another_far_via_stack_0000_0100_00100);
        // We completely override the assembly code
        return Hlt();
    }

    public Action Near_F000_0100_F0100(int loadOffset) {
        NearCalled++;
        return NearRet();
    }

    public Action Far_2000_0100_20100(int loadOffset) {
        FarCalled++;
        return FarRet();
    }

    public Action Far_calls_another_far_via_stack_0000_0100_00100(int loadOffset) {
        // Replace value on stack to call far_callee1_from_stack_3000_0200_10000 when returning, evil!!
        Stack.Pop16();
        Stack.Pop16();
        Stack.Push16(0);
        Stack.Push16(0x200);
        return FarRet();
    }

    public Action Far_callee1_from_stack_0000_0200_00200(int loadOffset) {
        FarCalled1FromStack++;
        // Call of far_callee2_from_stack_3000_0300_10000 when returning. No need to replace value on stack as we were not called conventionally
        Stack.Push16(0);
        Stack.Push16(0x300);
        return FarRet();
    }

    public Action Far_callee2_from_stack_0000_0300_00300(int loadOffset) {
        FarCalled2FromStack++;
        // Push back the values of the expected return address
        Stack.Push16(0xF000);
        Stack.Push16(0xFFF0);
        return FarRet();
    }
}

class VariousOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILogger loggerService,
        Configuration configuration,
        ushort programStartSegment,
        Machine machine) {
        return VariousOverrides.Create(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration).FunctionInformations;
    }
}

class VariousOverrides : CSharpOverrideHelper {
    public static VariousOverrides? CurrentInstance;
    public int FirstFunctionCalled { get; set; }
    public int SecondFunctionCalled { get; set; }
    public int ThirdFunctionCalled { get; set; }
    public int FirstInstructionOverridenCalled { get; set; }
    public int FirstDoOnTopOfInstructionCalled { get; set; }

    public VariousOverrides(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILogger loggerService, Configuration configuration) : base(functionInformations, machine, loggerService, configuration) {
        // those are actual functions called by actual assembly code
        DefineFunction(0xF000, 0x0000, FirstFunction_F000_0000_F0000);
        DefineFunction(0xF000, 0x1290, SecondFunction_F000_1290_F1290);
        DefineFunction(0xE342, 0xEBE0, ThirdFunction_E342_EBE0_F2000);
        OverrideInstruction(0xF000, 0xFFF0, FirstInstructionOverriden);
        DoOnTopOfInstruction(0xF000, 0xFFF3, FirstDoOnTopOfInstruction);
    }

    public static VariousOverrides Create(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILogger loggerService, Configuration configuration) {
        VariousOverrides created = new(functionInformations, machine, loggerService, configuration);
        CurrentInstance = created;
        return created;
    }

    public Action FirstInstructionOverriden() {
        // MOV SP,0x1000
        SP = 0x1000;
        FirstInstructionOverridenCalled++;
        // instruction is 3 bytes, we need to return to next instruction
        return FarJump(0xF000, 0xFFF3);
    }

    public void FirstDoOnTopOfInstruction() {
        FirstDoOnTopOfInstructionCalled++;
    }

    public Action FirstFunction_F000_0000_F0000(int loadOffset) {
        FirstFunctionCalled++;
        // Execute code as if there were no override
        return FarJump(0xF000, 0x0000);
    }

    public Action SecondFunction_F000_1290_F1290(int loadOffset) {
        SecondFunctionCalled++;
        // Execute code as if there were no override
        return FarJump(0xF000, 0x1290);
    }

    public Action ThirdFunction_E342_EBE0_F2000(int loadOffset) {
        ThirdFunctionCalled++;
        // Execute code as if there were no override
        return FarJump(0xE342, 0xEBE0);
    }
}