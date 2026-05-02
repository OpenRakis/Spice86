namespace Spice86.Mcp;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using SkiaSharp;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Mcp;
using Spice86.Core.Emulator.Mcp.Response;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

[McpServerToolType]
internal sealed class EmulatorMcpTools {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly string ScreenshotDirectory = Path.Join(Path.GetTempPath(), "spice86-mcp-screenshots");
    private static readonly TimeSpan StepCompletionTimeout = TimeSpan.FromSeconds(2);
    private readonly EmulatorMcpServices _services;

    public EmulatorMcpTools(EmulatorMcpServices services) => _services = services;

    private CallToolResult Success(object response) {
        JsonNode? responseNode = JsonSerializer.SerializeToNode(response, response.GetType(), SerializerOptions);
        JsonObject structuredContent;
        if (responseNode is JsonObject responseObject) {
            structuredContent = responseObject;
        } else {
            structuredContent = new JsonObject {
                ["value"] = responseNode
            };
        }

        return new CallToolResult {
            StructuredContent = structuredContent
        };
    }

    private CallToolResult Error(string message) {
        JsonObject structuredContent = new JsonObject {
            ["success"] = false,
            ["message"] = message
        };

        return new CallToolResult {
            IsError = true,
            StructuredContent = structuredContent,
            Content = [
                new TextContentBlock {
                    Text = message
                }
            ]
        };
    }

    private CallToolResult ExecuteTool(Func<object> action, [CallerMemberName] string methodName = "") {
        MethodInfo? method = typeof(EmulatorMcpTools).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        if (method == null) {
            return Error($"Unknown MCP tool method: {methodName}");
        }
        McpServerToolAttribute? toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
        if (toolAttr == null) {
            return Error($"Method '{methodName}' is missing [McpServerTool] attribute");
        }
        string toolName = string.IsNullOrWhiteSpace(toolAttr.Name) ? methodName : toolAttr.Name;
        bool autoPause = method.GetCustomAttribute<McpManualControlAttribute>() == null;

        bool shouldResumeOnCommandEnd = false;
        try {
            EnsureToolEnabled(toolName);
            if (autoPause && !_services.PauseHandler.IsPaused) {
                _services.PauseHandler.RequestPause($"to process MCP tool '{toolName}'");
                if (!WaitUntilPaused(StepCompletionTimeout)) {
                    throw new InvalidOperationException($"Timed out waiting to pause before running tool '{toolName}'");
                }
                shouldResumeOnCommandEnd = true;
            }

            return Success(action());
        } catch (ArgumentException ex) {
            return Error(ex.Message);
        } catch (InvalidOperationException ex) {
            return Error(ex.Message);
        } catch (KeyNotFoundException ex) {
            return Error(ex.Message);
        } catch (FormatException ex) {
            return Error(ex.Message);
        } catch (OverflowException ex) {
            return Error(ex.Message);
        } finally {
            if (shouldResumeOnCommandEnd) {
                _services.PauseHandler.Resume();
            }
        }
    }

    private void EnsureToolEnabled(string toolName) {
        if (!_services.IsToolEnabled(toolName)) {
            throw new InvalidOperationException($"Tool '{toolName}' is disabled.");
        }
    }

    private static SegmentedAddress ToSegmentedAddress(uint physicalAddress) {
        ushort segment = MemoryUtils.ToSegment(physicalAddress);
        ushort offset = (ushort)(physicalAddress & 0xF);
        return new SegmentedAddress(segment, offset);
    }

    private static int ToPageSegment(int physicalPage) {
        int paragraphsPerPage = ExpandedMemoryManager.EmmPageSize / 16;
        return ExpandedMemoryManager.EmmPageFrameSegment + physicalPage * paragraphsPerPage;
    }

    private (bool IsMapped, int? HandleId, int? LogicalPage) ResolveEmsMappingForPhysicalPage(ushort physicalPage) {
        ExpandedMemoryManager? emsManager = _services.EmsManager;
        if (emsManager == null) {
            return (false, null, null);
        }

        EmmPage mappedPage = emsManager.EmmPageFrame[physicalPage].PhysicalPage;
        foreach (KeyValuePair<int, EmmHandle> handleEntry in emsManager.EmmHandles) {
            for (int logicalPage = 0; logicalPage < handleEntry.Value.LogicalPages.Count; logicalPage++) {
                EmmPage candidatePage = handleEntry.Value.LogicalPages[logicalPage];
                if (ReferenceEquals(candidatePage, mappedPage)) {
                    return (true, handleEntry.Key, logicalPage);
                }
            }
        }

        return (false, null, null);
    }

