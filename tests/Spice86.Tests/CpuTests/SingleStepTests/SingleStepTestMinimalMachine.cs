namespace Spice86.Tests.CpuTests.SingleStepTests;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

public class SingleStepTestMinimalMachine {
    private readonly IList<uint> _modifiedAddresses = new List<uint>();
    public SingleStepTestMinimalMachine(CpuModel cpuModel) {

        State state = new State(cpuModel);
        State = state;
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        PauseHandler pauseHandler = new PauseHandler(loggerService);
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, state);
        Memory memory = new(emulatorBreakpointsManager.MemoryReadWriteBreakpoints, new Ram(/*0x10FFEF*/1024 * 1024), new A20Gate(false));
        Memory = memory;
        for (uint address = 0; address < memory.Length; address++) {
            // monitor what is written in ram so that we can restore it to 0 after
            AddressBreakPoint breakPoint = new AddressBreakPoint(BreakPointType.MEMORY_WRITE, address,
                breakPoint => _modifiedAddresses.Add((uint)((AddressBreakPoint)breakPoint).Address), false
            );
            emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
        }
        IOPortDispatcher ioPortDispatcher =
            new(emulatorBreakpointsManager.IoReadWriteBreakpoints, state, loggerService, false);
        IOPortHandlerRegistry ioPortHandlerRegistry = new(ioPortDispatcher, state, loggerService, false);
        ExecutionStateSlice executionStateSlice = new(state) {
            CyclesAllocatedForSlice = 1,
            CyclesLeft = 1
        };
        CallbackHandler callbackHandler = new(state, loggerService);
        DualPic dualPic = new(ioPortHandlerRegistry, executionStateSlice, loggerService);
        FunctionCatalogue functionCatalogue = new();
        Cpu = new CfgCpu(memory, state, ioPortDispatcher, callbackHandler, dualPic, executionStateSlice,
            emulatorBreakpointsManager, functionCatalogue, false, loggerService);
    }

    public void RestoreMemoryAfterTest() {
        foreach (uint address in _modifiedAddresses) {
            Memory.SneakilyWrite(address, 0);
        }
        _modifiedAddresses.Clear();
    }

    public CfgCpu Cpu { get; }
    public State State { get; }
    public Memory Memory { get; }
}