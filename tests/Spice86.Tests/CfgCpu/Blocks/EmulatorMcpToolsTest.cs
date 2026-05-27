namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using ModelContextProtocol.Protocol;

using Spice86;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Mcp;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Mcp;

using System.Text.Json;
using System.Text.Json.Serialization;

using Xunit;

/// <summary>
/// Tests concrete <c>read_cfg_cpu_graph</c> MCP tool scenarios.
/// </summary>
public class EmulatorMcpToolsTest {
    private const string TestProgramName = "add";

    private static readonly JsonSerializerOptions DeserializerOptions = new() {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// After <see cref="CfgCpu.Clear"/> has been invoked, the BFS that
    /// powers <c>read_cfg_cpu_graph</c> has no seed blocks (no entry-point
    /// instructions, no <c>LastExecuted</c>) and therefore must return an
    /// empty <c>Blocks</c> array while still populating the other top-level
    /// fields of the response.
    /// </summary>
    [Fact]
    public void ReadCfgCpuGraph_AfterClear_ReturnsEmptyBlocks() {
        // Arrange boot a real Spice86 dependency-injection graph so that
        // EmulatorMcpTools is wired against a production-shaped CfgCpu and
        // ExecutionContextManager. We do NOT run the program: the test
        // operates on the pristine, then-cleared graph.
        Spice86Creator creator = new(TestProgramName);
        using Spice86DependencyInjection spice86 = creator.Create();
        EmulatorMcpServices services = spice86.McpServices;
        EmulatorMcpTools tools = new(services);

        // Pre-pause so EmulatorMcpTools.ExecuteTool's auto-pause branch does
        // not request a pause that would never resolve without an emulation
        // loop running.
        services.PauseHandler.RequestPause("test setup");

        // Drop every seed: clears ExecutionContextEntryPoints, current
        // execution context (so LastExecuted becomes null), and all
        // instruction caches.
        services.CfgCpu.Clear();

        // Act
        CallToolResult result = tools.ReadCfgCpuGraph(nodeLimit: null);

        // Assert
        result.IsError.Should().NotBe(true);
        result.StructuredContent.Should().NotBeNull();
        CfgCpuGraph response = DeserializeResponse(result);

        response.Blocks.Should().NotBeNull();
        response.Blocks.Should().BeEmpty(
            "after CfgCpu.Clear() there are no entry points and no LastExecuted, " +
            "so the block-centric BFS has no seeds");
        response.Partitions.Should().NotBeNull();
        response.Partitions.Should().BeEmpty();
        response.Transfers.Should().NotBeNull();
        response.Transfers.Should().BeEmpty();
        response.PartitioningRequiresFullGraph.Should().BeNull();
        response.Truncated.Should().BeFalse();
        response.LastExecutedAddress.Should().BeNull();
        response.LastExecutedBlockId.Should().BeNull();
        response.TotalEntryPoints.Should().Be(0);
        response.EntryPointAddresses.Should().BeEmpty();
        response.CurrentContextDepth.Should().Be(0);
    }

    private static CfgCpuGraph DeserializeResponse(CallToolResult result) {
        result.IsError.Should().NotBe(true);
        result.StructuredContent.Should().NotBeNull();

        CfgCpuGraph? response = result.StructuredContent.Value.Deserialize<CfgCpuGraph>(DeserializerOptions);
        if (response == null) {
            throw new InvalidOperationException("The structured MCP response could not be deserialized.");
        }
        return response;
    }

    /// <summary>
    /// When the graph is truncated by nodeLimit, every pred/succ ID in the returned
    /// blocks must reference a block that is also present in the returned block list.
    /// No edge endpoint may reference an omitted block.
    /// </summary>
    [Fact]
    public void ReadCfgCpuGraph_TruncatedGraph_PredSuccAreClosedToIncludedBlocks() {
        // Arrange — run the full "add" program so the CFG is populated with real blocks
        Spice86Creator creator = new(TestProgramName);
        using Spice86DependencyInjection spice86 = creator.Create();
        spice86.ProgramExecutor.Run();
        EmulatorMcpServices services = spice86.McpServices;
        EmulatorMcpTools tools = new(services);
        services.PauseHandler.RequestPause("test setup");

        // Act — nodeLimit=1 forces truncation on any real multi-block program
        CallToolResult result = tools.ReadCfgCpuGraph(nodeLimit: 1);

        // Assert
        result.IsError.Should().NotBe(true);
        CfgCpuGraph response = DeserializeResponse(result);
        response.Blocks.Should().NotBeEmpty("running the program must produce at least one block");
        response.Partitions.Should().BeNull("partitioning a node-limited graph would create references to omitted blocks");
        response.Transfers.Should().BeNull("partitioning a node-limited graph would create references to omitted partitions");
        response.PartitioningRequiresFullGraph.Should().BeTrue();

        HashSet<int> includedIds = response.Blocks.Select(b => b.Id).ToHashSet();
        foreach (CfgBlockInfo block in response.Blocks) {
            foreach (int predId in block.Pred) {
                includedIds.Should().Contain(predId,
                    $"block {block.Id} pred references id {predId} which is not in the returned block list");
            }
            foreach (int succId in block.Succ) {
                includedIds.Should().Contain(succId,
                    $"block {block.Id} succ references id {succId} which is not in the returned block list");
            }
        }
    }
}
