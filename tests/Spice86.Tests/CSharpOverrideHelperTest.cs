using Spice86.Core.CLI;

namespace Spice86.Tests;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

using Xunit;
using NSubstitute;

using Spice86.Shared.Emulator.Memory;

public class CSharpOverrideHelperTest {
    private readonly ILoggerService _loggerServiceMock = Substitute.For<ILoggerService>();

    private ProgramExecutor CreateDummyProgramExecutor() {
        ProgramExecutor res =  new MachineCreator().CreateProgramExecutorFromBinName("add", false, false);
        Machine machine = res.Machine;
        // Setup stack
        machine.Cpu.State.SS = 0;
        machine.Cpu.State.SP = 100;
        return res;
    }

    [Fact]
    void TestJumpReturns() {
        using ProgramExecutor programExecutor = CreateDummyProgramExecutor();
        RecursiveJumps recursiveJumps =
            new RecursiveJumps(new Dictionary<SegmentedAddress, FunctionInformation>(), programExecutor.Machine,
                _loggerServiceMock);
        recursiveJumps.JumpTarget1(0);
        Assert.Equal(RecursiveJumps.MaxNumberOfJumps, recursiveJumps.NumberOfCallsTo1);
        Assert.Equal(RecursiveJumps.MaxNumberOfJumps, recursiveJumps.NumberOfCallsTo2);
    }

    [Fact]
    void TestSimpleCallsJumps() {
        using ProgramExecutor programExecutor = CreateDummyProgramExecutor();

        SimpleCallsJumps callsJumps = new SimpleCallsJumps(new Dictionary<SegmentedAddress, FunctionInformation>(),
            programExecutor.Machine, _loggerServiceMock);
        callsJumps.Entry_1000_0000_10000();
        Assert.Equal(1, callsJumps.NearCalled);
        Assert.Equal(1, callsJumps.FarCalled);
        Assert.Equal(1, callsJumps.FarCalled1FromStack);
        Assert.Equal(1, callsJumps.FarCalled2FromStack);
    }
}

class RecursiveJumps : CSharpOverrideHelper {
    public static int MaxNumberOfJumps = 1;
    public int NumberOfCallsTo1 { get; set; }
    public int NumberOfCallsTo2 { get; set; }

    public RecursiveJumps(Dictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILoggerService loggerService) : base(
        functionInformations, machine, loggerService, new Configuration()) {
    }

    public Action JumpTarget1(int loadOffset) {
        entrydispatcher:
        NumberOfCallsTo1++;
        if (JumpDispatcher.Jump(JumpTarget2, 0)) {
            loadOffset = JumpDispatcher.NextEntryAddress;
            goto entrydispatcher;
        }

        return JumpDispatcher.JumpAsmReturn!;
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

        return JumpDispatcher.JumpAsmReturn!;
    }
}

class SimpleCallsJumps : CSharpOverrideHelper {
    public int NearCalled { get; set; }
    public int FarCalled { get; set; }
    public int FarCalled1FromStack { get; set; }
    public int FarCalled2FromStack { get; set; }

    public SimpleCallsJumps(Dictionary<SegmentedAddress, FunctionInformation> functionInformations, Machine machine,
        ILoggerService loggerService) :
        base(functionInformations, machine, loggerService, new Configuration()) {
        DefineFunction(0, 0x200, Far_callee1_from_stack_0000_0200_00200);
        DefineFunction(0, 0x300, Far_callee2_from_stack_0000_0300_00300);
    }

    public Action Entry_1000_0000_10000() {
        NearCall(0x1000, 0, Near_1000_0100_10100);
        FarCall(0x1000, 0, Far_2000_0100_20100);
        FarCall(0x1000, 0, Far_calls_another_far_via_stack_0000_0100_00100);
        return NearRet();
    }

    public Action Near_1000_0100_10100(int loadOffset) {
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
        Stack.Push16(0x1000);
        Stack.Push16(0);
        return FarRet();
    }
}