    [McpServerTool(Name = "mcp_about", UseStructuredContent = true), Description("Describe the MCP server purpose, capability scope, version, and discovery guidance for AI clients")]
    public CallToolResult McpAbout() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                return new McpAboutResponse {
                    Name = "Spice86 MCP Server",
                    Version = "2.0.0",
                    Purpose = "Reverse-engineering and emulator automation for DOS workloads in Spice86.",
                    Stateless = true,
                    McpEndpoint = "/mcp",
                    HealthEndpoint = "/health",
                    CapabilityScopes = [
                        "cpu_state_and_registers",
                        "memory_read_write_search_disassembly",
                        "io_ports",
                        "breakpoints",
                        "execution_control_pause_resume_step_step_over",
                        "function_listing_and_cfg_graph",
                        "video_and_screenshot",
                        "sound_devices_sb_opl_midi_speaker",
                        "dos_and_bios",
                        "ems_and_xms"
                    ],
                    ExtensionModel = "Spice86-based projects can register extra MCP tool assemblies and injectable services.",
                    ExtensionPoints = [
                        "IMcpToolSupplier.GetMcpToolAssemblies",
                        "IMcpToolSupplier.GetMcpServices",
                        "McpHttpHost.Start(additionalToolAssemblies, additionalServices)"
                    ],
                    Discovery = [
                        "initialize",
                        "tools/list",
                        "mcp_about"
                    ],
                    ToolCount = _services.GetAllToolNames().Count
                };
            }
        });
    }

    [McpServerTool(Name = "read_cpu_state", UseStructuredContent = true), Description("Read full CPU state (also known as 'read registers' or 'read_cpu_registers'). Returns general-purpose registers (EAX-EBP), segment registers (CS, DS, ES, FS, GS, SS), instruction pointer (IP), flags, and cycle count. CS:IP gives the address of the next instruction to execute. No parameters required.")]
    public CallToolResult ReadCpuState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                return CpuStateSnapshot.FromState(_services.State);
            }
        });
    }

    [McpServerTool(Name = "read_memory", UseStructuredContent = true), Description("Read a memory range. Parameters: segment (ushort), offset (ushort), length (int, 1-4096). Address is two separate integers, not a combined string. Returns the address, length, and hex-encoded byte string.")]
    public CallToolResult ReadMemory(ushort segment, ushort offset, int length) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (length <= 0 || length > 4096) {
                    throw new InvalidOperationException("Length must be between 1 and 4096");
                }

                uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
                byte[] data = _services.Memory.ReadRam((uint)length, address);
                return new {
                    Address = new SegmentedAddress(segment, offset),
                    Length = length,
                    Data = Convert.ToHexString(data)
                };
            }
        });
    }

    [McpServerTool(Name = "read_disassembly", UseStructuredContent = true), Description("Disassemble x86 real-mode instructions. Parameters: segment (ushort), offset (ushort), instructionCount (int, 1-500). Address is two separate integers, not a combined string. Returns array of instructions with address, raw bytes, and assembly text.")]
    public CallToolResult ReadDisassembly(ushort segment, ushort offset, int instructionCount) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (instructionCount <= 0 || instructionCount > 500) {
                    throw new InvalidOperationException("instructionCount must be between 1 and 500");
                }

                InstructionParser parser = new(_services.Memory, _services.State);
                AstInstructionRenderer renderer = new(AsmRenderingConfig.CreateSpice86Style());
                List<DisassemblyLine> lines = new();
                SegmentedAddress current = new(segment, offset);
                uint memoryLength = (uint)_services.Memory.Length;
                bool truncated = false;

                for (int i = 0; i < instructionCount; i++) {
                    uint physicalAddress = MemoryUtils.ToPhysicalAddress(current.Segment, current.Offset);
                    if (physicalAddress >= memoryLength) {
                        truncated = true;
                        break;
                    }

                    CfgInstruction instruction = parser.ParseInstructionAt(current);
                    uint endAddress = physicalAddress + instruction.Length;
                    if (endAddress > memoryLength) {
                        truncated = true;
                        break;
                    }

                    InstructionNode ast = instruction.DisplayAst;
                    string assembly = ast.Accept(renderer);
                    byte[] bytes = _services.Memory.ReadRam(instruction.Length, physicalAddress);
                    lines.Add(new DisassemblyLine {
                        Address = current,
                        Bytes = Convert.ToHexString(bytes),
                        Assembly = assembly
                    });
                    current = instruction.NextInMemoryAddress;
                }

                return new { Instructions = lines, Truncated = truncated };
            }
        });
    }

    [McpServerTool(Name = "write_memory", UseStructuredContent = true), Description("Write hex-encoded bytes to memory. Parameters: segment (ushort), offset (ushort), data (hex string like 'B80200'). Address is two separate integers, not a combined string. Max 4096 bytes.")]
    public CallToolResult WriteMemory(ushort segment, ushort offset, [StringSyntax("Hexadecimal")] string data) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                byte[] bytesToWrite = ParseHexData(data);
                if (bytesToWrite.Length > 4096) {
                    throw new InvalidOperationException("Data length must be between 1 and 4096 bytes");
                }

                uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
                ValidateMemoryWriteRange(address, bytesToWrite.Length);
                _services.Memory.WriteRam(bytesToWrite, address);
                return new {
                    Address = new SegmentedAddress(segment, offset),
                    Length = bytesToWrite.Length,
                    Success = true
                };
            }
        });
    }

    [McpServerTool(Name = "search_memory", UseStructuredContent = true), Description("Search conventional RAM for a hex-encoded byte pattern starting at segment:offset. Returns matching segmented addresses. Useful for finding strings, code sequences, or data patterns in memory.")]
    public CallToolResult SearchMemory([StringSyntax("Hexadecimal")] string pattern, ushort startSegment, ushort startOffset, int length, int limit) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                byte[] needle = ParseHexPattern(pattern);
                ValidateLimit(limit);

                uint startAddress = MemoryUtils.ToPhysicalAddress(startSegment, startOffset);
                int searchLength = ComputeSearchLength(startAddress, length, _services.Memory.Length);
                if (searchLength <= 0) {
                    return EmptyMemorySearchResponse(needle, startSegment, startOffset);
                }

                (uint[] matches, bool truncated) = SearchRamMatches(startAddress, searchLength, needle, limit);
                return new {
                    Pattern = Convert.ToHexString(needle),
                    StartAddress = new SegmentedAddress(startSegment, startOffset),
                    Length = searchLength,
                    Matches = matches.Select(ToSegmentedAddress).ToArray(),
                    Truncated = truncated
                };
            }
        });
    }

    private static byte[] ParseHexPattern([StringSyntax("Hexadecimal")] string pattern) {
        if (string.IsNullOrWhiteSpace(pattern)) {
            throw new ArgumentException("Pattern must not be empty", nameof(pattern));
        }

        string normalized = new string(pattern.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized[2..];
        }
        if ((normalized.Length & 1) != 0) {
            throw new ArgumentException("Pattern hex string length must be even", nameof(pattern));
        }

        try {
            byte[] needle = Convert.FromHexString(normalized);
            if (needle.Length == 0) {
                throw new ArgumentException("Pattern must contain at least one byte", nameof(pattern));
            }
            return needle;
        } catch (FormatException ex) {
            throw new ArgumentException("Pattern must be a valid hex string", nameof(pattern), ex);
        }
    }

    private static byte[] ParseHexData([StringSyntax("Hexadecimal")] string data) {
        if (string.IsNullOrWhiteSpace(data)) {
            throw new ArgumentException("Data must not be empty", nameof(data));
        }

        string normalized = new string(data.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized[2..];
        }
        if ((normalized.Length & 1) != 0) {
            throw new ArgumentException("Data hex string length must be even", nameof(data));
        }

        try {
            byte[] bytes = Convert.FromHexString(normalized);
            if (bytes.Length == 0) {
                throw new ArgumentException("Data must contain at least one byte", nameof(data));
            }
            return bytes;
        } catch (FormatException ex) {
            throw new ArgumentException("Data must be a valid hex string", nameof(data), ex);
        }
    }

    private void ValidateMemoryWriteRange(uint address, int length) {
        if (address >= _services.Memory.Length) {
            throw new ArgumentOutOfRangeException(nameof(address), "Start address is outside memory range");
        }

        int maxLength = _services.Memory.Length - (int)address;
        if (length > maxLength) {
            throw new InvalidOperationException("Data exceeds memory bounds");
        }
    }

    private static void ValidateLimit(int limit) {
        if (limit <= 0 || limit > 10_000) {
            throw new ArgumentException("Limit must be between 1 and 10000", nameof(limit));
        }
    }

    private static int ComputeSearchLength(uint startAddress, int requestedLength, int memoryLength) {
        if (startAddress >= memoryLength) {
            throw new ArgumentOutOfRangeException(nameof(startAddress), "Start address is outside memory range");
        }

        int maxLength = memoryLength - (int)startAddress;
        if (requestedLength <= 0) {
            return maxLength;
        }
        return Math.Min(requestedLength, maxLength);
    }

    private static object EmptyMemorySearchResponse(byte[] needle, ushort startSegment, ushort startOffset) {
        return new {
            Pattern = Convert.ToHexString(needle),
            StartAddress = new SegmentedAddress(startSegment, startOffset),
            Length = 0,
            Matches = Array.Empty<SegmentedAddress>(),
            Truncated = false
        };
    }

    private (uint[] Matches, bool Truncated) SearchRamMatches(uint startAddress, int searchLength, byte[] needle, int limit) {
        List<uint> matches = new();
        uint current = startAddress;
        int remaining = searchLength;

        while (remaining > 0 && matches.Count < limit) {
            uint? found = _services.Memory.SearchValue(current, remaining, needle);
            if (!found.HasValue) {
                break;
            }

            uint next = found.Value + 1;
            matches.Add(found.Value);
            if (next <= found.Value || found.Value < current) {
                break;
            }
            remaining = Math.Max(0, (int)((long)startAddress + searchLength - next));
            current = next;
        }

        bool truncated = matches.Count >= limit && remaining > 0;
        return (matches.ToArray(), truncated);
    }

    [McpServerTool(Name = "list_functions", UseStructuredContent = true), Description("List discovered functions ordered by call count. Returns address, name, call count, and override status for each function. Use limit to cap the result size.")]
    public CallToolResult ListFunctions(int? limit) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                int effectiveLimit = limit ?? 100;
                if (effectiveLimit <= 0) {
                    effectiveLimit = 100;
                }

                FunctionInfo[] functions = _services.FunctionCatalogue.FunctionInformations.Values
                .OrderByDescending(f => f.CalledCount)
                .Take(effectiveLimit)
                .Select(f => new FunctionInfo {
                    Address = f.Address,
                    Name = f.Name,
                    CalledCount = f.CalledCount,
                    HasOverride = f.HasOverride
                })
                .ToArray();

                return new {
                    Functions = functions,
                    TotalCount = _services.FunctionCatalogue.FunctionInformations.Count
                };
            }
        });
    }

    [McpServerTool(Name = "read_cfg_cpu_graph", UseStructuredContent = true), Description("Read the Control Flow Graph built by the CPU. Returns execution context depth, entry point addresses, last executed address, and graph nodes with successor/predecessor edges for each instruction. Use nodeLimit to cap BFS traversal; omit or pass null for the full graph.")]
    public CallToolResult ReadCfgCpuGraph(int? nodeLimit) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                ExecutionContextManager contextManager = _services.CfgCpu.ExecutionContextManager;
                ExecutionContext currentContext = contextManager.CurrentExecutionContext;

                SegmentedAddress[] entryPointAddresses = contextManager.ExecutionContextEntryPoints
                    .Select(kvp => kvp.Key)
                    .ToArray();

                ISet<ICfgNode> entryNodes = new HashSet<ICfgNode>();
                foreach (ISet<CfgInstruction> instructions in contextManager.ExecutionContextEntryPoints.Values) {
                    foreach (ICfgNode node in instructions) {
                        entryNodes.Add(node);
                    }
                }
                if (currentContext.LastExecuted != null) {
                    entryNodes.Add(currentContext.LastExecuted);
                }

                int? effectiveLimit = nodeLimit.HasValue && nodeLimit.Value > 0 ? nodeLimit.Value : null;
                (CfgNodeInfo[] nodes, bool truncated) = CollectGraphNodes(entryNodes, effectiveLimit);

                return new CfgCpuGraphResponse {
                    CurrentContextDepth = currentContext.Depth,
                    CurrentContextEntryPoint = currentContext.EntryPoint,
                    TotalEntryPoints = entryPointAddresses.Length,
                    EntryPointAddresses = entryPointAddresses,
                    LastExecutedAddress = currentContext.LastExecuted?.Address,
                    Nodes = nodes,
                    Truncated = truncated
                };
            }
        });
    }

    private static (CfgNodeInfo[] Nodes, bool Truncated) CollectGraphNodes(ISet<ICfgNode> seeds, int? limit) {
        HashSet<int> visited = new();
        Queue<ICfgNode> queue = new();
        List<CfgNodeInfo> result = new();

        foreach (ICfgNode seed in seeds) {
            if (visited.Add(seed.Id)) {
                queue.Enqueue(seed);
            }
        }

        while (queue.Count > 0) {
            if (limit.HasValue && result.Count >= limit.Value) {
                return (result.ToArray(), true);
            }

            ICfgNode current = queue.Dequeue();
            result.Add(new CfgNodeInfo {
                Id = current.Id,
                Address = current.Address,
                SuccessorIds = current.Successors.Select(s => s.Id).ToArray(),
                PredecessorIds = current.Predecessors.Select(p => p.Id).ToArray(),
                IsLive = current.IsLive
            });

            foreach (ICfgNode successor in current.Successors) {
                if (visited.Add(successor.Id)) {
                    queue.Enqueue(successor);
                }
            }
            foreach (ICfgNode predecessor in current.Predecessors) {
                if (visited.Add(predecessor.Id)) {
                    queue.Enqueue(predecessor);
                }
            }
        }

        return (result.ToArray(), false);
    }

    [McpServerTool(Name = "read_io_port", UseStructuredContent = true), Description("Read a byte from an x86 IO port (0-65535). Returns the port number and byte value.")]
    public CallToolResult ReadIoPort(int port) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (port < 0 || port > 65535) {
                    throw new ArgumentException("Port must be 0-65535");
                }
                byte value = _services.IoPortDispatcher.ReadByte((ushort)port);
                return new { Port = port, Value = value };
            }
        });
    }

    [McpServerTool(Name = "write_io_port", UseStructuredContent = true), Description("Write a byte to an x86 IO port (0-65535). Triggers the emulated device handler for that port.")]
    public CallToolResult WriteIoPort(int port, int value) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (port < 0 || port > 65535) {
                    throw new ArgumentException("Port must be 0-65535");
                }
                if (value < 0 || value > 255) {
                    throw new ArgumentException("Value must be 0-255");
                }
                _services.IoPortDispatcher.WriteByte((ushort)port, (byte)value);
                return new { Port = port, Value = value, Success = true };
            }
        });
    }

    [McpServerTool(Name = "send_keyboard_key", UseStructuredContent = true), Description("Send a single keyboard key event. Parameters: key (string, PcKeyboardKey enum name like 'Enter' or 'Escape'), isPressed (bool, true=key down, false=key up). For a full keypress, call twice: once with isPressed=true, then with isPressed=false. Use PcKeyboardKey enum names: Escape, Enter, A-Z, Up, Down, Left, Right, F1-F12, Space, Tab, etc.")]
    public CallToolResult SendKeyboardKey(string key, bool isPressed) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!Enum.TryParse(key, true, out PcKeyboardKey parsedKey)) {
                    throw new ArgumentException($"Invalid PcKeyboardKey: '{key}'");
                }

                PhysicalKey physicalKey = KeyboardScancodeConverter.ConvertToPhysicalKey(parsedKey);
                if (physicalKey == PhysicalKey.None) {
                    throw new ArgumentException($"PcKeyboardKey '{key}' has no corresponding physical key mapping");
                }

                KeyboardEventArgs args = new(physicalKey, isPressed);
                string action = isPressed ? "down" : "up";

                if (_services.PauseHandler.IsPaused) {
                    InputEventHub? pausedHub = _services.InputEventHub;
                    if (pausedHub == null) {
                        throw new InvalidOperationException("InputEventHub is not wired");
                    }
                    pausedHub.PostKeyboardEvent(args);
                    return new EmulatorControlResponse {
                        Success = true,
                        Message = $"Keyboard event enqueued while paused: {parsedKey} {action}"
                    };
                }

                InputEventHub? hub = _services.InputEventHub;
                if (hub == null) {
                    throw new InvalidOperationException("InputEventHub is not wired");
                }

                hub.PostKeyboardEvent(args);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Keyboard event sent: {parsedKey} {action}"
                };
            }
        });
    }

    [McpServerTool(Name = "send_mouse_packet", UseStructuredContent = true), Description("Send a raw PS/2 mouse packet (3 hex bytes) through the controller AUX port. Format: SSXXYY where SS=status, XX=X delta, YY=Y delta. Example: 080000 (no movement, no button).")]
    public CallToolResult SendMousePacket([StringSyntax("Hexadecimal")] string packetData) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Intel8042Controller? controller = _services.Intel8042Controller;
                if (controller == null) {
                    throw new InvalidOperationException("PS/2 controller is not available");
                }

                byte[] bytes = ParseHexData(packetData);
                if (bytes.Length > 8) {
                    throw new ArgumentException("Mouse packet must be 1-8 bytes", nameof(packetData));
                }

                foreach (byte packetByte in bytes) {
                    controller.AddAuxByte(packetByte);
                }

                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Mouse packet sent ({bytes.Length} byte(s))"
                };
            }
        });
    }

    [McpServerTool(Name = "send_mouse_move", UseStructuredContent = true), Description("Move the mouse to a normalized position on screen. x and y are in the range [0.0, 1.0] relative to the emulated display: (0,0) is top-left, (1,1) is bottom-right.")]
    public CallToolResult SendMouseMove(double x, double y) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                InputEventHub? hub = _services.InputEventHub;
                if (hub == null) {
                    throw new InvalidOperationException("InputEventHub is not wired");
                }

                double clampedX = Math.Clamp(x, 0.0, 1.0);
                double clampedY = Math.Clamp(y, 0.0, 1.0);
                hub.PostMouseMoveEvent(new MouseMoveEventArgs(clampedX, clampedY));
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Mouse moved to ({clampedX:F3}, {clampedY:F3})"
                };
            }
        });
    }

    [McpServerTool(Name = "send_mouse_button", UseStructuredContent = true), Description("Send a mouse button press or release event. button is one of: Left, Right, Middle. isPressed=true for press, false for release.")]
    public CallToolResult SendMouseButton(string button, bool isPressed) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!Enum.TryParse(button, true, out MouseButton parsedButton) || parsedButton == MouseButton.None) {
                    throw new ArgumentException($"Invalid MouseButton: '{button}'. Use Left, Right, or Middle.");
                }

                InputEventHub? hub = _services.InputEventHub;
                if (hub == null) {
                    throw new InvalidOperationException("InputEventHub is not wired");
                }

                hub.PostMouseButtonEvent(new MouseButtonEventArgs(parsedButton, isPressed));
                string action = isPressed ? "pressed" : "released";
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Mouse button {parsedButton} {action}"
                };
            }
        });
    }

    [McpServerTool(Name = "read_video_state", UseStructuredContent = true), Description("Read basic VGA renderer state: current width, height, and framebuffer size in pixels.")]
    public CallToolResult GetVideoState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                return new {
                    Width = _services.VgaRenderer.Width,
                    Height = _services.VgaRenderer.Height,
                    BufferSize = _services.VgaRenderer.BufferSize
                };
            }
        });
    }

    [McpServerTool(Name = "read_sound_blaster_state", UseStructuredContent = true), Description("Read Sound Blaster configuration: SB type, base address, IRQ, DMA channels, BLASTER environment string, speaker enable state, DSP sample rate, and test register.")]
    public CallToolResult QuerySoundBlasterState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                return new SoundBlasterStateResponse {
                    SbType = soundBlaster.SbTypeProperty,
                    BaseAddress = soundBlaster.BaseAddress,
                    Irq = soundBlaster.IRQ,
                    LowDma = soundBlaster.LowDma,
                    HighDma = soundBlaster.HighDma,
                    BlasterString = soundBlaster.BlasterString,
                    SpeakerEnabled = soundBlaster.IsSpeakerEnabled,
                    DspFrequencyHz = soundBlaster.DspFrequencyHz,
                    DspTestRegister = soundBlaster.DspTestRegister
                };
            }
        });
    }

    [McpServerTool(Name = "sound_blaster_set_speaker", UseStructuredContent = true), Description("Enable or disable the Sound Blaster DSP speaker output.")]
    public CallToolResult SoundBlasterSetSpeaker(bool enabled) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                ushort dspWritePort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.DspWriteData);
                byte command = enabled ? (byte)SoundBlaster.DspCommand.EnableSpeaker : (byte)SoundBlaster.DspCommand.DisableSpeaker;
                soundBlaster.WriteByte(dspWritePort, command);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = enabled ? "Sound Blaster speaker enabled" : "Sound Blaster speaker disabled"
                };
            }
        });
    }

    [McpServerTool(Name = "read_sound_blaster_dsp_version", UseStructuredContent = true), Description("Read the Sound Blaster DSP version by issuing the GetVersion DSP command. Returns major and minor version numbers.")]
    public CallToolResult QuerySoundBlasterDspVersion() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                return ReadSoundBlasterDspVersion(soundBlaster);
            }
        });
    }

    [McpServerTool(Name = "read_sound_blaster_mixer_state", UseStructuredContent = true), Description("Read Sound Blaster mixer volume levels (master, DAC, FM, CD, line-in, microphone) and stereo/filter enable state.")]
    public CallToolResult QuerySoundBlasterMixerState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                byte outputControl = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.OutputStereoSelect);
                return new SoundBlasterMixerStateResponse {
                    MasterLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.MasterVolumeLeft),
                    MasterRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.MasterVolumeRight),
                    DacLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.DacVolumeLeftOrMasterEss),
                    DacRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.DacVolumeRight),
                    FmLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.FmVolumeLeft),
                    FmRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.FmVolumeRight),
                    CdLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.CdAudioVolumeLeftOrFmEss),
                    CdRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.CdAudioVolumeRight),
                    LineInLeft = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.LineInVolumeLeftOrCdEss),
                    LineInRight = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.LineInVolumeRight),
                    Microphone = ReadSoundBlasterMixerRegister(soundBlaster, SoundBlaster.MixerRegister.MicVolume),
                    StereoOutputEnabled = (outputControl & 0x02) != 0,
                    LowPassFilterEnabled = (outputControl & 0x20) == 0
                };
            }
        });
    }

    [McpServerTool(Name = "sound_blaster_write_mixer_register", UseStructuredContent = true), Description("Write a Sound Blaster mixer register (0x00-0xFF) with a byte value. Controls volume levels, stereo, and filter settings.")]
    public CallToolResult SoundBlasterWriteMixerRegister(int register, int value) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                SoundBlaster soundBlaster = GetSoundBlaster();
                if (register < 0 || register > 0xFF) {
                    throw new ArgumentException("Register must be between 0x00 and 0xFF");
                }
                if (value < 0 || value > 0xFF) {
                    throw new ArgumentException("Value must be between 0x00 and 0xFF");
                }

                WriteSoundBlasterMixerRegister(soundBlaster, (byte)register, (byte)value);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Wrote Sound Blaster mixer register 0x{register:X2} = 0x{value:X2}"
                };
            }
        });
    }

    [McpServerTool(Name = "read_opl_state", UseStructuredContent = true), Description("Read OPL/AdLib FM synthesis state: OPL mode (OPL2/OPL3/DualOPL2), AdLib Gold enable, and mixer channel name, sample rate, and enable state.")]
    public CallToolResult QueryOplState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Opl3Fm opl3Fm = GetOpl3Fm();
                SoundChannel mixerChannel = opl3Fm.MixerChannel;
                return new OplStateResponse {
                    Mode = opl3Fm.Mode,
                    AdlibGoldEnabled = opl3Fm.IsAdlibGoldEnabled,
                    MixerChannelName = mixerChannel.Name,
                    MixerChannelSampleRate = mixerChannel.SampleRate,
                    MixerChannelEnabled = mixerChannel.IsEnabled
                };
            }
        });
    }

    [McpServerTool(Name = "read_pc_speaker_state", UseStructuredContent = true), Description("Read PC speaker state: port 0x61 control value, timer-2 gate enable, speaker output enable, timer-2 output level, and mixer channel info.")]
    public CallToolResult QueryPcSpeakerState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                PcSpeaker pcSpeaker = GetPcSpeaker();
                SoundChannel mixerChannel = pcSpeaker.Channel;
                return new PcSpeakerStateResponse {
                    ControlPort = 0x61,
                    ControlValue = pcSpeaker.ControlPortValue,
                    Timer2GateEnabled = pcSpeaker.IsTimer2GateEnabled,
                    SpeakerOutputEnabled = pcSpeaker.IsSpeakerOutputEnabled,
                    Timer2OutputHigh = pcSpeaker.IsTimer2OutputHigh,
                    MixerChannelName = mixerChannel.Name,
                    MixerChannelSampleRate = mixerChannel.SampleRate,
                    MixerChannelEnabled = mixerChannel.IsEnabled
                };
            }
        });
    }

    [McpServerTool(Name = "pc_speaker_set_control", UseStructuredContent = true), Description("Set PC speaker timer-2 gate and speaker output bits via port 0x61 semantics.")]
    public CallToolResult PcSpeakerSetControl(bool timer2GateEnabled, bool speakerOutputEnabled) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                PcSpeaker pcSpeaker = GetPcSpeaker();
                PpiPortB portState = new() {
                    Data = pcSpeaker.ControlPortValue
                };
                portState.Timer2Gating = timer2GateEnabled;
                portState.SpeakerOutput = speakerOutputEnabled;
                pcSpeaker.WriteByte(0x61, portState.Data);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"PC speaker control updated: gate={(timer2GateEnabled ? 1 : 0)}, output={(speakerOutputEnabled ? 1 : 0)}"
                };
            }
        });
    }

    [McpServerTool(Name = "read_midi_state", UseStructuredContent = true), Description("Read MPU-401 MIDI state: device kind (GeneralMidi/MT32), MT-32 ROM path, operational state, status flags, and data/status port addresses.")]
    public CallToolResult QueryMidiState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Midi midi = GetMidi();
                GeneralMidiStatus status = midi.Status;
                return new MidiStateResponse {
                    DeviceKind = midi.UseMT32 ? "MT32" : "GeneralMidi",
                    UseMt32 = midi.UseMT32,
                    Mt32RomsPath = midi.Mt32RomsPath,
                    State = midi.State,
                    Status = status,
                    DataPort = Midi.DataPort,
                    StatusPort = Midi.StatusPort
                };
            }
        });
    }

    [McpServerTool(Name = "midi_reset", UseStructuredContent = true), Description("Reset the MPU-401 MIDI interface to intelligent mode and enqueue an ACK byte.")]
    public CallToolResult MidiReset() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Midi midi = GetMidi();
                midi.WriteByte(Midi.StatusPort, Midi.ResetCommand);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = "MIDI interface reset"
                };
            }
        });
    }

    [McpServerTool(Name = "midi_enter_uart_mode", UseStructuredContent = true), Description("Switch the MPU-401 MIDI interface to UART (pass-through) mode.")]
    public CallToolResult MidiEnterUartMode() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Midi midi = GetMidi();
                midi.WriteByte(Midi.StatusPort, Midi.EnterUartModeCommand);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = "MIDI interface entered UART mode"
                };
            }
        });
    }

    [McpServerTool(Name = "midi_send_bytes", UseStructuredContent = true), Description("Send raw MIDI bytes (hex string, 1-1024 bytes) through the MPU-401 data port.")]
    public CallToolResult MidiSendBytes([StringSyntax("Hexadecimal")] string data) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Midi midi = GetMidi();
                byte[] bytes = ParseHexData(data);
                if (bytes.Length == 0 || bytes.Length > 1024) {
                    throw new ArgumentException("MIDI data length must be between 1 and 1024 bytes", nameof(data));
                }

                foreach (byte value in bytes) {
                    midi.WriteByte(Midi.DataPort, value);
                }

                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Sent {bytes.Length} MIDI byte(s)"
                };
            }
        });
    }

    [McpServerTool(Name = "opl_write_register", UseStructuredContent = true), Description("Write an OPL register (0x000-0x1FF) with a byte value. Controls FM synthesis operators, channels, and global settings.")]
    public CallToolResult OplWriteRegister(int register, int value) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Opl3Fm opl3Fm = GetOpl3Fm();
                if (register < 0 || register > 0x1FF) {
                    throw new ArgumentException("Register must be between 0x000 and 0x1FF");
                }
                if (value < 0 || value > 0xFF) {
                    throw new ArgumentException("Value must be between 0x00 and 0xFF");
                }
                if (opl3Fm.Mode == OplMode.Opl2 && register > 0xFF) {
                    throw new ArgumentException("OPL2 mode only supports registers between 0x00 and 0xFF");
                }

                ushort addressPort = register <= 0xFF ? (ushort)0x388 : (ushort)0x38A;
                ushort dataPort = (ushort)(addressPort + 1);
                opl3Fm.WriteByte(addressPort, (byte)(register & 0xFF));
                opl3Fm.WriteByte(dataPort, (byte)value);

                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Wrote OPL register 0x{register:X3} = 0x{value:X2}"
                };
            }
        });
    }

    [McpServerTool(Name = "read_video_state_detailed", UseStructuredContent = true), Description("Read detailed VGA state: BIOS video mode, VGA mode object, cursor position, screen columns/rows, and renderer dimensions. More complete than read_video_state.")]
    public CallToolResult QueryVideoStateDetailed() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                BiosDataArea biosDataArea = GetBiosDataArea();
                VgaMode currentMode = vgaFunctionality.GetCurrentMode();
                CursorPosition cursorPosition = vgaFunctionality.GetCursorPosition(biosDataArea.CurrentVideoPage);
                return new VideoDetailedStateResponse {
                    BiosVideoMode = biosDataArea.VideoMode,
                    Mode = currentMode,
                    Cursor = cursorPosition,
                    ScreenColumns = biosDataArea.ScreenColumns,
                    ScreenRows = biosDataArea.ScreenRows,
                    RendererWidth = _services.VgaRenderer.Width,
                    RendererHeight = _services.VgaRenderer.Height,
                    BufferSize = _services.VgaRenderer.BufferSize
                };
            }
        });
    }

    [McpServerTool(Name = "video_set_mode", UseStructuredContent = true), Description("Set a VGA/EGA/CGA video mode by mode ID. Optionally clears video memory.")]
    public CallToolResult VideoSetMode(int modeId, bool clearVideoMemory) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                ModeFlags modeFlags = clearVideoMemory ? 0 : ModeFlags.NoClearMem;
                vgaFunctionality.VgaSetMode(modeId, modeFlags);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Video mode set to 0x{modeId:X2}"
                };
            }
        });
    }

    [McpServerTool(Name = "video_write_text", UseStructuredContent = true), Description("Write a text string at the current cursor position in the active text page.")]
    public CallToolResult VideoWriteText(string text) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                vgaFunctionality.WriteString(text);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Wrote {text.Length} character(s) to the video device"
                };
            }
        });
    }

    [McpServerTool(Name = "read_video_cursor", UseStructuredContent = true), Description("Read the text-mode cursor position (x, y, page) and the character plus attribute stored at that location.")]
    public CallToolResult QueryVideoCursor(int page) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                ValidateVideoPage(page);

                CursorPosition cursorPosition = vgaFunctionality.GetCursorPosition((byte)page);
                CharacterPlusAttribute character = vgaFunctionality.ReadChar(cursorPosition);
                return BuildVideoCursorResponse(cursorPosition, character);
            }
        });
    }

    [McpServerTool(Name = "video_set_cursor_position", UseStructuredContent = true), Description("Set the cursor position (x, y) on a given text page.")]
    public CallToolResult VideoSetCursorPosition(int page, int x, int y) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                BiosDataArea biosDataArea = GetBiosDataArea();
                ValidateTextCoordinates(page, x, y, biosDataArea);

                vgaFunctionality.SetCursorPosition(new CursorPosition(x, y, page));
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Cursor moved to ({x}, {y}) on page {page}"
                };
            }
        });
    }

    [McpServerTool(Name = "video_read_character", UseStructuredContent = true), Description("Read the character and color attribute stored at a specific text-mode position (page, x, y).")]
    public CallToolResult VideoReadCharacter(int page, int x, int y) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                BiosDataArea biosDataArea = GetBiosDataArea();
                ValidateTextCoordinates(page, x, y, biosDataArea);

                CursorPosition cursorPosition = new(x, y, page);
                CharacterPlusAttribute character = vgaFunctionality.ReadChar(cursorPosition);
                return new VideoCharacterResponse {
                    Position = cursorPosition,
                    Character = character
                };
            }
        });
    }

    [McpServerTool(Name = "video_set_active_page", UseStructuredContent = true), Description("Set the active VGA text page (0-7).")]
    public CallToolResult VideoSetActivePage(int page) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                ValidateVideoPage(page);

                int pageStartAddress = vgaFunctionality.SetActivePage((byte)page);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Active video page set to {page} (start 0x{pageStartAddress:X})"
                };
            }
        });
    }

    [McpServerTool(Name = "read_video_palette", UseStructuredContent = true), Description("Read EGA/VGA palette registers, overscan border color, pixel mask, and color page state.")]
    public CallToolResult QueryVideoPalette() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                byte[] registers = vgaFunctionality.GetAllPaletteRegisters();
                return new {
                    Registers = registers.Select(static x => (int)x).ToArray(),
                    OverscanBorderColor = vgaFunctionality.GetOverscanBorderColor(),
                    PixelMask = vgaFunctionality.ReadPixelMask(),
                    ColorPageState = vgaFunctionality.ReadColorPageState()
                };
            }
        });
    }

    [McpServerTool(Name = "video_write_pixel", UseStructuredContent = true), Description("Write a pixel at (x, y) with a color index in the current graphics mode.")]
    public CallToolResult VideoWritePixel(int x, int y, int color) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                VgaMode currentMode = vgaFunctionality.GetCurrentMode();
                ValidatePixelCoordinates(x, y, currentMode);
                if (color < 0 || color > 0xFF) {
                    throw new ArgumentException("Color must be between 0x00 and 0xFF");
                }

                vgaFunctionality.WritePixel((byte)color, (ushort)x, (ushort)y);
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"Pixel ({x}, {y}) set to 0x{color:X2}"
                };
            }
        });
    }

    [McpServerTool(Name = "video_read_pixel", UseStructuredContent = true), Description("Read the color index of a pixel at (x, y) in the current graphics mode.")]
    public CallToolResult VideoReadPixel(int x, int y) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                IVgaFunctionality vgaFunctionality = GetVgaFunctionality();
                VgaMode currentMode = vgaFunctionality.GetCurrentMode();
                ValidatePixelCoordinates(x, y, currentMode);

                byte color = vgaFunctionality.ReadPixel((ushort)x, (ushort)y);
                return new {
                    X = x,
                    Y = y,
                    Color = (int)color
                };
            }
        });
    }

    [McpServerTool(Name = "read_bios_data_area", UseStructuredContent = true), Description("Read key BIOS Data Area fields: conventional memory size, equipment flags, video mode, screen dimensions, active page, character height, CRT base address, timer counter, and last unexpected IRQ.")]
    public CallToolResult QueryBiosDataArea() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                BiosDataArea biosDataArea = GetBiosDataArea();
                return new BiosDataAreaResponse {
                    ConventionalMemorySizeKb = biosDataArea.ConventionalMemorySizeKb,
                    EquipmentListFlags = biosDataArea.EquipmentListFlags,
                    VideoMode = biosDataArea.VideoMode,
                    ScreenColumns = biosDataArea.ScreenColumns,
                    ScreenRows = biosDataArea.ScreenRows,
                    CurrentVideoPage = biosDataArea.CurrentVideoPage,
                    CharacterHeight = biosDataArea.CharacterHeight,
                    CrtControllerBaseAddress = biosDataArea.CrtControllerBaseAddress,
                    TimerCounter = biosDataArea.TimerCounter,
                    LastUnexpectedIrq = biosDataArea.LastUnexpectedIrq
                };
            }
        });
    }

    [McpServerTool(Name = "read_interrupt_vector", UseStructuredContent = true), Description("Read an interrupt vector (0x00-0xFF) from the IVT. Returns the segmented address (segment:offset) the vector points to.")]
    public CallToolResult QueryInterruptVector(int vectorNumber) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (vectorNumber < 0 || vectorNumber > 0xFF) {
                    throw new ArgumentException("Interrupt vector must be between 0x00 and 0xFF");
                }

                InterruptVectorTable interruptVectorTable = GetInterruptVectorTable();
                SegmentedAddress address = interruptVectorTable[vectorNumber];
                return new {
                    VectorNumber = vectorNumber,
                    Address = new SegmentedAddress(address.Segment, address.Offset)
                };
            }
        });
    }

    [McpServerTool(Name = "read_dos_state", UseStructuredContent = true), Description("Read DOS kernel state: current drive, drive index, mounted drives with host directories, PSP segment, device count, and EMS/XMS availability.")]
    public CallToolResult QueryDosState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                List<DosDriveResponse> drives = dos.DosDriveManager.Values
                    .Select(static drive => new DosDriveResponse {
                        Drive = drive.DosVolume,
                        CurrentDosDirectory = drive.CurrentDosDirectory,
                        MountedHostDirectory = drive.MountedHostDirectory
                    })
                    .ToList();

                return new DosStateResponse {
                    CurrentDrive = dos.DosDriveManager.CurrentDrive.DosVolume,
                    CurrentDriveIndex = dos.DosDriveManager.CurrentDriveIndex,
                    PotentialDriveLetters = dos.DosDriveManager.NumberOfPotentiallyValidDriveLetters,
                    CurrentProgramSegmentPrefix = dos.DosSwappableDataArea.CurrentProgramSegmentPrefix,
                    DeviceCount = dos.Devices.Count,
                    HasEms = dos.Ems != null,
                    HasXms = dos.Xms != null,
                    Drives = drives
                };
            }
        });
    }

    [McpServerTool(Name = "read_dos_current_directory", UseStructuredContent = true), Description("Read the current DOS directory for the current drive or for an explicit drive letter.")]
    public CallToolResult QueryDosCurrentDirectory(string driveLetter) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                byte driveNumber = ResolveDosDriveNumber(dos, driveLetter, out string resolvedDrive);
                DosFileOperationResult result = dos.FileManager.GetCurrentDir(driveNumber, out string currentDirectory);
                EnsureDosFileOperationSucceeded(result, $"Could not query current directory for {resolvedDrive}");

                return new {
                    Drive = resolvedDrive,
                    CurrentDirectory = currentDirectory
                };
            }
        });
    }

    [McpServerTool(Name = "dos_set_current_directory", UseStructuredContent = true), Description("Set the DOS current directory using a DOS path (e.g. C:\\GAMES\\DUNE).")]
    public CallToolResult DosSetCurrentDirectory(string path) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                DosFileOperationResult result = dos.FileManager.SetCurrentDir(path);
                EnsureDosFileOperationSucceeded(result, $"Could not set current directory to '{path}'");

                return new {
                    Drive = dos.DosDriveManager.CurrentDrive.DosVolume,
                    CurrentDirectory = dos.DosDriveManager.CurrentDrive.CurrentDosDirectory
                };
            }
        });
    }

    [McpServerTool(Name = "read_dos_program_state", UseStructuredContent = true), Description("Read current DOS process state: PSP segment, parent PSP, environment segment, max open files, allocated paragraphs, and command tail length.")]
    public CallToolResult QueryDosProgramState() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                ushort currentPspSegment = dos.DosSwappableDataArea.CurrentProgramSegmentPrefix;
                DosProgramSegmentPrefix currentPsp = new(_services.Memory, MemoryUtils.ToPhysicalAddress(currentPspSegment, 0));

                return new DosProgramStateResponse {
                    CurrentProgramSegmentPrefix = currentPspSegment,
                    ParentProgramSegmentPrefix = currentPsp.ParentProgramSegmentPrefix,
                    EnvironmentTableSegment = currentPsp.EnvironmentTableSegment,
                    MaximumOpenFiles = currentPsp.MaximumOpenFiles,
                    CurrentSizeParagraphs = currentPsp.CurrentSize,
                    CommandTailLength = currentPsp.DosCommandTail.Length
                };
            }
        });
    }

    [McpServerTool(Name = "dos_set_default_drive", UseStructuredContent = true), Description("Set the DOS default drive by letter (A-Z). Does not issue INT 21h.")]
    public CallToolResult DosSetDefaultDrive(string driveLetter) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                Dos dos = GetDos();
                if (string.IsNullOrWhiteSpace(driveLetter) || driveLetter.Length != 1) {
                    throw new ArgumentException("Drive letter must be a single character between A and Z");
                }

                char normalizedDriveLetter = char.ToUpperInvariant(driveLetter[0]);
                if (!dos.DosDriveManager.TryGetValue(normalizedDriveLetter, out VirtualDrive? mountedDrive) || mountedDrive == null) {
                    throw new InvalidOperationException($"Drive '{normalizedDriveLetter}' is not mounted");
                }

                dos.DosDriveManager.CurrentDrive = mountedDrive;
                dos.DosSwappableDataArea.CurrentDrive = dos.DosDriveManager.CurrentDriveIndex;
                return new EmulatorControlResponse {
                    Success = true,
                    Message = $"DOS default drive set to {mountedDrive.DosVolume}"
                };
            }
        });
    }

    [McpServerTool(Name = "screenshot", UseStructuredContent = true), Description("Capture a screenshot as PNG. No parameters. Returns base64 image data inline plus metadata (width, height, file path). MCP clients that support images will display it directly.")]
    public CallToolResult TakeScreenshot() {
        return ExecuteScreenshot();
    }

    private CallToolResult ExecuteScreenshot() {
        bool shouldResume = false;
        try {
            EnsureToolEnabled("screenshot");
            if (!_services.PauseHandler.IsPaused) {
                _services.PauseHandler.RequestPause("to process MCP tool 'screenshot'");
                if (!WaitUntilPaused(StepCompletionTimeout)) {
                    return Error("Timed out waiting to pause before taking screenshot");
                }
                shouldResume = true;
            }

            lock (_services.ToolsLock) {
                int width = _services.VgaRenderer.Width;
                int height = _services.VgaRenderer.Height;
                uint[] buffer = new uint[width * height];

                _services.VgaRenderer.CopyLastFrame(buffer);

                bool hasVisiblePixelData = false;
                for (int i = 0; i < buffer.Length; i++) {
                    if (buffer[i] != 0) {
                        hasVisiblePixelData = true;
                        break;
                    }
                }

                if (!hasVisiblePixelData) {
                    return Error("No video frame is available for screenshot capture.");
                }

                byte[] bytes = new byte[buffer.Length * 4];
                Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);

                Directory.CreateDirectory(ScreenshotDirectory);
                string fileName = $"spice86-mcp-screenshot-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png";
                string filePath = Path.Join(ScreenshotDirectory, fileName);

                SKImageInfo imageInfo = new(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                using SKBitmap bitmap = new(imageInfo);
                IntPtr pointer = bitmap.GetPixels();
                Marshal.Copy(bytes, 0, pointer, bytes.Length);

                using SKImage image = SKImage.FromBitmap(bitmap);
                using SKData pngData = image.Encode(SKEncodedImageFormat.Png, 100);
                if (pngData == null) {
                    return Error("Failed to encode screenshot as PNG.");
                }

                byte[] pngBytes = pngData.ToArray();

                using (FileStream fileStream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.Read)) {
                    fileStream.Write(pngBytes, 0, pngBytes.Length);
                }

                FileInfo fileInfo = new(filePath);
                Uri fileUri = new(filePath);

                ScreenshotResponse metadata = new() {
                    Width = width,
                    Height = height,
                    Format = "png",
                    MimeType = "image/png",
                    FilePath = filePath,
                    FileUri = fileUri.AbsoluteUri,
                    FileSizeBytes = fileInfo.Length
                };

                JsonNode? metadataNode = JsonSerializer.SerializeToNode(metadata, typeof(ScreenshotResponse), SerializerOptions);

                return new CallToolResult {
                    Content = [
                        new ImageContentBlock {
                            Data = Convert.ToBase64String(pngBytes),
                            MimeType = "image/png"
                        }
                    ],
                    StructuredContent = metadataNode as JsonObject
                };
            }
        } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException) {
            return Error(ex.Message);
        } finally {
            if (shouldResume) {
                _services.PauseHandler.Resume();
            }
        }
    }

    [McpManualControl]
    [McpServerTool(Name = "pause_emulator", UseStructuredContent = true), Description("Pause the emulator immediately. Use this before inspecting CPU state, memory, or any device registers.")]
    public CallToolResult PauseEmulator() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.PauseHandler.IsPaused) {
                    return new EmulatorControlResponse { Success = true, Message = "Already paused" };
                }
                _services.PauseHandler.RequestPause("MCP server request");
                return new EmulatorControlResponse { Success = true, Message = "Paused" };
            }
        });
    }

    [McpManualControl]
    [McpServerTool(Name = "resume_emulator", UseStructuredContent = true), Description("Resume continuous execution of the emulator.")]
    public CallToolResult ResumeEmulator() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!_services.PauseHandler.IsPaused) {
                    return new EmulatorControlResponse { Success = true, Message = "Already running" };
                }
                _services.PauseHandler.Resume();
                return new EmulatorControlResponse { Success = true, Message = "Resumed" };
            }
        });
    }

    [McpManualControl]
    [McpServerTool(Name = "go", UseStructuredContent = true), Description("Alias for resume_emulator.")]
    public CallToolResult Go() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!_services.PauseHandler.IsPaused) {
                    return new EmulatorControlResponse { Success = true, Message = "Already running" };
                }
                _services.PauseHandler.Resume();
                return new EmulatorControlResponse { Success = true, Message = "Resumed" };
            }
        });
    }

    [McpManualControl]
    [McpServerTool(Name = "step", UseStructuredContent = true), Description("Execute exactly one CPU instruction and then pause. Returns the updated CPU state (registers, IP, flags, cycles) after the step.")]
    public CallToolResult Step() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!_services.PauseHandler.IsPaused) {
                    _services.PauseHandler.RequestPause("Step requested while running");
                    if (!WaitUntilPaused(StepCompletionTimeout)) {
                        throw new InvalidOperationException("Could not pause emulator before stepping.");
                    }
                }

                _services.CfgCpu.ExecuteNext();

                return new StepResponse {
                    Success = true,
                    Message = "Step completed",
                    CpuState = CpuStateSnapshot.FromState(_services.State)
                };
            }
        });
    }

    private SoundBlaster GetSoundBlaster() {
        return _services.SoundBlaster ?? throw new InvalidOperationException("Sound Blaster is not available");
    }

    private Opl3Fm GetOpl3Fm() {
        return _services.Opl3Fm ?? throw new InvalidOperationException("OPL is not available");
    }

    private PcSpeaker GetPcSpeaker() {
        return _services.PcSpeaker ?? throw new InvalidOperationException("PC speaker is not available");
    }

    private Midi GetMidi() {
        return _services.Midi ?? throw new InvalidOperationException("MIDI device is not available");
    }

    private IVgaFunctionality GetVgaFunctionality() {
        return _services.VgaFunctionality ?? throw new InvalidOperationException("VGA functionality is not available");
    }

    private BiosDataArea GetBiosDataArea() {
        return _services.BiosDataArea ?? throw new InvalidOperationException("BIOS data area is not available");
    }

    private InterruptVectorTable GetInterruptVectorTable() {
        return _services.InterruptVectorTable ?? throw new InvalidOperationException("Interrupt vector table is not available");
    }

    private Dos GetDos() {
        return _services.Dos ?? throw new InvalidOperationException("DOS is not available");
    }

    private static object ReadSoundBlasterDspVersion(SoundBlaster soundBlaster) {
        if (soundBlaster.SbTypeProperty == SbType.None) {
            return new {
                MajorVersion = 0,
                MinorVersion = 0
            };
        }

        ushort dspWritePort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.DspWriteData);
        ushort dspReadPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.DspReadData);
        soundBlaster.WriteByte(dspWritePort, (byte)SoundBlaster.DspCommand.GetDspVersion);
        byte majorVersion = soundBlaster.ReadByte(dspReadPort);
        byte minorVersion = soundBlaster.ReadByte(dspReadPort);
        return new {
            MajorVersion = (int)majorVersion,
            MinorVersion = (int)minorVersion
        };
    }

    private static byte ReadSoundBlasterMixerRegister(SoundBlaster soundBlaster, SoundBlaster.MixerRegister register) {
        return ReadSoundBlasterMixerRegister(soundBlaster, (byte)register);
    }

    private static byte ReadSoundBlasterMixerRegister(SoundBlaster soundBlaster, byte register) {
        ushort mixerIndexPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.MixerIndex);
        ushort mixerDataPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.MixerData);
        soundBlaster.WriteByte(mixerIndexPort, register);
        return soundBlaster.ReadByte(mixerDataPort);
    }

    private static void WriteSoundBlasterMixerRegister(SoundBlaster soundBlaster, byte register, byte value) {
        ushort mixerIndexPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.MixerIndex);
        ushort mixerDataPort = (ushort)(soundBlaster.BaseAddress + (byte)SoundBlaster.SoundBlasterPortOffset.MixerData);
        soundBlaster.WriteByte(mixerIndexPort, register);
        soundBlaster.WriteByte(mixerDataPort, value);
    }

    private static VideoCharacterResponse BuildVideoCursorResponse(CursorPosition cursorPosition, CharacterPlusAttribute character) {
        return new VideoCharacterResponse {
            Position = cursorPosition,
            Character = character
        };
    }

    private static void ValidateVideoPage(int page) {
        if (page < 0 || page > 7) {
            throw new ArgumentException("Video page must be between 0 and 7");
        }
    }

    private static void ValidateTextCoordinates(int page, int x, int y, BiosDataArea biosDataArea) {
        ValidateVideoPage(page);
        if (x < 0 || x >= biosDataArea.ScreenColumns) {
            throw new ArgumentException($"X must be between 0 and {biosDataArea.ScreenColumns - 1}");
        }

        int maximumRow = biosDataArea.ScreenRows;
        if (y < 0 || y > maximumRow) {
            throw new ArgumentException($"Y must be between 0 and {maximumRow}");
        }
    }

    private static void ValidatePixelCoordinates(int x, int y, VgaMode currentMode) {
        if (x < 0 || x >= currentMode.Width) {
            throw new ArgumentException($"X must be between 0 and {currentMode.Width - 1}");
        }
        if (y < 0 || y >= currentMode.Height) {
            throw new ArgumentException($"Y must be between 0 and {currentMode.Height - 1}");
        }
    }

    private static byte ResolveDosDriveNumber(Dos dos, string driveLetter, out string resolvedDrive) {
        if (string.IsNullOrWhiteSpace(driveLetter)) {
            resolvedDrive = dos.DosDriveManager.CurrentDrive.DosVolume;
            return 0;
        }

        if (driveLetter.Length != 1) {
            throw new ArgumentException("Drive letter must be empty or a single character between A and Z");
        }

        char normalizedDriveLetter = char.ToUpperInvariant(driveLetter[0]);
        if (!DosDriveManager.DriveLetters.TryGetValue(normalizedDriveLetter, out byte driveIndex)) {
            throw new ArgumentException($"Drive letter '{normalizedDriveLetter}' is invalid");
        }
        if (!dos.DosDriveManager.TryGetValue(normalizedDriveLetter, out VirtualDrive? virtualDrive) || virtualDrive == null) {
            throw new InvalidOperationException($"Drive '{normalizedDriveLetter}' is not mounted");
        }

        resolvedDrive = virtualDrive.DosVolume;
        return (byte)(driveIndex + 1);
    }

    private static void EnsureDosFileOperationSucceeded(DosFileOperationResult result, string context) {
        if (!result.IsError) {
            return;
        }

        DosErrorCode errorCode = (DosErrorCode)(result.Value ?? 0);
        throw new InvalidOperationException($"{context}: {errorCode}");
    }

    private bool WaitUntilPaused(TimeSpan timeout) {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout) {
            if (_services.PauseHandler.IsPaused) {
                return true;
            }
            System.Threading.Thread.Sleep(1);
        }

        return _services.PauseHandler.IsPaused;
    }

    [McpManualControl]
    [McpServerTool(Name = "step_over", UseStructuredContent = true), Description("Step over one instruction. For CALL or INT, runs until the return address; otherwise single-steps. Requires nextAddress (physical = CS*16+IP + instruction length) and isCallOrInterrupt.")]
    public CallToolResult StepOver(uint nextAddress, bool isCallOrInterrupt) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (!_services.PauseHandler.IsPaused) {
                    _services.PauseHandler.RequestPause("Step over requested while running");
                }
                if (isCallOrInterrupt) {
                    DebuggerStepHelper.SetupStepOverBreakpoint(_services.BreakpointsManager, nextAddress,
                        () => _services.PauseHandler.RequestPause("Step over hit"));
                } else {
                    DebuggerStepHelper.SetupStepIntoBreakpoint(_services.BreakpointsManager, _services.State,
                        () => _services.PauseHandler.RequestPause("Step over hit"));
                }
                _services.PauseHandler.Resume();
                return new EmulatorControlResponse { Success = true, Message = "Step over initiated" };
            }
        });
    }

    [McpServerTool(Name = "read_stack", UseStructuredContent = true), Description("Read the top N 16-bit values from the stack at SS:SP. Returns each address and word value.")]
    public CallToolResult ReadStack(int count) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (count <= 0 || count > 100) {
                    throw new ArgumentException("Count must be between 1 and 100");
                }

                ushort ss = _services.State.SS;
                ushort sp = _services.State.SP;
                uint baseAddress = (uint)(ss << 4) + sp;

                List<object> values = new();
                for (int i = 0; i < count; i++) {
                    uint addr = baseAddress + (uint)(i * 2);
                    if (addr + 1 >= _services.Memory.Length) break;
                    ushort val = _services.Memory.UInt16[addr];
                    values.Add(new { Address = addr, Value = val });
                }

                return new { Ss = ss, Sp = sp, Values = values };
            }
        });
    }

    [McpServerTool(Name = "read_ems_state", UseStructuredContent = true), Description("Read EMS (Expanded Memory) state: page frame segment, total/allocated/free pages, active handles with names and page counts, and physical-to-logical page mappings.")]
    public CallToolResult QueryEms() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.EmsManager == null) {
                    throw new InvalidOperationException("EMS is not enabled");
                }

                object[] handles = _services.EmsManager.EmmHandles
                    .Where(kvp => kvp.Key != ExpandedMemoryManager.EmmNullHandle && kvp.Value != null)
                    .Select(kvp => (object)new {
                        HandleId = kvp.Key,
                        AllocatedPages = kvp.Value.LogicalPages.Count,
                        Name = kvp.Value.Name
                    })
                    .ToArray();

                int allocatedPages = _services.EmsManager.EmmHandles
                    .Where(kvp => kvp.Key != ExpandedMemoryManager.EmmNullHandle && kvp.Value != null)
                    .Sum(kvp => kvp.Value.LogicalPages.Count);
                ushort freePages = _services.EmsManager.GetFreePageCount();

                return new {
                    IsEnabled = true,
                    PageFrameSegment = (int)ExpandedMemoryManager.EmmPageFrameSegment,
                    TotalPages = allocatedPages + (int)freePages,
                    AllocatedPages = allocatedPages,
                    FreePages = (int)freePages,
                    PageSize = (int)ExpandedMemoryManager.EmmPageSize,
                    Handles = handles,
                    PageMappings = Enumerable.Range(0, ExpandedMemoryManager.EmmMaxPhysicalPages)
                        .Select(physicalPage => {
                            (bool isMapped, int? handleId, int? logicalPage) = ResolveEmsMappingForPhysicalPage((ushort)physicalPage);
                            return (object)new {
                                PhysicalPage = physicalPage,
                                Segment = ToPageSegment(physicalPage),
                                IsMapped = isMapped,
                                HandleId = handleId,
                                LogicalPage = logicalPage
                            };
                        })
                        .ToArray()
                };
            }
        });
    }

    [McpServerTool(Name = "read_ems_page_frame", UseStructuredContent = true), Description("Read mapped bytes from an EMS page frame physical page (0-3). Returns hex-encoded data.")]
    public CallToolResult ReadEmsPageFrame(int physicalPage, int offset, int length) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.EmsManager == null) {
                    throw new InvalidOperationException("EMS is not enabled");
                }
                if (physicalPage < 0 || physicalPage >= ExpandedMemoryManager.EmmMaxPhysicalPages) {
                    throw new InvalidOperationException($"Physical page must be between 0 and {ExpandedMemoryManager.EmmMaxPhysicalPages - 1}");
                }
                if (length <= 0 || length > 4096) {
                    throw new InvalidOperationException("Length must be between 1 and 4096");
                }

                EmmPage page = _services.EmsManager.EmmPageFrame[(ushort)physicalPage].PhysicalPage;
                if (offset < 0 || offset >= page.Size) {
                    throw new InvalidOperationException($"Invalid offset: {offset}");
                }
                if (offset + length > page.Size) {
                    throw new InvalidOperationException("Read would exceed page boundary");
                }

                IList<byte> data = page.GetSlice(offset, length);
                byte[] dataArray = new byte[data.Count];
                data.CopyTo(dataArray, 0);

                return new {
                    PhysicalPage = physicalPage,
                    Offset = offset,
                    Length = length,
                    Data = Convert.ToHexString(dataArray)
                };
            }
        });
    }

    [McpServerTool(Name = "read_ems_memory", UseStructuredContent = true), Description("Read bytes from a specific EMS handle and logical page. Returns hex-encoded data.")]
    public CallToolResult ReadEmsMemory(int handle, int logicalPage, int offset, int length) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.EmsManager == null) {
                    throw new InvalidOperationException("EMS is not enabled");
                }
                if (length <= 0 || length > 4096) {
                    throw new InvalidOperationException("Length must be between 1 and 4096");
                }
                if (!_services.EmsManager.EmmHandles.TryGetValue(handle, out EmmHandle? emmHandle)) {
                    throw new InvalidOperationException($"Invalid EMS handle: {handle}");
                }
                if (logicalPage < 0 || logicalPage >= emmHandle.LogicalPages.Count) {
                    throw new InvalidOperationException($"Invalid logical page: {logicalPage}");
                }
                EmmPage page = emmHandle.LogicalPages[logicalPage];
                if (offset < 0 || offset >= page.Size) {
                    throw new InvalidOperationException($"Invalid offset: {offset}");
                }
                if (offset + length > page.Size) {
                    throw new InvalidOperationException("Read would exceed page boundary");
                }

                IList<byte> data = page.GetSlice(offset, length);
                byte[] dataArray = new byte[data.Count];
                data.CopyTo(dataArray, 0);

                return new {
                    Handle = handle, LogicalPage = logicalPage,
                    Offset = offset, Length = length,
                    Data = Convert.ToHexString(dataArray)
                };
            }
        });
    }

    [McpServerTool(Name = "read_xms_state", UseStructuredContent = true), Description("Read XMS (Extended Memory) state: total/free/largest-block memory in KB, HMA availability, allocated block handles with sizes and lock counts.")]
    public CallToolResult QueryXms() {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.XmsManager == null) {
                    throw new InvalidOperationException("XMS is not enabled");
                }

                List<object> handles = new();
                for (int handleId = 1; handleId <= 512; handleId++) {
                    if (!_services.XmsManager.TryGetBlock(handleId, out XmsBlock? block)) {
                        continue;
                    }

                    if (block == null) {
                        continue;
                    }

                    byte lockCount = GetXmsLockCount(handleId);
                    handles.Add(new {
                        HandleId = handleId,
                        SizeKB = (int)(block.Value.Length / 1024),
                        IsLocked = lockCount > 0
                    });
                }

                long freeMemoryKB = _services.XmsManager.TotalFreeMemory / 1024;
                long largestBlockKB = _services.XmsManager.LargestFreeBlockLength / 1024;
                int totalMemoryKB = ExtendedMemoryManager.XmsMemorySize;

                return new {
                    IsEnabled = true,
                    TotalMemoryKB = totalMemoryKB,
                    FreeMemoryKB = (int)freeMemoryKB,
                    LargestBlockKB = (int)largestBlockKB,
                    HmaAvailable = true,
                    HmaAllocated = false,
                    AllocatedBlocks = handles.Count,
                    Handles = handles
                };
            }
        });
    }

    private byte GetXmsLockCount(int handleId) {
        ExtendedMemoryManager? xmsManager = _services.XmsManager;
        if (xmsManager == null) {
            return 0;
        }

        uint savedEax = _services.State.EAX;
        uint savedEbx = _services.State.EBX;
        uint savedEcx = _services.State.ECX;
        uint savedEdx = _services.State.EDX;

        try {
            _services.State.DX = (ushort)handleId;
            xmsManager.GetEmbHandleInformation();
            if (_services.State.AX != 1) {
                return 0;
            }

            return _services.State.BH;
        } finally {
            _services.State.EAX = savedEax;
            _services.State.EBX = savedEbx;
            _services.State.ECX = savedEcx;
            _services.State.EDX = savedEdx;
        }
    }

    [McpServerTool(Name = "read_xms_memory", UseStructuredContent = true), Description("Read bytes from a specific XMS handle at a given offset. Returns hex-encoded data.")]
    public CallToolResult ReadXmsMemory(int handle, uint offset, int length) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                if (_services.XmsManager == null) {
                    throw new InvalidOperationException("XMS is not enabled");
                }
                if (length <= 0 || length > 4096) {
                    throw new InvalidOperationException("Length must be between 1 and 4096");
                }
                if (!_services.XmsManager.TryGetBlock(handle, out XmsBlock? xmsBlock)) {
                    throw new InvalidOperationException($"Invalid XMS handle: {handle}");
                }
                if (offset >= xmsBlock.Value.Length) {
                    throw new InvalidOperationException($"Invalid offset: {offset}");
                }
                if (offset + length > xmsBlock.Value.Length) {
                    throw new InvalidOperationException("Read would exceed block boundary");
                }

                IList<byte> data = _services.XmsManager.XmsRam.GetSlice((int)(xmsBlock.Value.Offset + offset), length);
                byte[] dataArray = new byte[data.Count];
                data.CopyTo(dataArray, 0);

                return new {
                    Handle = handle, Offset = offset,
                    Length = length, Data = Convert.ToHexString(dataArray)
                };
            }
        });
    }

    [McpServerTool(Name = "search_ems_memory", UseStructuredContent = true), Description("Search for a hex byte pattern within one EMS logical page. Returns matching offsets within the page.")]
    public CallToolResult SearchEmsMemory(int handle, int logicalPage, [StringSyntax("Hexadecimal")] string pattern, int startOffset, int length, int limit) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                EmmPage page = ResolveEmsPage(handle, logicalPage);
                byte[] needle = ParseHexPattern(pattern);
                ValidateLimit(limit);
                int searchLength = ComputeSearchWindowLength(startOffset, length, (int)page.Size);
                uint[] matches = SearchArray(page.GetSlice(startOffset, searchLength), needle, (uint)startOffset, limit);
                return new {
                    Handle = handle,
                    LogicalPage = logicalPage,
                    Pattern = Convert.ToHexString(needle),
                    StartOffset = startOffset,
                    Length = searchLength,
                    Matches = matches.Select(static x => (int)x).ToArray(),
                    Truncated = matches.Length >= limit
                };
            }
        });
    }

    [McpServerTool(Name = "search_xms_memory", UseStructuredContent = true), Description("Search for a hex byte pattern within one XMS block. Returns matching offsets within the block.")]
    public CallToolResult SearchXmsMemory(int handle, [StringSyntax("Hexadecimal")] string pattern, uint startOffset, int length, int limit) {
        return ExecuteTool(() => {
            lock (_services.ToolsLock) {
                XmsBlock block = ResolveXmsBlock(handle);
                byte[] needle = ParseHexPattern(pattern);
                ValidateLimit(limit);
                int searchLength = ComputeSearchWindowLength((int)startOffset, length, (int)block.Length);
                if (_services.XmsManager == null) {
                    throw new InvalidOperationException("XMS is not enabled");
                }
                IList<byte> data = _services.XmsManager.XmsRam.GetSlice((int)(block.Offset + startOffset), searchLength);
                uint[] matches = SearchArray(data, needle, startOffset, limit);
                return new {
                    Handle = handle,
                    Pattern = Convert.ToHexString(needle),
                    StartOffset = startOffset,
                    Length = searchLength,
                    Matches = matches,
                    Truncated = matches.Length >= limit
                };
            }
        });
    }

    private EmmPage ResolveEmsPage(int handle, int logicalPage) {
        if (_services.EmsManager == null) {
            throw new InvalidOperationException("EMS is not enabled");
        }
        if (!_services.EmsManager.EmmHandles.TryGetValue(handle, out EmmHandle? emmHandle)) {
            throw new InvalidOperationException($"Invalid EMS handle: {handle}");
        }
        if (logicalPage < 0 || logicalPage >= emmHandle.LogicalPages.Count) {
            throw new InvalidOperationException($"Invalid logical page: {logicalPage}");
        }
        return emmHandle.LogicalPages[logicalPage];
    }

    private XmsBlock ResolveXmsBlock(int handle) {
        if (_services.XmsManager == null) {
            throw new InvalidOperationException("XMS is not enabled");
        }
        if (!_services.XmsManager.TryGetBlock(handle, out XmsBlock? block) || block == null) {
            throw new InvalidOperationException($"Invalid XMS handle: {handle}");
        }
        return block.Value;
    }

    private static int ComputeSearchWindowLength(int startOffset, int requestedLength, int maxLength) {
        if (startOffset < 0 || startOffset >= maxLength) {
            throw new InvalidOperationException($"Invalid offset: {startOffset}");
        }

        int availableLength = maxLength - startOffset;
        if (requestedLength <= 0) {
            return availableLength;
        }
        return Math.Min(requestedLength, availableLength);
    }

    private static uint[] SearchArray(IList<byte> data, byte[] needle, uint baseOffset, int limit) {
        if (data.Count < needle.Length) {
            return Array.Empty<uint>();
        }

        List<uint> matches = new();
        int lastSearchIndex = data.Count - needle.Length;
        for (int i = 0; i <= lastSearchIndex && matches.Count < limit; i++) {
            bool matched = true;
            for (int j = 0; j < needle.Length; j++) {
                if (data[i + j] != needle[j]) {
                    matched = false;
                    break;
                }
            }
            if (matched) {
                matches.Add(baseOffset + (uint)i);
            }
        }
        return matches.ToArray();
    }

    [McpServerTool(Name = "add_breakpoint", UseStructuredContent = true), Description("Add a breakpoint. Parameters: address (long, physical linear address), type (string: CPU_EXECUTION_ADDRESS, MEMORY_ACCESS, MEMORY_WRITE, MEMORY_READ, IO_ACCESS, IO_WRITE, IO_READ), condition (string or null for unconditional).")]
    public CallToolResult AddBreakpoint(long address, string type, [StringSyntax("Spice86BreakpointCondition")] string? condition) {
        return ExecuteTool(() => {
            lock (_services.McpBreakpointsLock) {
                if (!Enum.TryParse(type, true, out BreakPointType bpType)) {
                    string validTypes = string.Join(", ", Enum.GetNames<BreakPointType>());
                    throw new ArgumentException($"Invalid breakpoint type: '{type}'. Valid types: {validTypes}");
                }

                string id = _services.GetNextBreakpointId().ToString();
                Action<BreakPoint> onReached = _ => {
                    _services.PauseHandler.RequestPause($"MCP Breakpoint {id} hit");
                };

                BreakPoint breakPoint;
                if (string.IsNullOrWhiteSpace(condition)) {
                    breakPoint = new AddressBreakPoint(bpType, address, onReached, false);
                } else {
                    BreakpointConditionCompiler compiler = new(_services.State,
                        _services.Memory as Memory ?? throw new InvalidOperationException("Memory must be of type Memory"));
                    Func<long, bool> compiledCondition = compiler.Compile(condition);
                    breakPoint = new AddressBreakPoint(bpType, address, onReached, false, compiledCondition, condition);
                }

                _services.BreakpointsManager.ToggleBreakPoint(breakPoint, true);
                _services.McpBreakpoints[id] = breakPoint;

                return new BreakpointInfo {
                    Id = id, Address = address,
                    Type = bpType, Condition = condition,
                    IsEnabled = true
                };
            }
        });
    }

    [McpServerTool(Name = "list_breakpoints", UseStructuredContent = true), Description("List all breakpoints managed by this MCP session, with their IDs, addresses, types, conditions, and enable state.")]
    public CallToolResult ListBreakpoints() {
        return ExecuteTool(() => {
            lock (_services.McpBreakpointsLock) {
                List<BreakpointInfo> list = _services.McpBreakpoints.Select(kvp => new BreakpointInfo {
                    Id = kvp.Key,
                    Address = (kvp.Value as AddressBreakPoint)?.Address ?? 0,
                    Type = kvp.Value.BreakPointType,
                    Condition = (kvp.Value as AddressBreakPoint)?.ConditionExpression,
                    IsEnabled = kvp.Value.IsEnabled
                }).ToList();

                return new { Breakpoints = list };
            }
        });
    }

    [McpServerTool(Name = "remove_breakpoint", UseStructuredContent = true), Description("Remove a single MCP-managed breakpoint by its ID.")]
    public CallToolResult RemoveBreakpoint(string id) {
        return ExecuteTool(() => {
            lock (_services.McpBreakpointsLock) {
                if (_services.McpBreakpoints.Remove(id, out BreakPoint? bp)) {
                    _services.BreakpointsManager.RemoveUserBreakpoint(bp);
                    return new EmulatorControlResponse { Success = true, Message = $"Breakpoint {id} removed." };
                }
                throw new ArgumentException($"Breakpoint {id} not found.");
            }
        });
    }

    [McpServerTool(Name = "clear_breakpoints", UseStructuredContent = true), Description("Remove all breakpoints managed by this MCP session.")]
    public CallToolResult ClearBreakpoints() {
        return ExecuteTool(() => {
            lock (_services.McpBreakpointsLock) {
                int count = _services.McpBreakpoints.Count;
                _services.BreakpointsManager.RemoveBreakpoints(_services.McpBreakpoints.Values);
                _services.McpBreakpoints.Clear();
                return new EmulatorControlResponse { Success = true, Message = $"{count} breakpoints removed." };
            }
        });
    }
}
