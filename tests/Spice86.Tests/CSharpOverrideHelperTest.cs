using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Emulator.Errors;

namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

using System;
using System.Collections.Generic;

using Xunit;

public class CSharpOverrideHelperTest {
    private readonly ILogger _loggerServiceMock = Substitute.For<ILogger>();

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
            new(new SegmentedAddress(0x3456, 0x0078), () => { invocationCounter++; return markerAction; }),
            new(new SegmentedAddress(0x3456, 0x0078), () => { invocationCounter++; return helper.NearRet(); }) // duplicate, should never run
        ];

        CpuDivisionErrorException exception = new("Division by zero");

        // Act
        Action returnedAction = helper.DispatchCpuFault(exception, 0x2000, 0x0042, targets);

        // Assert
        returnedAction.Should().BeSameAs(markerAction);
        invocationCounter.Should().Be(1, "only the first matching delegate should run");
        executionCounter.Should().Be(0, "the returned action should not be executed");

        // Verify fault entry was performed
        helper.CS.Should().Be(0x3456);
        helper.IP.Should().Be(0x0078);
        helper.InterruptFlag.Should().BeFalse();
        helper.SP.Should().Be(0x00FA);

        // Stack frame: [0]=faulting IP, [2]=faulting CS, [4]=original flags
        helper.Stack.Peek16(0).Should().Be(0x0042);
        helper.Stack.Peek16(2).Should().Be(0x2000);
        helper.Stack.Peek16(4).Should().Be(originalFlags);
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

        List<CpuFaultTarget> targets = useNonEmptyList
            ? [new(new SegmentedAddress(0x3457, 0x0068), () => helper.NearRet())] // segmented alias, doesn't match
            : [];

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

        // Stack frame words should be unchanged
        helper.Stack.Peek16(0).Should().Be(0);
        helper.Stack.Peek16(2).Should().Be(0);
        helper.Stack.Peek16(4).Should().Be(0);
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
}

class RecursiveJumps : CSharpOverrideHelper {
    public static int MaxNumberOfJumps = 1;
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
    public static int MaxNumberOfJumps = 2;
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

class SimpleCallsJumpsOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILogger loggerService,
        Configuration configuration,
        ushort programStartSegment,
        Machine machine) {
        return new SimpleCallsJumps(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration).FunctionInformations;
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
        CurrentInstance = this;
        // this is the real entry point but other functions are fictional.
        // This is a synthetic test for control flow with stack modification where the whole program is overridden
        DefineFunction(0xF000, 0xFFF0, Entry_F000_FFF0_FFFF0);
        DefineFunction(0, 0x200, Far_callee1_from_stack_0000_0200_00200);
        DefineFunction(0, 0x300, Far_callee2_from_stack_0000_0300_00300);
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
        return new VariousOverrides(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration).FunctionInformations;
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
        CurrentInstance = this;
        // those are actual functions called by actual assembly code
        DefineFunction(0xF000, 0x0000, FirstFunction_F000_0000_F0000);
        DefineFunction(0xF000, 0x1290, SecondFunction_F000_1290_F1290);
        DefineFunction(0xE342, 0xEBE0, ThirdFunction_E342_EBE0_F2000);
        OverrideInstruction(0xF000, 0xFFF0, FirstInstructionOverriden);
        DoOnTopOfInstruction(0xF000, 0xFFF3, FirstDoOnTopOfInstruction);
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
        return FarJump(0xF000, 0000);
    }

    public Action SecondFunction_F000_1290_F1290(int loadOffset) {
        SecondFunctionCalled++;
        // Execute code as if there were no override
        return FarJump(0xF000, 1290);
    }

    public Action ThirdFunction_E342_EBE0_F2000(int loadOffset) {
        ThirdFunctionCalled++;
        // Execute code as if there were no override
        return FarJump(0xE342, 0xEBE0);
    }
}