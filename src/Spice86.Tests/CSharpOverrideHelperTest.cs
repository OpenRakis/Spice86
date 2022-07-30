namespace Spice86.Tests;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;

using System;
using System.Collections.Generic;

using Xunit;

public class CSharpOverrideHelperTest {
    private ProgramExecutor createDummyProgramExecutor() {
        ProgramExecutor res =  new MachineCreator().CreateProgramExecutorFromBinName("add");
        Machine machine = res.Machine;
        // Setup stack
        machine.Cpu.State.SS = 0;
        machine.Cpu.State.SP = 100;
        return res;
    }

    [Fact]
    void TestJumpReturns() {
        using ProgramExecutor programExecutor = createDummyProgramExecutor();
        RecursiveJumps recursiveJumps =
            new RecursiveJumps(new Dictionary<SegmentedAddress, FunctionInformation>(), programExecutor.Machine);
        recursiveJumps.jumpTarget1(0);
        Assert.Equal(RecursiveJumps.MaxNumberOfJumps, recursiveJumps.NumberOfCallsTo1);
        Assert.Equal(RecursiveJumps.MaxNumberOfJumps, recursiveJumps.NumberOfCallsTo2);
    }

    [Fact]
    void testSimpleCallsJumps() {
        using ProgramExecutor programExecutor = createDummyProgramExecutor();
        
        SimpleCallsJumps callsJumps = new SimpleCallsJumps(new Dictionary<SegmentedAddress, FunctionInformation>(),
            programExecutor.Machine);
        callsJumps.entry_1000_0000_10000(0);
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
        Machine machine) : base(
        functionInformations, machine) {
    }

    public Action jumpTarget1(int loadOffset) {
        entrydispatcher:
        NumberOfCallsTo1++;
        if (JumpDispatcher.Jump(jumpTarget2, 0)) {
            loadOffset = JumpDispatcher.NextEntryAddress;
            goto entrydispatcher;
        }

        return JumpDispatcher.JumpAsmReturn!;
    }

    public Action jumpTarget2(int loadOffset) {
        entrydispatcher:
        NumberOfCallsTo2++;
        if (NumberOfCallsTo2 == MaxNumberOfJumps) {
            return NearRet();
        }

        if (JumpDispatcher.Jump(jumpTarget1, 0)) {
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

    public SimpleCallsJumps(Dictionary<SegmentedAddress, FunctionInformation> functionInformations, Machine machine) :
        base(functionInformations, machine) {
        DefineFunction(0, 0x200, far_callee1_from_stack_0000_0200_00200);
        DefineFunction(0, 0x300, far_callee2_from_stack_0000_0300_00300);
    }

    public Action entry_1000_0000_10000(int loadOffset) {
        NearCall(0x1000, 0, near_1000_0100_10100);
        FarCall(0x1000, 0, far_2000_0100_20100);
        FarCall(0x1000, 0, far_calls_another_far_via_stack_0000_0100_00100);
        return NearRet();
    }

    public Action near_1000_0100_10100(int loadOffset) {
        NearCalled++;
        return NearRet();
    }

    public Action far_2000_0100_20100(int loadOffset) {
        FarCalled++;
        return FarRet();
    }

    public Action far_calls_another_far_via_stack_0000_0100_00100(int loadOffset) {
        // Replace value on stack to call far_callee1_from_stack_3000_0200_10000 when returning, evil!!
        Stack.Pop();
        Stack.Pop();
        Stack.Push(0);
        Stack.Push(0x200);
        return FarRet();
    }

    public Action far_callee1_from_stack_0000_0200_00200(int loadOffset) {
        FarCalled1FromStack++;
        // Call of far_callee2_from_stack_3000_0300_10000 when returning. No need to replace value on stack as we were not called conventionally
        Stack.Push(0);
        Stack.Push(0x300);
        return FarRet();
    }

    public Action far_callee2_from_stack_0000_0300_00300(int loadOffset) {
        FarCalled2FromStack++;
        // Push back the values of the expected return address
        Stack.Push(0x1000);
        Stack.Push(0);
        return FarRet();
    }
}