namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Mcp;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;

using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit;

public class McpServerTest {
    private const string TestProgramName = "add";

    private static (Spice86DependencyInjection spice86, McpServer server, FunctionCatalogue functionCatalogue) CreateMcpServerForTest() {
        Spice86Creator creator = new Spice86Creator(TestProgramName, false);
        Spice86DependencyInjection spice86 = creator.Create();
        FunctionCatalogue functionCatalogue = spice86.FunctionCatalogue;
        McpServer server = (McpServer)spice86.McpServer;
        return (spice86, server, functionCatalogue);
    }

    [Fact]
    public void TestInitialize() {
        (Spice86DependencyInjection spice86, McpServer server, FunctionCatalogue _) = CreateMcpServerForTest();
        using (spice86) {
            string request = """{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2025-06-18"},"id":1}""";

            string response = server.HandleRequest(request);

            JsonNode? responseNode = JsonNode.Parse(response);
            responseNode.Should().NotBeNull();
            if (responseNode != null) {
                responseNode["jsonrpc"]?.GetValue<string>().Should().Be("2.0");
                responseNode["id"]?.GetValue<int>().Should().Be(1);
                responseNode["result"]?["protocolVersion"]?.GetValue<string>().Should().Be("2025-06-18");
                responseNode["result"]?["serverInfo"]?["name"]?.GetValue<string>().Should().Be("Spice86 MCP Server");
            }
        }
    }

    [Fact]
    public void TestToolsList() {
        (Spice86DependencyInjection spice86, McpServer server, FunctionCatalogue _) = CreateMcpServerForTest();
        using (spice86) {
            string request = """{"jsonrpc":"2.0","method":"tools/list","id":2}""";

            string response = server.HandleRequest(request);

        JsonNode? responseNode = JsonNode.Parse(response);
        responseNode.Should().NotBeNull();
        if (responseNode != null) {
            responseNode["jsonrpc"]?.GetValue<string>().Should().Be("2.0");
            responseNode["id"]?.GetValue<int>().Should().Be(2);

            JsonArray? tools = responseNode["result"]?["tools"]?.AsArray();
            tools.Should().NotBeNull();
            if (tools != null) {
                tools.Count.Should().BeGreaterThanOrEqualTo(3);

                string[] toolNames = tools.Select(t => t?["name"]?.GetValue<string>() ?? "").ToArray();
                toolNames.Should().Contain("read_cpu_registers");
                toolNames.Should().Contain("read_memory");
                toolNames.Should().Contain("list_functions");
                toolNames.Should().Contain("read_io_port");
                toolNames.Should().Contain("write_io_port");
                toolNames.Should().Contain("get_video_state");
                toolNames.Should().Contain("screenshot");
                toolNames.Should().Contain("pause_emulator");
                toolNames.Should().Contain("resume_emulator");
            }
        }
        }
    }

    [Fact]
    public void TestReadCpuRegisters() {
        (Spice86DependencyInjection spice86, McpServer server, FunctionCatalogue _) = CreateMcpServerForTest();
        using (spice86) {
            spice86.Machine.CpuState.EAX = 0x12345678;
            spice86.Machine.CpuState.EBX = 0xABCDEF01;
            spice86.Machine.CpuState.CS = 0x1000;
            spice86.Machine.CpuState.IP = 0x0100;

            string request = """{"jsonrpc":"2.0","method":"tools/call","params":{"name":"read_cpu_registers","arguments":{}},"id":3}""";

            string response = server.HandleRequest(request);

            JsonNode? responseNode = JsonNode.Parse(response);
            responseNode.Should().NotBeNull();
            if (responseNode != null) {
                responseNode["jsonrpc"]?.GetValue<string>().Should().Be("2.0");
                responseNode["id"]?.GetValue<int>().Should().Be(3);

                string? resultText = responseNode["result"]?["content"]?[0]?["text"]?.GetValue<string>();
                resultText.Should().NotBeNull();

                if (resultText != null) {
                    JsonNode? registers = JsonNode.Parse(resultText);
                    registers.Should().NotBeNull();
                    if (registers != null) {
                        registers["generalPurpose"]?["EAX"]?.GetValue<uint>().Should().Be(0x12345678);
                        registers["generalPurpose"]?["EBX"]?.GetValue<uint>().Should().Be(0xABCDEF01);
                        registers["segments"]?["CS"]?.GetValue<ushort>().Should().Be(0x1000);
                        registers["instructionPointer"]?["IP"]?.GetValue<ushort>().Should().Be(0x0100);
                    }
                }
            }
        }
    }

