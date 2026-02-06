namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Mcp;
using Spice86.Logging;

using System.Linq;

using Xunit;

public class McpServerToolStateTests {
    private const string TestProgramName = "add";

    private static (Spice86DependencyInjection spice86, McpServer server) CreateMcpServerForTest() {
        Spice86Creator creator = new Spice86Creator(TestProgramName, false);
        Spice86DependencyInjection spice86 = creator.Create();
        McpServer server = (McpServer)spice86.McpServer;
        return (spice86, server);
    }

    [Fact]
    public void TestDefaultToolState() {
        (Spice86DependencyInjection spice86, McpServer server) = CreateMcpServerForTest();
        using (spice86) {
            var tools = server.GetAvailableTools();
            tools.Should().NotBeEmpty();
            tools.Length.Should().Be(server.GetAllTools().Length);
            
            foreach(var tool in tools) {
                // By default all tools should be enabled
                server.GetAllTools().Should().Contain(t => t.Name == tool.Name);
            }
        }
    }

    [Fact]
    public void TestDisableTool() {
        (Spice86DependencyInjection spice86, McpServer server) = CreateMcpServerForTest();
        using (spice86) {
            string toolName = "read_cpu_registers";
            
            // Verify initially enabled
            server.GetAvailableTools().Should().Contain(t => t.Name == toolName);
            
            // Disable
            server.SetToolEnabled(toolName, false);
            
            // Verify disabled
            server.GetAvailableTools().Should().NotContain(t => t.Name == toolName);
            server.GetAllTools().Should().Contain(t => t.Name == toolName);
            
            // Re-enable
            server.SetToolEnabled(toolName, true);
            
            // Verify enabled
            server.GetAvailableTools().Should().Contain(t => t.Name == toolName);
        }
    }

    [Fact]
    public void TestSetToolEnabled_UnknownTool() {
        (Spice86DependencyInjection spice86, McpServer server) = CreateMcpServerForTest();
        using (spice86) {
            // Should not throw
            server.SetToolEnabled("unknown_tool", false);
            
            // State should be unchanged
            server.GetAvailableTools().Length.Should().Be(server.GetAllTools().Length);
        }
    }
}
