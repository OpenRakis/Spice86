namespace Spice86.Core.Emulator.Mcp;

using ModelContextProtocol.Protocol;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// In-process Model Context Protocol (MCP) server for inspecting emulator state.
/// This server exposes tools to query CPU registers, memory contents, function definitions, and CFG CPU state.
/// Uses ModelContextProtocol.Core SDK for protocol types while avoiding Microsoft.Extensions.DependencyInjection.
/// Automatically pauses the emulator before inspection to ensure thread-safe access to state.
/// Thread-safe: All requests are serialized using an internal lock, allowing concurrent calls from multiple threads.
/// </summary>
public sealed class McpServer : IMcpServer {
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly CfgCpu? _cfgCpu;
    private readonly IPauseHandler _pauseHandler;
    private readonly ILoggerService _loggerService;
    private readonly Tool[] _tools;
    private readonly object _requestLock = new object();

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServer"/> class.
    /// </summary>
    /// <param name="memory">The memory bus to inspect.</param>
    /// <param name="state">The CPU state to inspect.</param>
    /// <param name="functionCatalogue">The function catalogue to query.</param>
    /// <param name="cfgCpu">The CFG CPU instance (optional, null if not using CFG CPU).</param>
    /// <param name="pauseHandler">The pause handler for safe state inspection.</param>
    /// <param name="loggerService">The logger service for diagnostics.</param>
    public McpServer(IMemory memory, State state, FunctionCatalogue functionCatalogue, CfgCpu? cfgCpu, IPauseHandler pauseHandler, ILoggerService loggerService) {
        _memory = memory;
        _state = state;
        _functionCatalogue = functionCatalogue;
        _cfgCpu = cfgCpu;
        _pauseHandler = pauseHandler;
        _loggerService = loggerService;
        _tools = CreateTools();
    }

    private Tool[] CreateTools() {
        Tool[] baseTools = new Tool[] {
            new Tool {
                Name = "read_cpu_registers",
                Description = "Read the current values of CPU registers (general purpose, segment, instruction pointer, and flags)",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            },
            new Tool {
                Name = "read_memory",
                Description = "Read a range of bytes from emulator memory",
                InputSchema = ConvertToJsonElement(CreateMemoryReadInputSchema())
            },
            new Tool {
                Name = "list_functions",
                Description = "List all known functions in the function catalogue",
                InputSchema = ConvertToJsonElement(CreateFunctionListInputSchema())
            }
        };

        // Add CFG CPU tool only if CFG CPU is available
        if (_cfgCpu != null) {
            Tool[] allTools = new Tool[baseTools.Length + 1];
            baseTools.CopyTo(allTools, 0);
            allTools[baseTools.Length] = new Tool {
                Name = "read_cfg_cpu_graph",
                Description = "Read Control Flow Graph CPU statistics and execution context information",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            };
            return allTools;
        }

        return baseTools;
    }

    private static JsonElement ConvertToJsonElement(object schema) {
        JsonSerializerOptions options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        string json = JsonSerializer.Serialize(schema, options);
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <inheritdoc />
    public string HandleRequest(string requestJson) {
        JsonDocument document;
        try {
            document = JsonDocument.Parse(requestJson);
        } catch (JsonException ex) {
            _loggerService.Error(ex, "JSON parsing error in MCP request");
            return CreateErrorResponse(null, -32700, $"Parse error: {ex.Message}");
        }

        using (document) {
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("method", out JsonElement methodElement)) {
                return CreateErrorResponse(null, -32600, "Invalid Request: Missing method");
            }

            string? method = methodElement.GetString();
            if (string.IsNullOrEmpty(method)) {
                return CreateErrorResponse(null, -32600, "Invalid Request: Missing method");
            }

            JsonElement? idElement = root.TryGetProperty("id", out JsonElement id) ? id : null;

            switch (method) {
                case "initialize":
                    return HandleInitialize(idElement);
                case "tools/list":
                    return HandleToolsList(idElement);
                case "tools/call":
                    return HandleToolCall(root, idElement);
                default:
                    return CreateErrorResponse(idElement, -32601, $"Method not found: {method}");
            }
        }
    }

    /// <inheritdoc />
    public Tool[] GetAvailableTools() {
        return _tools;
    }

    private string HandleInitialize(JsonElement? id) {
        InitializeResult response = new InitializeResult {
            ProtocolVersion = "2025-06-18",
            ServerInfo = new Implementation {
                Name = "Spice86 MCP Server",
                Version = "1.0.0"
            },
            Capabilities = new ServerCapabilities {
                Tools = new()
            }
        };

        return CreateSuccessResponse(id, response);
    }

