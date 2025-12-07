namespace Spice86.Core.Emulator.Mcp;

using ModelContextProtocol.Protocol;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.IOPorts;

/// <summary>
/// MCP server exposing emulator inspection and control tools.
/// </summary>
public sealed class McpServer : IMcpServer {
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly CfgCpu _cfgCpu;
    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly IVgaRenderer _vgaRenderer;
    private readonly IPauseHandler _pauseHandler;
    private readonly ILoggerService _loggerService;
    private readonly Tool[] _tools;
    private readonly object _requestLock = new object();

    public McpServer(IMemory memory, State state, FunctionCatalogue functionCatalogue, CfgCpu cfgCpu,
        IOPortDispatcher ioPortDispatcher, IVgaRenderer vgaRenderer, 
        IPauseHandler pauseHandler, ILoggerService loggerService) {
        _memory = memory;
        _state = state;
        _functionCatalogue = functionCatalogue;
        _cfgCpu = cfgCpu;
        _ioPortDispatcher = ioPortDispatcher;
        _vgaRenderer = vgaRenderer;
        _pauseHandler = pauseHandler;
        _loggerService = loggerService;
        _tools = CreateTools();
    }

    private Tool[] CreateTools() {
        List<Tool> tools = new List<Tool> {
            new Tool {
                Name = "read_cpu_registers",
                Description = "Read CPU registers",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            },
            new Tool {
                Name = "read_memory",
                Description = "Read memory range",
                InputSchema = ConvertToJsonElement(CreateMemoryReadInputSchema())
            },
            new Tool {
                Name = "list_functions",
                Description = "List functions",
                InputSchema = ConvertToJsonElement(CreateFunctionListInputSchema())
            },
            new Tool {
                Name = "read_io_port",
                Description = "Read from IO port",
                InputSchema = ConvertToJsonElement(CreateIoPortInputSchema())
            },
            new Tool {
                Name = "write_io_port",
                Description = "Write to IO port",
                InputSchema = ConvertToJsonElement(CreateIoPortWriteInputSchema())
            },
            new Tool {
                Name = "get_video_state",
                Description = "Get video card state",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            },
            new Tool {
                Name = "screenshot",
                Description = "Capture screenshot as base64-encoded BGRA32 raw data",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            },
            new Tool {
                Name = "pause_emulator",
                Description = "Pause emulator",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            },
            new Tool {
                Name = "resume_emulator",
                Description = "Resume emulator",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            }
        };

        tools.Add(new Tool {
            Name = "read_cfg_cpu_graph",
            Description = "Read CFG CPU statistics",
            InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
        });

        return tools.ToArray();
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
            object? result = null;
            string? errorMessage = null;
            int errorCode = 0;

            switch (toolName) {
                case "read_cpu_registers":
                    result = ReadCpuRegisters();
                    break;
                case "read_memory":
                    (result, errorMessage, errorCode) = TryReadMemory(argumentsElement);
                    break;
                case "list_functions":
                    (result, errorMessage) = TryListFunctions(argumentsElement);
                    if (errorMessage != null) {
                        errorCode = -32602;
                    }
                    break;
                case "read_cfg_cpu_graph":
                    (result, errorMessage) = TryReadCfgCpuGraph();
                    if (errorMessage != null) {
                        errorCode = -32603;
                    }
                    break;
                case "read_io_port":
                    (result, errorMessage, errorCode) = TryReadIoPort(argumentsElement);
                    break;
                case "write_io_port":
                    (result, errorMessage, errorCode) = TryWriteIoPort(argumentsElement);
                    break;
                case "get_video_state":
                    result = GetVideoState();
                    break;
                case "screenshot":
                    (result, errorMessage) = TryTakeScreenshot();
                    if (errorMessage != null) {
                        errorCode = -32603;
                    }
                    break;
                case "pause_emulator":
                    result = PauseEmulator();
                    break;
                case "resume_emulator":
                    result = ResumeEmulator();
                    break;
                default:
                    errorMessage = $"Unknown tool: {toolName}";
                    errorCode = -32601;
                    break;
            }

            if (errorMessage != null) {
                _loggerService.Error("Error executing tool {ToolName}: {Error}", toolName, errorMessage);
                return CreateErrorResponse(id, errorCode, errorMessage);
            }

            if (result == null) {
                _loggerService.Error("Tool {ToolName} returned null result", toolName);
                return CreateErrorResponse(id, -32603, "Tool execution returned null result");
            }

            return CreateToolCallResponse(id, result);
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

    private (MemoryReadResponse? result, string? error, int errorCode) TryReadMemory(JsonElement? arguments) {
        if (!arguments.HasValue) {
            return (null, "Missing arguments for read_memory", -32602);
        }

        JsonElement argsValue = arguments.Value;

        if (!argsValue.TryGetProperty("address", out JsonElement addressElement)) {
            return (null, "Missing address parameter", -32602);
        }

        if (!argsValue.TryGetProperty("length", out JsonElement lengthElement)) {
            return (null, "Missing length parameter", -32602);
        }

        uint address = addressElement.GetUInt32();
        int length = lengthElement.GetInt32();

        if (length <= 0 || length > 4096) {
            return (null, "Tool execution error: Length must be between 1 and 4096", -32603);
        }

        byte[] data = _memory.ReadRam((uint)length, address);

        return (new MemoryReadResponse {
            Address = address,
            Length = length,
            Data = Convert.ToHexString(data)
        }, null, 0);
    }

    private (FunctionListResponse? result, string? error) TryListFunctions(JsonElement? arguments) {
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

        return (new FunctionListResponse {
            Functions = functions,
            TotalCount = _functionCatalogue.FunctionInformations.Count
        }, null);
    }

    private (CfgCpuGraphResponse? result, string? error) TryReadCfgCpuGraph() {
        if (_cfgCpu == null) {
            return (null, "CFG CPU is not enabled. Use --CfgCpu to enable Control Flow Graph CPU.");
        }

        ExecutionContextManager contextManager = _cfgCpu.ExecutionContextManager;
        Spice86.Core.Emulator.CPU.CfgCpu.Linker.ExecutionContext currentContext = contextManager.CurrentExecutionContext;

        int totalEntryPoints = contextManager.ExecutionContextEntryPoints
            .Sum(kvp => kvp.Value.Count);

        string[] entryPointAddresses = contextManager.ExecutionContextEntryPoints
            .Select(kvp => kvp.Key.ToString())
            .ToArray();

        return (new CfgCpuGraphResponse {
            CurrentContextDepth = currentContext.Depth,
            CurrentContextEntryPoint = currentContext.EntryPoint.ToString(),
            TotalEntryPoints = totalEntryPoints,
            EntryPointAddresses = entryPointAddresses,
            LastExecutedAddress = currentContext.LastExecuted?.Address.ToString() ?? "None"
        }, null);
    }

    private (IoPortReadResponse? result, string? error, int errorCode) TryReadIoPort(JsonElement? arguments) {
        if (_pauseHandler.IsPaused) {
            return (null, "Emulator is paused. Resume to read IO ports.", -32603);
        }

        if (!arguments.HasValue) {
            return (null, "Missing port parameter", -32602);
        }

        if (!arguments.Value.TryGetProperty("port", out JsonElement portElement)) {
            return (null, "Missing port parameter", -32602);
        }

        int port = portElement.GetInt32();
        if (port < 0 || port > 65535) {
            return (null, "Port must be 0-65535", -32602);
        }

        byte value = _ioPortDispatcher.ReadByte((ushort)port);
        return (new IoPortReadResponse { Port = port, Value = value }, null, 0);
    }

    private (IoPortWriteResponse? result, string? error, int errorCode) TryWriteIoPort(JsonElement? arguments) {
        if (_pauseHandler.IsPaused) {
            return (null, "Emulator is paused. Resume to write IO ports.", -32603);
        }

        if (!arguments.HasValue) {
            return (null, "Missing parameters", -32602);
        }

        JsonElement argsValue = arguments.Value;
        if (!argsValue.TryGetProperty("port", out JsonElement portElement)) {
            return (null, "Missing port parameter", -32602);
        }

        if (!argsValue.TryGetProperty("value", out JsonElement valueElement)) {
            return (null, "Missing value parameter", -32602);
        }

        int port = portElement.GetInt32();
        int value = valueElement.GetInt32();

        if (port < 0 || port > 65535) {
            return (null, "Port must be 0-65535", -32602);
        }

        if (value < 0 || value > 255) {
            return (null, "Value must be 0-255", -32602);
        }

        _ioPortDispatcher.WriteByte((ushort)port, (byte)value);
        return (new IoPortWriteResponse { Port = port, Value = value, Success = true }, null, 0);
    }

    private VideoStateResponse GetVideoState() {
        return new VideoStateResponse {
            Width = _vgaRenderer.Width,
            Height = _vgaRenderer.Height,
            BufferSize = _vgaRenderer.BufferSize
        };
    }

    private (ScreenshotResponse? result, string? error) TryTakeScreenshot() {
        int width = _vgaRenderer.Width;
        int height = _vgaRenderer.Height;
        uint[] buffer = new uint[width * height];
        
        _vgaRenderer.Render(buffer);

        byte[] bytes = new byte[buffer.Length * 4];
        Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
        string base64 = Convert.ToBase64String(bytes);

        return (new ScreenshotResponse {
            Width = width,
            Height = height,
            Format = "bgra32",
            Data = base64
        }, null);
    }

    private EmulatorControlResponse PauseEmulator() {
        if (_pauseHandler.IsPaused) {
            return new EmulatorControlResponse { Success = true, Message = "Already paused" };
        }
        _pauseHandler.RequestPause("MCP server request");
        return new EmulatorControlResponse { Success = true, Message = "Paused" };
    }

    private EmulatorControlResponse ResumeEmulator() {
        if (!_pauseHandler.IsPaused) {
            return new EmulatorControlResponse { Success = true, Message = "Already running" };
        }
        _pauseHandler.Resume();
        return new EmulatorControlResponse { Success = true, Message = "Resumed" };
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

    private static IoPortInputSchema CreateIoPortInputSchema() {
        return new IoPortInputSchema {
            Type = "object",
            Properties = new IoPortInputProperties {
                Port = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "IO port number (0-65535)"
                }
            },
            Required = new string[] { "port" }
        };
    }

    private static IoPortWriteInputSchema CreateIoPortWriteInputSchema() {
        return new IoPortWriteInputSchema {
            Type = "object",
            Properties = new IoPortWriteInputProperties {
                Port = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "IO port number (0-65535)"
                },
                Value = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "Byte value to write (0-255)"
                }
            },
            Required = new string[] { "port", "value" }
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