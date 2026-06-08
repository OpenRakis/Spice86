namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using NSubstitute;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Fixtures;

using Xunit;

/// <summary>
/// Tests that <see cref="CfgCpuSnapshotBuilder"/> degrades gracefully when partitioning fails: the block
/// graph is still exported (so reload and inspection keep working) and the failure is logged, instead of the
/// partition invariant violation aborting the whole state dump.
/// </summary>
public sealed class CfgCpuSnapshotBuilderTest : IDisposable {
    private const ushort Seg = 0x1000;

    private readonly LinkerHarness _harness = new();
    private readonly FunctionCatalogue _functionCatalogue = new();
    private readonly ExecutionContextManagerFactory _contextManagerFactory;
    private readonly ExecutionContextManager _contextManager;
    private readonly ILoggerService _loggerService = Substitute.For<ILoggerService>();

    public CfgCpuSnapshotBuilderTest() {
        _contextManagerFactory = new ExecutionContextManagerFactory(_functionCatalogue);
        _contextManager = _contextManagerFactory.ContextManager;
        _loggerService.IsEnabled(Arg.Any<LogEventLevel>()).Returns(true);
    }

    public void Dispose() {
        _harness.Dispose();
        _contextManagerFactory.Dispose();
    }

    [Fact]
    public void Build_PartitioningFailsWithOwnerlessBlock_KeepsBlocksAndLogsError() {
        // Arrange an ownerless graph: an entry-point block and a disconnected last-executed block with no
        // edges between them. The exporter seeds both, but the partitioner cannot attribute the second block
        // to any root and throws, which the builder must catch.
        CfgInstruction entry = CreateInstruction(0x0000);
        CfgInstruction ownerless = CreateInstruction(0x0100);
        CreateBlock(100, entry);
        CreateBlock(101, ownerless);
        RegisterEntry(entry);
        _contextManager.CurrentExecutionContext.LastExecuted = ownerless;
        CfgCpuSnapshotBuilder builder = new(new CfgBlockGraphExporter(), new CfgFunctionPartitioner(),
            _functionCatalogue, _loggerService);

        // Act
        CfgCpuSnapshot snapshot = builder.Build(_contextManager, null);

        // Assert
        snapshot.PartitionedProgram.Should().BeNull("partitioning failed, so the snapshot degrades to blocks-only");
        snapshot.Exported.Graph.Truncated.Should().BeFalse();
        snapshot.Exported.Graph.Blocks.Select(node => node.Block.Id)
            .Should().Contain([100, 101], "the full block graph must survive a partitioning failure");
        _loggerService.Received().Error(Arg.Any<InvalidOperationException>(), Arg.Any<string>(), Arg.Any<string>());
    }

    private void RegisterEntry(CfgInstruction instruction) {
        if (!_contextManager.ExecutionContextEntryPoints.TryGetValue(instruction.Address, out ISet<CfgInstruction>? instructions)) {
            instructions = new HashSet<CfgInstruction>();
            _contextManager.ExecutionContextEntryPoints.Add(instruction.Address, instructions);
        }
        instructions.Add(instruction);
    }

    private CfgInstruction CreateInstruction(ushort offset) =>
        _harness.CreateInstruction(new SegmentedAddress(Seg, offset), 0x90, 1, InstructionKind.None);

    private static CfgBlock CreateBlock(int id, CfgInstruction instruction) {
        CfgBlock block = new(id, instruction);
        instruction.ContainingBlock = block;
        return block;
    }
}
