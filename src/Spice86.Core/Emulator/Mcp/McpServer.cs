namespace Spice86.Core.Emulator.Mcp;

using ModelContextProtocol.Protocol;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
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
    public event EventHandler<string>? OnNotification;
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
    private readonly EmulatorBreakpointsManager _breakpointsManager;
    private readonly Tool[] _tools;
    private readonly object _requestLock = new object();
    private readonly Dictionary<string, BreakPoint> _mcpBreakpoints = new();
    private readonly Dictionary<string, bool> _toolEnabledState = new();
    private int _nextBreakpointId = 1;

    public McpServer(IMemory memory, State state, FunctionCatalogue functionCatalogue, CfgCpu cfgCpu,
        IOPortDispatcher ioPortDispatcher, IVgaRenderer vgaRenderer, 
        IPauseHandler pauseHandler, ExpandedMemoryManager? emsManager, ExtendedMemoryManager? xmsManager,
        EmulatorBreakpointsManager breakpointsManager, ILoggerService loggerService) {
        _memory = memory;
        _state = state;
        _functionCatalogue = functionCatalogue;
        _cfgCpu = cfgCpu;
        _ioPortDispatcher = ioPortDispatcher;
        _vgaRenderer = vgaRenderer;
        _pauseHandler = pauseHandler;
        _emsManager = emsManager;
        _xmsManager = xmsManager;
        _breakpointsManager = breakpointsManager;
        _loggerService = loggerService;
        _tools = CreateTools();
        foreach (Tool tool in _tools) {
            _toolEnabledState[tool.Name] = true;
        }
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
                Description = "Immediately stop the emulation. Use this to inspect state at an arbitrary point.",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            },
            new Tool {
                Name = "resume_emulator",
                Description = "Resume continuous execution of the emulator. Also known as 'go'.",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            },
            new Tool {
                Name = "go",
                Description = "Alias for resume_emulator. Resumes continuous execution.",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            },
            new Tool {
                Name = "step",
                Description = "Execute exactly one CPU instruction and then pause again. Useful for trace analysis.",
                InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
            },
            new Tool {
                Name = "read_stack",
                Description = "Read the top values of the stack (SS:SP). Returns addresses and 16-bit values.",
                InputSchema = ConvertToJsonElement(CreateReadStackInputSchema())
            }
        };

        tools.Add(new Tool {
            Name = "read_cfg_cpu_graph",
            Description = "Read CFG CPU statistics",
            InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
        });

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

        tools.Add(new Tool {
            Name = "add_breakpoint",
            Description = "Add a breakpoint (execution, memory, or IO)",
            InputSchema = ConvertToJsonElement(CreateAddBreakpointInputSchema())
        });

        tools.Add(new Tool {
            Name = "list_breakpoints",
            Description = "List all MCP-managed breakpoints",
            InputSchema = ConvertToJsonElement(CreateEmptyInputSchema())
        });

        tools.Add(new Tool {
            Name = "remove_breakpoint",
            Description = "Remove an MCP-managed breakpoint by ID",
            InputSchema = ConvertToJsonElement(CreateRemoveBreakpointInputSchema())
        });
        
        tools.Add(new Tool {
            Name = "clear_breakpoints",
            Description = "Remove ALL breakpoints currently managed by the MCP server.",
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
        return _tools.Where(t => _toolEnabledState.GetValueOrDefault(t.Name, true)).ToArray();
    }

    /// <inheritdoc />
    public Tool[] GetAllTools() {
        return _tools;
    }

    /// <inheritdoc />
    public void SetToolEnabled(string toolName, bool isEnabled) {
        if (_toolEnabledState.ContainsKey(toolName)) {
            _toolEnabledState[toolName] = isEnabled;
        }
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
                    "go" => ResumeEmulator(),
                    "step" => Step(),
                    "read_stack" => ReadStack(argumentsElement),
                    "query_ems" => QueryEms(),
                    "read_ems_memory" => ReadEmsMemory(argumentsElement),
                    "query_xms" => QueryXms(),
                    "read_xms_memory" => ReadXmsMemory(argumentsElement),
                    "add_breakpoint" => AddBreakpoint(argumentsElement),
                    "list_breakpoints" => ListBreakpoints(),
                    "remove_breakpoint" => RemoveBreakpoint(argumentsElement),
                    "clear_breakpoints" => ClearBreakpoints(),
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

    private EmulatorControlResponse Step() {
        if (!_pauseHandler.IsPaused) {
             _pauseHandler.RequestPause("Step requested while running");
        }

        string id = "step-" + _nextBreakpointId++;
        
        Action<BreakPoint> onReached = (bp) => {
            _pauseHandler.RequestPause($"Single step hit");
            SendBreakpointHitNotification(id, bp);
        };

        // One-shot unconditional breakpoint for the next execution
        UnconditionalBreakPoint stepBp = new(BreakPointType.CPU_EXECUTION_ADDRESS, onReached, true);
        _breakpointsManager.ToggleBreakPoint(stepBp, true);

        _pauseHandler.Resume();
        
        return new EmulatorControlResponse { Success = true, Message = "Stepping..." };
    }

    private StackResponse ReadStack(JsonElement? arguments) {
        int count = 10;
        if (arguments != null && arguments.Value.TryGetProperty("count", out JsonElement countElem)) {
            count = countElem.GetInt32();
        }

        if (count <= 0 || count > 100) {
            throw new McpInvalidParametersException("Count must be between 1 and 100");
        }

        ushort ss = _state.SS;
        ushort sp = _state.SP;
        uint baseAddress = (uint)(ss << 4) + sp;
        
        List<StackValue> values = new();
        for (int i = 0; i < count; i++) {
            uint addr = baseAddress + (uint)(i * 2);
            if (addr + 1 >= _memory.Length) break;
            
            ushort val = _memory.UInt16[addr];
            values.Add(new StackValue { Address = addr, Value = val });
        }

        return new StackResponse {
            Ss = ss,
            Sp = sp,
            Values = values
        };
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

    private BreakpointInfo AddBreakpoint(JsonElement? arguments) {
        if (!arguments.HasValue) {
            throw new McpInvalidParametersException("Missing arguments for add_breakpoint");
        }

        JsonElement args = arguments.Value;
        if (!args.TryGetProperty("address", out JsonElement addrElem)) {
            throw new McpInvalidParametersException("Missing address");
        }
        if (!args.TryGetProperty("type", out JsonElement typeElem)) {
            throw new McpInvalidParametersException("Missing type");
        }

        long address = addrElem.GetInt64();
        string typeStr = typeElem.GetString() ?? "";
        if (!Enum.TryParse(typeStr, out BreakPointType type)) {
            throw new McpInvalidParametersException($"Invalid breakpoint type: {typeStr}");
        }

        string? condition = args.TryGetProperty("condition", out JsonElement condElem) ? condElem.GetString() : null;

        string id = _nextBreakpointId++.ToString();
        
        Action<BreakPoint> onReached = (bp) => {
            _pauseHandler.RequestPause($"MCP Breakpoint {id} hit");
            SendBreakpointHitNotification(id, bp);
        };

        BreakPoint breakPoint;
        if (string.IsNullOrWhiteSpace(condition)) {
            breakPoint = new AddressBreakPoint(type, address, onReached, false);
        } else {
            try {
                BreakpointConditionCompiler compiler = new(_state, _memory as Memory ?? throw new InvalidOperationException("Memory must be of type Memory"));
                Func<long, bool> compiledCondition = compiler.Compile(condition);
                breakPoint = new AddressBreakPoint(type, address, onReached, false, compiledCondition, condition);
            } catch (Exception ex) {
                throw new McpInvalidParametersException($"Failed to compile condition: {ex.Message}");
            }
        }

        _breakpointsManager.ToggleBreakPoint(breakPoint, true);
        _mcpBreakpoints[id] = breakPoint;

        return new BreakpointInfo {
            Id = id,
            Address = address,
            Type = typeStr,
            Condition = condition,
            IsEnabled = true
        };
    }

    private BreakpointListResponse ListBreakpoints() {
        var list = _mcpBreakpoints.Select(kvp => new BreakpointInfo {
            Id = kvp.Key,
            Address = (kvp.Value as AddressBreakPoint)?.Address ?? 0,
            Type = kvp.Value.BreakPointType.ToString(),
            Condition = (kvp.Value as AddressBreakPoint)?.ConditionExpression,
            IsEnabled = kvp.Value.IsEnabled
        }).ToList();

        return new BreakpointListResponse {
            Breakpoints = list
        };
    }

    private EmulatorControlResponse RemoveBreakpoint(JsonElement? arguments) {
        if (!arguments.HasValue || !arguments.Value.TryGetProperty("id", out JsonElement idElem)) {
            throw new McpInvalidParametersException("Missing breakpoint id");
        }

        string id = idElem.GetString() ?? "";
        if (_mcpBreakpoints.Remove(id, out BreakPoint? bp)) {
            _breakpointsManager.ToggleBreakPoint(bp, false);
            return new EmulatorControlResponse { Success = true, Message = $"Breakpoint {id} removed." };
        }

        throw new McpInvalidParametersException($"Breakpoint {id} not found.");
    }

    private EmulatorControlResponse ClearBreakpoints() {
        int count = _mcpBreakpoints.Count;
        foreach (BreakPoint bp in _mcpBreakpoints.Values) {
            _breakpointsManager.ToggleBreakPoint(bp, false);
        }
        _mcpBreakpoints.Clear();
        return new EmulatorControlResponse { Success = true, Message = $"{count} breakpoints removed." };
    }

    private void SendBreakpointHitNotification(string id, BreakPoint bp) {
        var notification = new {
            method = "notifications/emulator/breakpoint_hit",
            @params = new {
                breakpointId = id,
                address = (bp as AddressBreakPoint)?.Address,
                registers = ReadCpuRegisters()
            }
        };

        string json = JsonSerializer.Serialize(notification, new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        OnNotification?.Invoke(this, json);
    }

    private object CreateAddBreakpointInputSchema() {
        return new {
            type = "object",
            properties = new {
                address = new { type = "integer", description = "Memory address, IO port, or cycle count" },
                type = new { 
                    type = "string", 
                    description = "Breakpoint type",
                    @enum = Enum.GetNames(typeof(BreakPointType))
                },
                condition = new { type = "string", description = "Optional conditional expression (e.g. 'ax == 0x300')" }
            },
            required = new[] { "address", "type" }
        };
    }

    private object CreateRemoveBreakpointInputSchema() {
        return new {
            type = "object",
            properties = new {
                id = new { type = "string", description = "The ID of the breakpoint to remove" }
            },
            required = new[] { "id" }
        };
    }

    private static object CreateReadStackInputSchema() {
        return new {
            type = "object",
            properties = new {
                count = new { type = "integer", description = "Number of words to read from the stack top (default 10)" }
            }
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