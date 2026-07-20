namespace Spice86.Tests.CfgCpu.Blocks;

using NSubstitute;
using Microsoft.Extensions.Logging;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Disposable test harness that constructs a real <see cref="NodeLinker"/> with its production
/// dependencies. Each test instantiates a fresh harness so the JIT compiler's background thread
/// does not leak across tests.
/// </summary>
internal sealed class LinkerHarness : IDisposable {
    private readonly CfgNodeExecutionCompiler _compiler;
    private readonly CfgNodeExecutionCompilerMonitor _monitor;
    private readonly SequentialIdAllocator _idAllocator;

    public LinkerHarness() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        _monitor = new CfgNodeExecutionCompilerMonitor(loggerService);
        _compiler = new CfgNodeExecutionCompiler(_monitor, loggerService, JitMode.InterpretedOnly);
        _idAllocator = new SequentialIdAllocator();
        Linker = new NodeLinker(new InstructionReplacerRegistry(), _compiler, _idAllocator);
    }

    public NodeLinker Linker { get; }

    /// <summary>
    /// Builds a synthetic NOP <see cref="CfgInstruction"/> at <paramref name="address"/>.
    /// </summary>
    public CfgInstruction CreateInstruction(SegmentedAddress address) {
        return CfgTestHelpers.CreateInstruction(address);
    }

    /// <summary>
    /// Builds a synthetic <see cref="CfgInstruction"/> at <paramref name="address"/> with the
    /// specified <paramref name="opcode"/>, <paramref name="length"/>, and <paramref name="kind"/>.
    /// </summary>
    public CfgInstruction CreateInstruction(SegmentedAddress address, byte opcode, int length, InstructionKind kind) {
        return CfgTestHelpers.CreateInstruction(address, opcode, length, kind);
    }

    public void Dispose() {
        _compiler.Dispose();
        _monitor.Dispose();
    }
}
