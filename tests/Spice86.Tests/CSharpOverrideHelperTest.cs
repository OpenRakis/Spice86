using Spice86.Core.CLI;

namespace Spice86.Tests;

using NSubstitute;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

using Xunit;

public class CSharpOverrideHelperTest {
    private readonly ILoggerService _loggerServiceMock = Substitute.For<ILoggerService>();

    public static IEnumerable<object[]> GetCfgCpuConfigurations() {
        yield return new object[] { false };
        yield return new object[] { true };
    }
    
    private Spice86DependencyInjection CreateDummyProgramExecutor(bool enableCfgCpu, string? overrideSupplierClassName=null) {
        Spice86DependencyInjection res =
            new Spice86Creator("add", enableCfgCpu, overrideSupplierClassName: overrideSupplierClassName).Create();
        // Setup stack
        res.Machine.CpuState.SS = 0x3000;
        res.Machine.CpuState.SP = 0x100;
        return res;
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestJumpReturns(bool enableCfgCpu) {
        using Spice86DependencyInjection res = CreateDummyProgramExecutor(enableCfgCpu);
        Machine machine = res.Machine;
        RecursiveJumps recursiveJumps =
            new RecursiveJumps(new Dictionary<SegmentedAddress, FunctionInformation>(),
                machine,
                _loggerServiceMock, new Configuration());
        recursiveJumps.JumpTarget1(0);
        Assert.Equal(RecursiveJumps.MaxNumberOfJumps, recursiveJumps.NumberOfCallsTo1);
        Assert.Equal(RecursiveJumps.MaxNumberOfJumps, recursiveJumps.NumberOfCallsTo2);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestSimpleCallsJumps(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection =
            CreateDummyProgramExecutor(enableCfgCpu, typeof(SimpleCallsJumpsOverrideSupplier).AssemblyQualifiedName);
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
}

class RecursiveJumps : CSharpOverrideHelper {
    public static int MaxNumberOfJumps = 1;
    public int NumberOfCallsTo1 { get; set; }
    public int NumberOfCallsTo2 { get; set; }

    public RecursiveJumps(IDictionary<SegmentedAddress, FunctionInformation> functionInformations,
        Machine machine, ILoggerService loggerService, Configuration configuration) : base(functionInformations, machine, loggerService, new()) {
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

class SimpleCallsJumpsOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILoggerService loggerService,
        Configuration configuration,
        ushort programStartSegment,
        Machine machine) {
        SimpleCallsJumps callsJumps = new(new Dictionary<SegmentedAddress, FunctionInformation>(), machine, loggerService, configuration);
        return callsJumps.DefineOverrides();
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
        Machine machine, ILoggerService loggerService, Configuration configuration) : base(functionInformations, machine, loggerService, configuration) {
        CurrentInstance = this;
    }

    public IDictionary<SegmentedAddress, FunctionInformation> DefineOverrides() {
        DefineFunction(0xF000, 0xFFF0, Entry_F000_FFF0_FFFF0);
        DefineFunction(0, 0x200, Far_callee1_from_stack_0000_0200_00200);
        DefineFunction(0, 0x300, Far_callee2_from_stack_0000_0300_00300);
        return _functionInformations;
    }

    // bios entry point
    public Action Entry_F000_FFF0_FFFF0(int loadOffset) {
        EntryCalled++;
        NearCall(0xF000, 0xFFF0, Near_F000_0100_F0100);
        FarCall(0xF000, 0xFFF0, Far_2000_0100_20100);
        FarCall(0xF000, 0xFFF0, Far_calls_another_far_via_stack_0000_0100_00100);
        return NearRet();
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