    private string HandleToolsList(JsonElement? id) {
        Tool[] tools = GetAvailableTools();

        ListToolsResult response = new ListToolsResult {
            Tools = tools
        };

        return CreateSuccessResponse(id, response);
    }

    private string HandleToolCall(JsonElement root, JsonElement? id) {
        if (!root.TryGetProperty("params", out JsonElement paramsElement)) {
            return CreateErrorResponse(id, -32602, "Invalid params: Missing params");
        }

        if (!paramsElement.TryGetProperty("name", out JsonElement nameElement)) {
            return CreateErrorResponse(id, -32602, "Invalid params: Missing tool name");
        }

        string? toolName = nameElement.GetString();
        if (string.IsNullOrEmpty(toolName)) {
            return CreateErrorResponse(id, -32602, "Invalid params: Missing tool name");
        }

        JsonElement? argumentsElement = paramsElement.TryGetProperty("arguments", out JsonElement args) ? args : null;

        // Thread-safe: serialize all MCP requests to prevent concurrent access
        lock (_requestLock) {
            // Pause emulator before inspecting state to ensure consistency
            bool wasPaused = _pauseHandler.IsPaused;
            if (!wasPaused) {
                _pauseHandler.RequestPause($"MCP tool execution: {toolName}");
                // Wait for pause to take effect by waiting for Paused event
                // The PauseHandler's Paused event is invoked immediately after setting _pausing = true,
                // so by the time RequestPause returns, the pause has taken effect
            }

            try {
                object result;
                switch (toolName) {
                    case "read_cpu_registers":
                        result = ReadCpuRegisters();
                        break;
                    case "read_memory":
                        result = ReadMemory(argumentsElement);
                        break;
                    case "list_functions":
                        result = ListFunctions(argumentsElement);
                        break;
                    case "read_cfg_cpu_graph":
                        result = ReadCfgCpuGraph();
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown tool: {toolName}");
                }

                return CreateToolCallResponse(id, result);
            } catch (ArgumentException ex) {
                _loggerService.Error(ex, "Error executing tool {ToolName}", toolName);
                return CreateErrorResponse(id, -32602, $"Invalid params: {ex.Message}");
            } catch (InvalidOperationException ex) {
                _loggerService.Error(ex, "Error executing tool {ToolName}", toolName);
                return CreateErrorResponse(id, -32603, $"Tool execution error: {ex.Message}");
            } finally {
                // Resume emulator if we paused it
                if (!wasPaused && _pauseHandler.IsPaused) {
                    _pauseHandler.Resume();
                }
            }
        }
    }

    private CpuRegistersResponse ReadCpuRegisters() {
        return new CpuRegistersResponse {
            GeneralPurpose = new GeneralPurposeRegisters {
                EAX = _state.EAX,
                EBX = _state.EBX,
                ECX = _state.ECX,
                EDX = _state.EDX,
                ESI = _state.ESI,
                EDI = _state.EDI,
                ESP = _state.ESP,
                EBP = _state.EBP
            },
            Segments = new SegmentRegisters {
                CS = _state.CS,
                DS = _state.DS,
                ES = _state.ES,
                FS = _state.FS,
                GS = _state.GS,
                SS = _state.SS
            },
            InstructionPointer = new InstructionPointer {
                IP = _state.IP
            },
            Flags = new CpuFlags {
                CarryFlag = _state.CarryFlag,
                ParityFlag = _state.ParityFlag,
                AuxiliaryFlag = _state.AuxiliaryFlag,
                ZeroFlag = _state.ZeroFlag,
                SignFlag = _state.SignFlag,
                DirectionFlag = _state.DirectionFlag,
                OverflowFlag = _state.OverflowFlag,
                InterruptFlag = _state.InterruptFlag
            }
        };
    }

    private MemoryReadResponse ReadMemory(JsonElement? arguments) {
        if (arguments == null || !arguments.HasValue) {
            throw new ArgumentException("Missing arguments for read_memory");
        }

        JsonElement argsValue = arguments.Value;

        if (!argsValue.TryGetProperty("address", out JsonElement addressElement)) {
            throw new ArgumentException("Missing address parameter");
        }

        if (!argsValue.TryGetProperty("length", out JsonElement lengthElement)) {
            throw new ArgumentException("Missing length parameter");
        }

        uint address = addressElement.GetUInt32();
        int length = lengthElement.GetInt32();

        if (length <= 0 || length > 4096) {
            throw new InvalidOperationException("Length must be between 1 and 4096");
        }

        byte[] data = _memory.ReadRam((uint)length, address);

        return new MemoryReadResponse {
            Address = address,
            Length = length,
            Data = Convert.ToHexString(data)
        };
    }

