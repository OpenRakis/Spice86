namespace Spice86.Tests.CpuTests.SingleStepTests;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

public class SingleStepTestMinimalMachine : IDisposable {
    private readonly IList<uint> _modifiedAddresses = new List<uint>();
    private readonly CfgNodeExecutionCompiler _cfgNodeExecutionCompiler;
    public SingleStepTestMinimalMachine(CpuModel cpuModel) {

        State state = new State(cpuModel);
        State = state;
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        PauseHandler pauseHandler = new PauseHandler(loggerService);
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IMmu mmu = RealModeMmuFactory.FromCpuModel(cpuModel);
        Memory memory = new(memoryBreakpoints, new Ram(A20Gate.EndOfHighMemoryArea + 1u), new A20Gate(), mmu, false);
        Memory = memory;
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, state, memory, memoryBreakpoints, ioBreakpoints);
        for (uint address = 0; address < memory.Length; address++) {
            // monitor what is written in ram so that we can restore it to 0 after
            AddressBreakPoint breakPoint = new AddressBreakPoint(BreakPointType.MEMORY_WRITE, address, 
                breakPoint => _modifiedAddresses.Add((uint)((AddressBreakPoint)breakPoint).Address), false
            );
            emulatorBreakpointsManager.ToggleBreakPoint(breakPoint, true);
        }
        IOPortDispatcher ioPortDispatcher =
            new(emulatorBreakpointsManager.IoReadWriteBreakpoints, state, loggerService, false);
        CallbackHandler callbackHandler = new(state, loggerService);
        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);
        _ = new SstPortHandler(state, loggerService, ioPortDispatcher);
        FunctionCatalogue functionCatalogue = new();
        CfgNodeExecutionCompiler executionCompiler = new CfgNodeExecutionCompiler(new CfgNodeExecutionCompilerMonitor(loggerService), loggerService, JitMode.InterpretedOnly);
        _cfgNodeExecutionCompiler = executionCompiler;
        Cpu = new CfgCpu(memory, state, ioPortDispatcher, callbackHandler, dualPic,
            emulatorBreakpointsManager, functionCatalogue, false, false, true, loggerService,
            executionCompiler);
    }

    public void RestoreMemoryAfterTest() {
        foreach (uint address in _modifiedAddresses) {
            Memory.SneakilyWrite(address, 0);
        }
        _modifiedAddresses.Clear();
    }

    /// <inheritdoc />
    public void Dispose() {
        _cfgNodeExecutionCompiler.Dispose();
    }
    
    public CfgCpu Cpu { get; }
    public State State { get; }
    public Memory Memory { get; }

    /// <summary>
    /// 386EX various on-board peripherals seemed to reply non FFFF values on some ports. we need to support that.
    /// </summary>
    private class SstPortHandler : DefaultIOPortHandler {
        private const ushort Port1F = 0x1F;
        private const ushort Port21 = 0x21;
        private const ushort PortA0 = 0xA0;
        private const ushort PortA1 = 0xA1;

        public SstPortHandler(State state, ILoggerService loggerService, IOPortDispatcher ioPortDispatcher)
            : base(state, false, loggerService) {
            ioPortDispatcher.AddIOPortHandler(Port1F, this);
            ioPortDispatcher.ReplaceIOPortHandler(Port21, this);
            ioPortDispatcher.ReplaceIOPortHandler(PortA0, this);
            ioPortDispatcher.ReplaceIOPortHandler(PortA1, this);
        }

        public override ushort ReadWord(ushort port) {
            if (port == Port21) {
                return 0x7FFF;
            }
            return base.ReadWord(port);
        }

        public override uint ReadDWord(ushort port) {
            if (port == Port1F) {
                return 0x7FFFFFFF;
            }
            if (port == Port21) {
                return 0xFF427FFF;
            }
            return base.ReadDWord(port);
        }
    }
}