    [Fact]
    public void TestReadMemory() {
        (Spice86DependencyInjection spice86, McpServer server, FunctionCatalogue _) = CreateMcpServerForTest();
        using (spice86) {
            byte[] testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            spice86.Machine.Memory.WriteRam(testData, 0x1000);

            string request = """{"jsonrpc":"2.0","method":"tools/call","params":{"name":"read_memory","arguments":{"address":4096,"length":5}},"id":4}""";

            string response = server.HandleRequest(request);

            JsonNode? responseNode = JsonNode.Parse(response);
            responseNode.Should().NotBeNull();
            if (responseNode != null) {
                responseNode["jsonrpc"]?.GetValue<string>().Should().Be("2.0");
                responseNode["id"]?.GetValue<int>().Should().Be(4);

                string? resultText = responseNode["result"]?["content"]?[0]?["text"]?.GetValue<string>();
                resultText.Should().NotBeNull();

                if (resultText != null) {
                    JsonNode? memoryData = JsonNode.Parse(resultText);
                    memoryData.Should().NotBeNull();
                    if (memoryData != null) {
                        memoryData["address"]?.GetValue<uint>().Should().Be(4096);
                        memoryData["length"]?.GetValue<int>().Should().Be(5);
                        memoryData["data"]?.GetValue<string>().Should().Be("0102030405");
                    }
                }
            }
        }
    }

    [Fact]
    public void TestListFunctions() {
        (Spice86DependencyInjection spice86, McpServer server, FunctionCatalogue functionCatalogue) = CreateMcpServerForTest();
        using (spice86) {
            FunctionInformation func1 = functionCatalogue.GetOrCreateFunctionInformation(new SegmentedAddress(0x1000, 0x0000), "TestFunction1");
            func1.Enter(null);
            func1.Enter(null);

            FunctionInformation func2 = functionCatalogue.GetOrCreateFunctionInformation(new SegmentedAddress(0x2000, 0x0100), "TestFunction2");
            func2.Enter(null);

            string request = """{"jsonrpc":"2.0","method":"tools/call","params":{"name":"list_functions","arguments":{"limit":10}},"id":5}""";

            string response = server.HandleRequest(request);

            JsonNode? responseNode = JsonNode.Parse(response);
            responseNode.Should().NotBeNull();
            if (responseNode != null) {
                responseNode["jsonrpc"]?.GetValue<string>().Should().Be("2.0");
                responseNode["id"]?.GetValue<int>().Should().Be(5);

                string? resultText = responseNode["result"]?["content"]?[0]?["text"]?.GetValue<string>();
                resultText.Should().NotBeNull();

                if (resultText != null) {
                    JsonNode? functionData = JsonNode.Parse(resultText);
                    functionData.Should().NotBeNull();
                    if (functionData != null) {
                        functionData["totalCount"]?.GetValue<int>().Should().Be(2);

                        JsonArray? functions = functionData["functions"]?.AsArray();
                        functions.Should().NotBeNull();
                        if (functions != null) {
                            functions.Count.Should().Be(2);

                            functions[0]?["name"]?.GetValue<string>().Should().Be("TestFunction1");
                            functions[0]?["calledCount"]?.GetValue<int>().Should().Be(2);
                            functions[1]?["name"]?.GetValue<string>().Should().Be("TestFunction2");
                            functions[1]?["calledCount"]?.GetValue<int>().Should().Be(1);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void TestInvalidJson() {
        (Spice86DependencyInjection spice86, McpServer server, FunctionCatalogue _) = CreateMcpServerForTest();
        using (spice86) {
            string request = "invalid json";

            string response = server.HandleRequest(request);

            JsonNode? responseNode = JsonNode.Parse(response);
            responseNode.Should().NotBeNull();
            if (responseNode != null) {
                responseNode["error"]?["code"]?.GetValue<int>().Should().Be(-32700);
                responseNode["error"]?["message"]?.GetValue<string>().Should().Contain("Parse error");
            }
        }
    }

    [Fact]
    public void TestUnknownMethod() {
        (Spice86DependencyInjection spice86, McpServer server, FunctionCatalogue _) = CreateMcpServerForTest();
        using (spice86) {
            string request = """{"jsonrpc":"2.0","method":"unknown_method","id":99}""";

            string response = server.HandleRequest(request);

            JsonNode? responseNode = JsonNode.Parse(response);
            responseNode.Should().NotBeNull();
            if (responseNode != null) {
                responseNode["error"]?["code"]?.GetValue<int>().Should().Be(-32601);
                responseNode["error"]?["message"]?.GetValue<string>().Should().Contain("Method not found");
            }
        }
    }

    [Fact]
    public void TestReadMemoryInvalidLength() {
        (Spice86DependencyInjection spice86, McpServer server, FunctionCatalogue _) = CreateMcpServerForTest();
        using (spice86) {
            string request = """{"jsonrpc":"2.0","method":"tools/call","params":{"name":"read_memory","arguments":{"address":0,"length":10000}},"id":6}""";

            string response = server.HandleRequest(request);

            JsonNode? responseNode = JsonNode.Parse(response);
            responseNode.Should().NotBeNull();
            if (responseNode != null) {
                responseNode["error"]?["code"]?.GetValue<int>().Should().Be(-32603);
                responseNode["error"]?["message"]?.GetValue<string>().Should().Contain("Tool execution error");
            }
        }
    }
}