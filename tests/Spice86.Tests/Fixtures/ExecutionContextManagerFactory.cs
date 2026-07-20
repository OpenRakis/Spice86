namespace Spice86.Tests.Fixtures;

using NSubstitute;
using Microsoft.Extensions.Logging;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Builds the common Memory + State + ExecutionContextManager graph used by multiple test classes.
/// Implements <see cref="IDisposable"/> to clean up the JIT compiler resources.
/// </summary>
internal sealed class ExecutionContextManagerFactory : IDisposable {
    private readonly CfgNodeExecutionCompilerMonitor _monitor;
    private readonly CfgNodeExecutionCompiler _compiler;

    public Memory Memory { get; }
    public State State { get; }
    public ExecutionContextManager ContextManager { get; }

    public ExecutionContextManagerFactory(FunctionCatalogue functionCatalogue) {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        Memory = new Memory(memoryBreakpoints, new Ram(0x100000), new A20Gate(), new RealModeMmu386(), false);
        State = new State(CpuModel.INTEL_80286);
        InstructionReplacerRegistry replacerRegistry = new();
        _monitor = new CfgNodeExecutionCompilerMonitor(loggerService);
        _compiler = new CfgNodeExecutionCompiler(_monitor, loggerService, JitMode.InterpretedOnly);
        Spice86.Core.Emulator.VM.PauseHandler pauseHandler = new(loggerService);
        CfgNodeFeeder feeder = new(Memory, State, new EmulatorBreakpointsManager(
            pauseHandler, State, Memory,
            memoryBreakpoints, new AddressReadWriteBreakpoints()), replacerRegistry, _compiler, new SequentialIdAllocator(), enableSpeculativeExploration: false);
        ContextManager = new ExecutionContextManager(Memory, State, feeder, replacerRegistry,
            functionCatalogue, false, loggerService, null);
    }

    public void Dispose() {
        _compiler.Dispose();
        _monitor.Dispose();
    }
}