    private FunctionListResponse ListFunctions(JsonElement? arguments) {
        int limit = 100;

        if (arguments != null) {
            JsonElement argsValue = arguments.Value;
            if (argsValue.TryGetProperty("limit", out JsonElement limitElement)) {
                limit = limitElement.GetInt32();
            }
        }

        FunctionInfo[] functions = _functionCatalogue.FunctionInformations.Values
            .OrderByDescending(f => f.CalledCount)
            .Take(limit)
            .Select(f => new FunctionInfo {
                Address = f.Address.ToString(),
                Name = f.Name,
                CalledCount = f.CalledCount,
                HasOverride = f.HasOverride
            })
            .ToArray();

        return new FunctionListResponse {
            Functions = functions,
            TotalCount = _functionCatalogue.FunctionInformations.Count
        };
    }

    private CfgCpuGraphResponse ReadCfgCpuGraph() {
        if (_cfgCpu == null) {
            throw new InvalidOperationException("CFG CPU is not enabled. Use --CfgCpu to enable Control Flow Graph CPU.");
        }

        ExecutionContextManager contextManager = _cfgCpu.ExecutionContextManager;
        Spice86.Core.Emulator.CPU.CfgCpu.Linker.ExecutionContext currentContext = contextManager.CurrentExecutionContext;

        // Count total entry points across all contexts
        int totalEntryPoints = contextManager.ExecutionContextEntryPoints
            .Sum(kvp => kvp.Value.Count);

        // Get entry point addresses
        string[] entryPointAddresses = contextManager.ExecutionContextEntryPoints
            .Select(kvp => kvp.Key.ToString())
            .ToArray();

        return new CfgCpuGraphResponse {
            CurrentContextDepth = currentContext.Depth,
            CurrentContextEntryPoint = currentContext.EntryPoint.ToString(),
            TotalEntryPoints = totalEntryPoints,
            EntryPointAddresses = entryPointAddresses,
            LastExecutedAddress = currentContext.LastExecuted?.Address.ToString() ?? "None"
        };
    }

    private static EmptyInputSchema CreateEmptyInputSchema() {
        return new EmptyInputSchema {
            Type = "object",
            Properties = new EmptySchemaProperties { },
            Required = Array.Empty<string>()
        };
    }

    private static MemoryReadInputSchema CreateMemoryReadInputSchema() {
        return new MemoryReadInputSchema {
            Type = "object",
            Properties = new MemoryReadInputProperties {
                Address = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "The starting memory address (linear address)"
                },
                Length = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "The number of bytes to read (max 4096)"
                }
            },
            Required = new string[] { "address", "length" }
        };
    }

    private static FunctionListInputSchema CreateFunctionListInputSchema() {
        return new FunctionListInputSchema {
            Type = "object",
            Properties = new FunctionListInputProperties {
                Limit = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "Maximum number of functions to return (default 100)"
                }
            },
            Required = Array.Empty<string>()
        };
    }

    private static string CreateSuccessResponse(JsonElement? id, object result) {
        JsonSerializerOptions options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        string serializedResult = JsonSerializer.Serialize(result, options);
        JsonNode? resultNode = JsonNode.Parse(serializedResult);

        JsonObject response = new JsonObject {
            ["jsonrpc"] = "2.0",
            ["result"] = resultNode
        };

        if (id.HasValue) {
            response["id"] = JsonValue.Create(id.Value);
        }

        return response.ToJsonString();
    }

    private static string CreateToolCallResponse(JsonElement? id, object toolResult) {
        JsonSerializerOptions options = new JsonSerializerOptions {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        string serializedResult = JsonSerializer.Serialize(toolResult, options);

        JsonObject content = new JsonObject {
            ["type"] = "text",
            ["text"] = serializedResult
        };

        JsonArray contentArray = new JsonArray {
            content
        };

        JsonObject resultObj = new JsonObject {
            ["content"] = contentArray
        };

        JsonObject response = new JsonObject {
            ["jsonrpc"] = "2.0",
            ["result"] = resultObj
        };

        if (id.HasValue) {
            response["id"] = JsonValue.Create(id.Value);
        }

        return response.ToJsonString();
    }

    private static string CreateErrorResponse(JsonElement? id, int code, string message) {
        JsonObject error = new JsonObject {
            ["code"] = code,
            ["message"] = message
        };

        JsonObject response = new JsonObject {
            ["jsonrpc"] = "2.0",
            ["error"] = error
        };

        if (id.HasValue) {
            response["id"] = JsonValue.Create(id.Value);
        }

        return response.ToJsonString();
    }
}