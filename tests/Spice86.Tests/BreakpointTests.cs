namespace Spice86.Tests;

using JetBrains.Annotations;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;

using Xunit;

public class BreakpointTests {
    public static IEnumerable<object[]> GetCfgCpuConfigurations() {
        yield return new object[] { false };
        yield return new object[] { true };
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestMemoryBreakpoints(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        IMemory memory = spice86DependencyInjection.Machine.Memory;

        // simple read
        // 2 reads, but breakpoint is removed after first
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_READ, 0, 1, true, () => {
            _ = memory.UInt8[0];
            _ = memory.UInt8[0];
        });

        // simple write
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_WRITE, 0, 1, true, () => {
            memory.UInt8[0] = 0;
        });

        // read / write with remove
        int readWrite0Triggered = 0;
        AddressBreakPoint readWrite0 = new AddressBreakPoint(BreakPointType.MEMORY_ACCESS, 0, breakpoint => { readWrite0Triggered++; }, false);
        emulatorBreakpointsManager.ToggleBreakPoint(readWrite0, true);
        _ =  memory.UInt8[0];
        memory.UInt8[0] = 0;
        emulatorBreakpointsManager.ToggleBreakPoint(readWrite0, false);
        // Should not trigger
        _ =  memory.UInt8[0];
        Assert.Equal(2, readWrite0Triggered);

        // Memset
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_WRITE, 5, 1, true, () => {
            memory.Memset8(0, 0, 6);
            // Should not trigger for this
            memory.Memset8(0, 0, 5);
            memory.Memset8(6, 0, 5);
        });
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_WRITE, 5, 1, true, () => {
            memory.Memset8(5, 0, 5);
        });

        // GetData
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_READ, 5, 1, true, () => {
            memory.GetSpan(5, 10);
            // Should not trigger for this
            memory.GetSpan(0, 5);
            memory.GetSpan(6, 5);
        });
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_READ, 5, 1, true, () => {
            memory.GetSpan(0, 6);
        });

        // LoadData
        byte[] data = new byte[] { 1, 2, 3 };
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_WRITE, 5, 1, true, () => {
            memory.LoadData(5, data);
            // Should not trigger for this
            memory.LoadData(2, data);
            memory.LoadData(6, data);
        });
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_WRITE, 5, 1, true, () => {
            memory.LoadData(3, data);
        });
        // Bonus test for search
        uint? address = memory.SearchValue(0, 10, data);
        Assert.NotNull(address);
        Assert.Equal(3, (int)address!);

        //MemCopy
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_WRITE, 10, 1, true, () => {
            memory.MemCopy(0, 10, 10);
            // Should not trigger for this
            memory.MemCopy(0, 20, 10);
        });
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_READ, 0, 1, true, () => {
            memory.MemCopy(0, 10, 10);
            // Should not trigger for this
            memory.MemCopy(1, 10, 10);
        });

        // Long reads
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_READ, 1, 2, false, () => {
            _ = memory.UInt16[0];
            _ = memory.UInt32[0];
        });

        // Long writes
        AssertAddressMemoryBreakPoint(emulatorBreakpointsManager, BreakPointType.MEMORY_WRITE, 1, 2, false, () => {
            memory.UInt16[0] = 0;
            memory.UInt32[0] = 0;
        });
    }

    [AssertionMethod]
    private void AssertAddressMemoryBreakPoint(EmulatorBreakpointsManager emulatorBreakpointsManager, BreakPointType breakPointType, uint address, int expectedTriggers, bool isRemovedOnTrigger, Action action) {
        int count = 0;
        AddressBreakPoint breakPoint = new AddressBreakPoint(breakPointType, address, breakpoint => { count++; }, isRemovedOnTrigger);
        emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
        action.Invoke();
        emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, false);
        Assert.Equal(expectedTriggers, count);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestExecutionBreakpoints(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("add", enableCfgCpu: enableCfgCpu).Create();
        State state = spice86DependencyInjection.Machine.CpuState;
        Machine machine = spice86DependencyInjection.Machine;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        int triggers = 0;
        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_CYCLES, 10, breakpoint => {
            Assert.Equal(10, state.Cycles);
            triggers++;
        }, true), true);
        // Address of cycle 10 to test multiple breakpoints
        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_EXECUTION_ADDRESS, 0xF001C, breakpoint => {
            Assert.Equal(0xF001C, (int)state.IpPhysicalAddress);
            triggers++;
        }, true), true);
        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.MACHINE_STOP, 0, breakpoint => {
            Assert.Equal(0xF01A9, (int)state.IpPhysicalAddress);
            Assert.False(machine.CpuState.IsRunning);
            triggers++;
        }, true), true);
        programExecutor.Run();
        Assert.Equal(3, triggers);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestIoBreakpoints(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("externalint", enableCfgCpu: enableCfgCpu, maxCycles: 0xFFFFFFF, enablePit: true).Create();
        State state = spice86DependencyInjection.Machine.CpuState;
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        IOPortDispatcher ioPortDispatcher = spice86DependencyInjection.Machine.IoPortDispatcher;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        int triggers = 0;
        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.IO_WRITE, 0x43, breakpoint => {
            Assert.Equal(8, state.Cycles);
            Assert.Equal(0x43, ioPortDispatcher.LastPortWritten);
            Assert.Equal((uint)0b00110110, ioPortDispatcher.LastPortWrittenValue);
            triggers++;
        }, true), true);
        programExecutor.Run();
        Assert.Equal(1, triggers);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestExternalInterruptBreakpoints(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("externalint", enableCfgCpu: enableCfgCpu, maxCycles: 0xFFFFFFF, enablePit: true).Create();
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        int triggers = 0;
        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_INTERRUPT, 0x8, breakpoint => {
            triggers++;
        }, false), true);
        programExecutor.Run();
        Assert.Equal(356, triggers);
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestProgrammaticInterruptBreakpoints(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator("interrupt", enableCfgCpu: enableCfgCpu).Create();
        EmulatorBreakpointsManager emulatorBreakpointsManager = spice86DependencyInjection.Machine.EmulatorBreakpointsManager;
        ProgramExecutor programExecutor = spice86DependencyInjection.ProgramExecutor;
        int intDtriggers = 0;
        int intOtriggers = 0;
        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_INTERRUPT, 0xD, breakpoint => {
            intDtriggers++;
        }, false), true);
        // Int on overflow
        emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_INTERRUPT, 0x4, breakpoint => {
            intOtriggers++;
        }, false), true);
        programExecutor.Run();
        Assert.Equal(1, intDtriggers);
        Assert.Equal(1, intOtriggers);
    }
}