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
using System.Runtime.CompilerServices;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Mcp.Request;
using Spice86.Core.Emulator.Mcp.Response;
using Spice86.Core.Emulator.Mcp.Schema;

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
    private readonly ExpandedMemoryManager? _emsManager;
    private readonly ExtendedMemoryManager? _xmsManager;
    private readonly ILoggerService _loggerService;
    private readonly Tool[] _tools;
    private readonly object _requestLock = new object();

    public McpServer(IMemory memory, State state, FunctionCatalogue functionCatalogue, CfgCpu cfgCpu,
        IOPortDispatcher ioPortDispatcher, IVgaRenderer vgaRenderer, 
        IPauseHandler pauseHandler, ExpandedMemoryManager? emsManager, ExtendedMemoryManager? xmsManager,
        ILoggerService loggerService) {
        _memory = memory;
        _state = state;
        _functionCatalogue = functionCatalogue;
        _cfgCpu = cfgCpu;
        _ioPortDispatcher = ioPortDispatcher;
        _vgaRenderer = vgaRenderer;
        _pauseHandler = pauseHandler;
        _emsManager = emsManager;
        _xmsManager = xmsManager;
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

        if (_emsManager != null) {
            tools.Add(new Tool {
                Name = "query_ems",
                Description = "Query EMS (Expanded Memory Manager) state",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            });
            tools.Add(new Tool {
                Name = "read_ems_memory",
                Description = "Read EMS (Expanded Memory) from a specific handle and page",
                InputSchema = ConvertToJsonElement(CreateEmsMemoryReadInputSchema())
            });
        }

        if (_xmsManager != null) {
            tools.Add(new Tool {
                Name = "query_xms",
                Description = "Query XMS (Extended Memory Manager) state",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            });
            tools.Add(new Tool {
                Name = "read_xms_memory",
                Description = "Read XMS (Extended Memory) from a specific handle",
                InputSchema = ConvertToJsonElement(CreateXmsMemoryReadInputSchema())
            });
        }

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

        lock (_requestLock) {
            try {
                McpToolResponse result = toolName switch {
                    "read_cpu_registers" => ReadCpuRegisters(),
                    "read_memory" => ReadMemory(argumentsElement),
                    "list_functions" => ListFunctions(argumentsElement),
                    "read_cfg_cpu_graph" => ReadCfgCpuGraph(),
                    "read_io_port" => ReadIoPort(argumentsElement),
                    "write_io_port" => WriteIoPort(argumentsElement),
                    "get_video_state" => GetVideoState(),
                    "screenshot" => TakeScreenshot(),
                    "pause_emulator" => PauseEmulator(),
                    "resume_emulator" => ResumeEmulator(),
                    "query_ems" => QueryEms(),
                    "query_xms" => QueryXms(),
                    "read_ems_memory" => ReadEmsMemory(argumentsElement),
                    "read_xms_memory" => ReadXmsMemory(argumentsElement),
                    _ => throw new McpMethodNotFoundException(toolName)
                };

                return CreateToolCallResponse(id, result);
            } catch (McpException ex) {
                _loggerService.Error("Error executing tool {ToolName}: {Error}", toolName, ex.Message);
                return CreateErrorResponse(id, ex.ErrorCode, ex.Message);
            } catch (ArgumentException ex) {
                _loggerService.Error("Invalid argument for tool {ToolName}: {Error}", toolName, ex.Message);
                return CreateErrorResponse(id, (int)JsonRpcErrorCode.InvalidParams, ex.Message);
            } catch (InvalidOperationException ex) {
                _loggerService.Error("Invalid operation for tool {ToolName}: {Error}", toolName, ex.Message);
                return CreateErrorResponse(id, (int)JsonRpcErrorCode.InternalError, ex.Message);
            } catch (Exception ex) when (!IsFatalException(ex)) {
                _loggerService.Error(ex, "Unexpected error executing tool {ToolName}", toolName);
                return CreateErrorResponse(id, -32603, $"Internal error: {ex.Message}");
            }
        }
    }

    private static bool IsFatalException(Exception ex) =>
        ex is OutOfMemoryException ||
        ex is StackOverflowException ||
        ex is ThreadAbortException ||
        ex is AccessViolationException;

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
        if (!arguments.HasValue) {
            throw new McpInvalidParametersException("Missing arguments for read_memory");
        }

        JsonElement argsValue = arguments.Value;

        if (!argsValue.TryGetProperty("address", out JsonElement addressElement)) {
            throw new McpInvalidParametersException("Missing address parameter");
        }

        if (!argsValue.TryGetProperty("length", out JsonElement lengthElement)) {
            throw new McpInvalidParametersException("Missing length parameter");
        }

        uint address = addressElement.GetUInt32();
        int length = lengthElement.GetInt32();

        if (length <= 0 || length > 4096) {
            throw new McpInternalErrorException("Tool execution error: Length must be between 1 and 4096");
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
        ExecutionContextManager contextManager = _cfgCpu.ExecutionContextManager;
        Spice86.Core.Emulator.CPU.CfgCpu.Linker.ExecutionContext currentContext = contextManager.CurrentExecutionContext;

        int totalEntryPoints = contextManager.ExecutionContextEntryPoints
            .Sum(kvp => kvp.Value.Count);

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

    private IoPortReadResponse ReadIoPort(JsonElement? arguments) {
        if (_pauseHandler.IsPaused) {
            throw new McpInternalErrorException("Emulator is paused. Resume to read IO ports.");
        }

        if (!arguments.HasValue) {
            throw new McpInvalidParametersException("Missing port parameter");
        }

        if (!arguments.Value.TryGetProperty("port", out JsonElement portElement)) {
            throw new McpInvalidParametersException("Missing port parameter");
        }

        int port = portElement.GetInt32();
        if (port < 0 || port > 65535) {
            throw new McpInvalidParametersException("Port must be 0-65535");
        }

        byte value = _ioPortDispatcher.ReadByte((ushort)port);
        return new IoPortReadResponse { Port = port, Value = value };
    }

    private IoPortWriteResponse WriteIoPort(JsonElement? arguments) {
        if (_pauseHandler.IsPaused) {
            throw new McpInternalErrorException("Emulator is paused. Resume to write IO ports.");
        }

        if (!arguments.HasValue) {
            throw new McpInvalidParametersException("Missing parameters");
        }

        JsonElement argsValue = arguments.Value;
        if (!argsValue.TryGetProperty("port", out JsonElement portElement)) {
            throw new McpInvalidParametersException("Missing port parameter");
        }

        if (!argsValue.TryGetProperty("value", out JsonElement valueElement)) {
            throw new McpInvalidParametersException("Missing value parameter");
        }

        int port = portElement.GetInt32();
        int value = valueElement.GetInt32();

        if (port < 0 || port > 65535) {
            throw new McpInvalidParametersException("Port must be 0-65535");
        }

        if (value < 0 || value > 255) {
            throw new McpInvalidParametersException("Value must be 0-255");
        }

        _ioPortDispatcher.WriteByte((ushort)port, (byte)value);
        return new IoPortWriteResponse { Port = port, Value = value, Success = true };
    }

    private VideoStateResponse GetVideoState() {
        return new VideoStateResponse {
            Width = _vgaRenderer.Width,
            Height = _vgaRenderer.Height,
            BufferSize = _vgaRenderer.BufferSize
        };
    }

    private ScreenshotResponse TakeScreenshot() {
        int width = _vgaRenderer.Width;
        int height = _vgaRenderer.Height;
        uint[] buffer = new uint[width * height];
        
        _vgaRenderer.Render(buffer);

        byte[] bytes = new byte[buffer.Length * 4];
        Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
        string base64 = Convert.ToBase64String(bytes);

        return new ScreenshotResponse {
            Width = width,
            Height = height,
            Format = "bgra32",
            Data = base64
        };
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

    private EmsStateResponse QueryEms() {
        if (_emsManager == null) {
            throw new InvalidOperationException("EMS is not enabled");
        }

        EmsHandleInfo[] handles = _emsManager.EmmHandles
            .Where(kvp => kvp.Key != ExpandedMemoryManager.EmmNullHandle && kvp.Value != null)
            .Select(kvp => new EmsHandleInfo {
                HandleId = kvp.Key,
                AllocatedPages = kvp.Value.LogicalPages.Count,
                Name = kvp.Value.Name
            })
            .ToArray();

        int allocatedPages = handles.Sum(h => h.AllocatedPages);

        ushort freePages = _emsManager.GetFreePageCount();

        return new EmsStateResponse {
            IsEnabled = true,
            PageFrameSegment = ExpandedMemoryManager.EmmPageFrameSegment,
            TotalPages = allocatedPages + freePages,
            AllocatedPages = allocatedPages,
            FreePages = freePages,
            PageSize = ExpandedMemoryManager.EmmPageSize,
            Handles = handles
        };
    }

    private XmsStateResponse QueryXms() {
        if (_xmsManager == null) {
            throw new InvalidOperationException("XMS is not enabled");
        }

        long freeMemoryKB = _xmsManager.TotalFreeMemory / 1024;
        int totalMemoryKB = ExtendedMemoryManager.XmsMemorySize;

        return new XmsStateResponse {
            IsEnabled = true,
            TotalMemoryKB = totalMemoryKB,
            FreeMemoryKB = (int)freeMemoryKB,
            LargestBlockKB = (int)freeMemoryKB,
            HmaAvailable = true,
            HmaAllocated = false,
            AllocatedBlocks = 0,
            Handles = Array.Empty<XmsHandleInfo>()
        };
    }

    private EmsMemoryReadResponse ReadEmsMemory(JsonElement? arguments) {
        if (_emsManager == null) {
            throw new InvalidOperationException("EMS is not enabled");
        }

        if (!arguments.HasValue) {
            throw new McpInvalidParametersException("Missing arguments for read_ems_memory");
        }

        JsonElement argsValue = arguments.Value;

        if (!argsValue.TryGetProperty("handle", out JsonElement handleElement)) {
            throw new McpInvalidParametersException("Missing handle parameter");
        }

        if (!argsValue.TryGetProperty("logicalPage", out JsonElement logicalPageElement)) {
            throw new McpInvalidParametersException("Missing logicalPage parameter");
        }

        if (!argsValue.TryGetProperty("offset", out JsonElement offsetElement)) {
            throw new McpInvalidParametersException("Missing offset parameter");
        }

        if (!argsValue.TryGetProperty("length", out JsonElement lengthElement)) {
            throw new McpInvalidParametersException("Missing length parameter");
        }

        int handle = handleElement.GetInt32();
        int logicalPage = logicalPageElement.GetInt32();
        int offset = offsetElement.GetInt32();
        int length = lengthElement.GetInt32();

        if (length <= 0 || length > 4096) {
            throw new McpInternalErrorException("Tool execution error: Length must be between 1 and 4096");
        }

        if (!_emsManager.EmmHandles.TryGetValue(handle, out InterruptHandlers.Dos.Ems.EmmHandle? emmHandle)) {
            throw new McpInternalErrorException($"Invalid EMS handle: {handle}");
        }

        if (logicalPage < 0 || logicalPage >= emmHandle.LogicalPages.Count) {
            throw new McpInternalErrorException($"Invalid logical page: {logicalPage}");
        }

        InterruptHandlers.Dos.Ems.EmmPage page = emmHandle.LogicalPages[logicalPage];

        if (offset < 0 || offset >= page.Size) {
            throw new McpInternalErrorException($"Invalid offset: {offset}");
        }

        if (offset + length > page.Size) {
            throw new McpInternalErrorException($"Read would exceed page boundary");
        }

        IList<byte> data = page.GetSlice(offset, length);
        byte[] dataArray = new byte[data.Count];
        data.CopyTo(dataArray, 0);

        return new EmsMemoryReadResponse {
            Handle = handle,
            LogicalPage = logicalPage,
            Offset = offset,
            Length = length,
            Data = Convert.ToHexString(dataArray)
        };
    }

    private XmsMemoryReadResponse ReadXmsMemory(JsonElement? arguments) {
        if (_xmsManager == null) {
            throw new McpInternalErrorException("XMS is not enabled");
        }

        if (!arguments.HasValue) {
            throw new McpInvalidParametersException("Missing arguments for read_xms_memory");
        }

        JsonElement argsValue = arguments.Value;

        if (!argsValue.TryGetProperty("handle", out JsonElement handleElement)) {
            throw new McpInvalidParametersException("Missing handle parameter");
        }

        if (!argsValue.TryGetProperty("offset", out JsonElement offsetElement)) {
            throw new McpInvalidParametersException("Missing offset parameter");
        }

        if (!argsValue.TryGetProperty("length", out JsonElement lengthElement)) {
            throw new McpInvalidParametersException("Missing length parameter");
        }

        int handle = handleElement.GetInt32();
        uint offset = offsetElement.GetUInt32();
        int length = lengthElement.GetInt32();

        if (length <= 0 || length > 4096) {
            throw new McpInternalErrorException("Tool execution error: Length must be between 1 and 4096");
        }

        if (!_xmsManager.TryGetBlock(handle, out InterruptHandlers.Dos.Xms.XmsBlock? xmsBlock)) {
            throw new McpInternalErrorException($"Invalid XMS handle: {handle}");
        }

        if (offset >= xmsBlock.Value.Length) {
            throw new McpInternalErrorException($"Invalid offset: {offset}");
        }

        if (offset + length > xmsBlock.Value.Length) {
            throw new McpInternalErrorException($"Read would exceed block boundary");
        }

        IList<byte> data = _xmsManager.XmsRam.GetSlice((int)(xmsBlock.Value.Offset + offset), length);
        byte[] dataArray = new byte[data.Count];
        data.CopyTo(dataArray, 0);

        return new XmsMemoryReadResponse {
            Handle = handle,
            Offset = offset,
            Length = length,
            Data = Convert.ToHexString(dataArray)
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

    private static EmsMemoryReadInputSchema CreateEmsMemoryReadInputSchema() {
        return new EmsMemoryReadInputSchema {
            Type = "object",
            Properties = new EmsMemoryReadInputProperties {
                Handle = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "EMS handle ID"
                },
                LogicalPage = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "Logical page number within the handle"
                },
                Offset = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "Offset within the logical page"
                },
                Length = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "Number of bytes to read (max 4096)"
                }
            },
            Required = new string[] { "handle", "logicalPage", "offset", "length" }
        };
    }

    private static XmsMemoryReadInputSchema CreateXmsMemoryReadInputSchema() {
        return new XmsMemoryReadInputSchema {
            Type = "object",
            Properties = new XmsMemoryReadInputProperties {
                Handle = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "XMS handle ID"
                },
                Offset = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "Offset within the XMS block"
                },
                Length = new JsonSchemaProperty {
                    Type = "integer",
                    Description = "Number of bytes to read (max 4096)"
                }
            },
            Required = new string[] { "handle", "offset", "length" }
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