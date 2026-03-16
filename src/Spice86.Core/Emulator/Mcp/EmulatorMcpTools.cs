namespace Spice86.Mcp;

using ModelContextProtocol.Server;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Mcp;
using Spice86.Core.Emulator.Mcp.Response;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

/// <summary>
/// MCP tool implementations for emulator inspection and control.
/// Discovered automatically by the SDK via <c>WithToolsFromAssembly</c>.
/// </summary>
[McpServerToolType]
public sealed class EmulatorMcpTools {
    private readonly EmulatorMcpServices _services;

    public EmulatorMcpTools(EmulatorMcpServices services) => _services = services;

    [McpServerTool(Name = "read_cpu_registers"), Description("Read CPU registers")]
    public CpuRegistersResponse ReadCpuRegisters() {
        lock (_services.ToolsLock) {
            State state = _services.State;
            return new CpuRegistersResponse {
                GeneralPurpose = new GeneralPurposeRegisters {
                    EAX = state.EAX, EBX = state.EBX, ECX = state.ECX, EDX = state.EDX,
                    ESI = state.ESI, EDI = state.EDI, ESP = state.ESP, EBP = state.EBP
                },
                Segments = new SegmentRegisters {
                    CS = state.CS, DS = state.DS, ES = state.ES,
                    FS = state.FS, GS = state.GS, SS = state.SS
                },
                InstructionPointer = new InstructionPointer { IP = state.IP },
                Flags = new CpuFlags {
                    CarryFlag = state.CarryFlag, ParityFlag = state.ParityFlag,
                    AuxiliaryFlag = state.AuxiliaryFlag, ZeroFlag = state.ZeroFlag,
                    SignFlag = state.SignFlag, DirectionFlag = state.DirectionFlag,
                    OverflowFlag = state.OverflowFlag, InterruptFlag = state.InterruptFlag
                }
            };
        }
    }

    [McpServerTool(Name = "read_memory"), Description("Read memory range (max 4096 bytes). Returns hex-encoded data.")]
    public MemoryReadResponse ReadMemory(uint address, int length) {
        lock (_services.ToolsLock) {
            if (length <= 0 || length > 4096) {
                throw new InvalidOperationException("Length must be between 1 and 4096");
            }
            byte[] data = _services.Memory.ReadRam((uint)length, address);
            return new MemoryReadResponse {
                Address = address,
                Length = length,
                Data = Convert.ToHexString(data)
            };
        }
    }

    [McpServerTool(Name = "search_memory"), Description("Search RAM for a hex-encoded byte sequence. Returns absolute addresses of matches.")]
    public MemorySearchResponse SearchMemory(string pattern, uint startAddress, int length, int limit) {
        lock (_services.ToolsLock) {
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

            if (limit <= 0 || limit > 10_000) {
                throw new ArgumentException("Limit must be between 1 and 10000", nameof(limit));
            }

            byte[] needle;
            try {
                needle = Convert.FromHexString(normalized);
            } catch (FormatException ex) {
                throw new ArgumentException("Pattern must be a valid hex string", nameof(pattern), ex);
            }

            if (needle.Length == 0) {
                throw new ArgumentException("Pattern must contain at least one byte", nameof(pattern));
            }

            if (startAddress >= _services.Memory.Length) {
                throw new ArgumentOutOfRangeException(nameof(startAddress), "Start address is outside memory range");
            }

            int maxLen = _services.Memory.Length - (int)startAddress;
            int searchLen;
            if (length <= 0) {
                searchLen = maxLen;
            } else {
                searchLen = Math.Min(length, maxLen);
            }

            if (searchLen <= 0) {
                return new MemorySearchResponse {
                    Pattern = Convert.ToHexString(needle),
                    StartAddress = startAddress,
                    Length = 0,
                    Matches = Array.Empty<uint>(),
                    Truncated = false
                };
            }

            List<uint> matches = new();
            uint current = startAddress;
            int remaining = searchLen;

            while (remaining > 0 && matches.Count < limit) {
                uint? found = _services.Memory.SearchValue(current, remaining, needle);
                if (!found.HasValue) {
                    break;
                }

                uint foundAddress = found.Value;
                matches.Add(foundAddress);

                if (foundAddress + 1 <= foundAddress) {
                    break;
                }

                if (foundAddress < current) {
                    break;
                }

                uint next = foundAddress + 1;
                long newRemainingLong = (long)startAddress + searchLen - next;
                remaining = newRemainingLong > 0 ? (int)newRemainingLong : 0;
                current = next;
            }

            return new MemorySearchResponse {
                Pattern = Convert.ToHexString(needle),
                StartAddress = startAddress,
                Length = searchLen,
                Matches = matches.ToArray(),
                Truncated = matches.Count >= limit && remaining > 0
            };
        }
    }

    [McpServerTool(Name = "list_functions"), Description("List functions ordered by call count")]
    public FunctionListResponse ListFunctions(int limit) {
        lock (_services.ToolsLock) {
            FunctionInfo[] functions = _services.FunctionCatalogue.FunctionInformations.Values
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
                TotalCount = _services.FunctionCatalogue.FunctionInformations.Count
            };
        }
    }

    [McpServerTool(Name = "read_cfg_cpu_graph"), Description("Read CFG CPU statistics")]
    public CfgCpuGraphResponse ReadCfgCpuGraph() {
        lock (_services.ToolsLock) {
            ExecutionContextManager contextManager = _services.CfgCpu.ExecutionContextManager;
            ExecutionContext currentContext = contextManager.CurrentExecutionContext;

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
    }

    [McpServerTool(Name = "read_io_port"), Description("Read from IO port")]
    public IoPortReadResponse ReadIoPort(int port) {
        lock (_services.ToolsLock) {
            if (_services.PauseHandler.IsPaused) {
                throw new InvalidOperationException("Emulator is paused. Resume to read IO ports.");
            }
            if (port < 0 || port > 65535) {
                throw new ArgumentException("Port must be 0-65535");
            }
            byte value = _services.IoPortDispatcher.ReadByte((ushort)port);
            return new IoPortReadResponse { Port = port, Value = value };
        }
    }

    [McpServerTool(Name = "write_io_port"), Description("Write to IO port")]
    public IoPortWriteResponse WriteIoPort(int port, int value) {
        lock (_services.ToolsLock) {
            if (_services.PauseHandler.IsPaused) {
                throw new InvalidOperationException("Emulator is paused. Resume to write IO ports.");
            }
            if (port < 0 || port > 65535) {
                throw new ArgumentException("Port must be 0-65535");
            }
            if (value < 0 || value > 255) {
                throw new ArgumentException("Value must be 0-255");
            }
            _services.IoPortDispatcher.WriteByte((ushort)port, (byte)value);
            return new IoPortWriteResponse { Port = port, Value = value, Success = true };
        }
    }

    [McpServerTool(Name = "get_video_state"), Description("Get video card state")]
    public VideoStateResponse GetVideoState() {
        lock (_services.ToolsLock) {
            return new VideoStateResponse {
                Width = _services.VgaRenderer.Width,
                Height = _services.VgaRenderer.Height,
                BufferSize = _services.VgaRenderer.BufferSize
            };
        }
    }

    [McpServerTool(Name = "screenshot"), Description("Capture screenshot as base64-encoded BGRA32 raw data")]
    public ScreenshotResponse TakeScreenshot() {
        lock (_services.ToolsLock) {
            int width = _services.VgaRenderer.Width;
            int height = _services.VgaRenderer.Height;
            uint[] buffer = new uint[width * height];
            _services.VgaRenderer.Render(buffer);
            byte[] bytes = new byte[buffer.Length * 4];
            Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
            return new ScreenshotResponse {
                Width = width, Height = height,
                Format = "bgra32", Data = Convert.ToBase64String(bytes)
            };
        }
    }

    [McpServerTool(Name = "pause_emulator"), Description("Immediately stop the emulation. Use this to inspect state at an arbitrary point.")]
    public EmulatorControlResponse PauseEmulator() {
        lock (_services.ToolsLock) {
            if (_services.PauseHandler.IsPaused) {
                return new EmulatorControlResponse { Success = true, Message = "Already paused" };
            }
            _services.PauseHandler.RequestPause("MCP server request");
            return new EmulatorControlResponse { Success = true, Message = "Paused" };
        }
    }

    [McpServerTool(Name = "resume_emulator"), Description("Resume continuous execution of the emulator. Also known as 'go'.")]
    public EmulatorControlResponse ResumeEmulator() {
        lock (_services.ToolsLock) {
            if (!_services.PauseHandler.IsPaused) {
                return new EmulatorControlResponse { Success = true, Message = "Already running" };
            }
            _services.PauseHandler.Resume();
            return new EmulatorControlResponse { Success = true, Message = "Resumed" };
        }
    }

    [McpServerTool(Name = "go"), Description("Alias for resume_emulator. Resumes continuous execution.")]
    public EmulatorControlResponse Go() {
        return ResumeEmulator();
    }

    [McpServerTool(Name = "step"), Description("Execute exactly one CPU instruction and then pause again. Useful for trace analysis.")]
    public EmulatorControlResponse Step() {
        lock (_services.ToolsLock) {
            if (!_services.PauseHandler.IsPaused) {
                _services.PauseHandler.RequestPause("Step requested while running");
            }

            Action<BreakPoint> onReached = _ => {
                _services.PauseHandler.RequestPause("Single step hit");
            };

            UnconditionalBreakPoint stepBp = new(BreakPointType.CPU_EXECUTION_ADDRESS, onReached, true);
            _services.BreakpointsManager.ToggleBreakPoint(stepBp, true);
            _services.PauseHandler.Resume();

            return new EmulatorControlResponse { Success = true, Message = "Stepping..." };
        }
    }

    [McpServerTool(Name = "read_stack"), Description("Read the top values of the stack (SS:SP). Returns addresses and 16-bit values.")]
    public StackResponse ReadStack(int count) {
        lock (_services.ToolsLock) {
            if (count <= 0 || count > 100) {
                throw new ArgumentException("Count must be between 1 and 100");
            }

            ushort ss = _services.State.SS;
            ushort sp = _services.State.SP;
            uint baseAddress = (uint)(ss << 4) + sp;

            List<StackValue> values = new();
            for (int i = 0; i < count; i++) {
                uint addr = baseAddress + (uint)(i * 2);
                if (addr + 1 >= _services.Memory.Length) break;
                ushort val = _services.Memory.UInt16[addr];
                values.Add(new StackValue { Address = addr, Value = val });
            }

            return new StackResponse { Ss = ss, Sp = sp, Values = values };
        }
    }

    [McpServerTool(Name = "query_ems"), Description("Query EMS (Expanded Memory Manager) state")]
    public EmsStateResponse QueryEms() {
        lock (_services.ToolsLock) {
            if (_services.EmsManager == null) {
                throw new InvalidOperationException("EMS is not enabled");
            }

            EmsHandleInfo[] handles = _services.EmsManager.EmmHandles
                .Where(kvp => kvp.Key != ExpandedMemoryManager.EmmNullHandle && kvp.Value != null)
                .Select(kvp => new EmsHandleInfo {
                    HandleId = kvp.Key,
                    AllocatedPages = kvp.Value.LogicalPages.Count,
                    Name = kvp.Value.Name
                })
                .ToArray();

            int allocatedPages = handles.Sum(h => h.AllocatedPages);
            ushort freePages = _services.EmsManager.GetFreePageCount();

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
    }

    [McpServerTool(Name = "read_ems_memory"), Description("Read EMS (Expanded Memory) from a specific handle and page")]
    public EmsMemoryReadResponse ReadEmsMemory(int handle, int logicalPage, int offset, int length) {
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

            return new EmsMemoryReadResponse {
                Handle = handle, LogicalPage = logicalPage,
                Offset = offset, Length = length,
                Data = Convert.ToHexString(dataArray)
            };
        }
    }

    [McpServerTool(Name = "query_xms"), Description("Query XMS (Extended Memory Manager) state")]
    public XmsStateResponse QueryXms() {
        lock (_services.ToolsLock) {
            if (_services.XmsManager == null) {
                throw new InvalidOperationException("XMS is not enabled");
            }

            long freeMemoryKB = _services.XmsManager.TotalFreeMemory / 1024;
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
    }

    [McpServerTool(Name = "read_xms_memory"), Description("Read XMS (Extended Memory) from a specific handle")]
    public XmsMemoryReadResponse ReadXmsMemory(int handle, uint offset, int length) {
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

            return new XmsMemoryReadResponse {
                Handle = handle, Offset = offset,
                Length = length, Data = Convert.ToHexString(dataArray)
            };
        }
    }

    [McpServerTool(Name = "add_breakpoint"), Description("Add a breakpoint (execution, memory, or IO). Valid types: CPU_EXECUTION_ADDRESS, MEMORY_ACCESS, MEMORY_WRITE, MEMORY_READ, IO_ACCESS, IO_WRITE, IO_READ (case-insensitive). Pass null for condition to add an unconditional breakpoint.")]
    public BreakpointInfo AddBreakpoint(long address, string type, string? condition) {
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
                Type = bpType.ToString(), Condition = condition,
                IsEnabled = true
            };
        }
    }

    [McpServerTool(Name = "list_breakpoints"), Description("List all MCP-managed breakpoints")]
    public BreakpointListResponse ListBreakpoints() {
        lock (_services.McpBreakpointsLock) {
            List<BreakpointInfo> list = _services.McpBreakpoints.Select(kvp => new BreakpointInfo {
                Id = kvp.Key,
                Address = (kvp.Value as AddressBreakPoint)?.Address ?? 0,
                Type = kvp.Value.BreakPointType.ToString(),
                Condition = (kvp.Value as AddressBreakPoint)?.ConditionExpression,
                IsEnabled = kvp.Value.IsEnabled
            }).ToList();

            return new BreakpointListResponse { Breakpoints = list };
        }
    }

    [McpServerTool(Name = "remove_breakpoint"), Description("Remove an MCP-managed breakpoint by ID")]
    public EmulatorControlResponse RemoveBreakpoint(string id) {
        lock (_services.McpBreakpointsLock) {
            if (_services.McpBreakpoints.Remove(id, out BreakPoint? bp)) {
                _services.BreakpointsManager.ToggleBreakPoint(bp, false);
                return new EmulatorControlResponse { Success = true, Message = $"Breakpoint {id} removed." };
            }
            throw new ArgumentException($"Breakpoint {id} not found.");
        }
    }

    [McpServerTool(Name = "clear_breakpoints"), Description("Remove ALL breakpoints currently managed by the MCP server.")]
    public EmulatorControlResponse ClearBreakpoints() {
        lock (_services.McpBreakpointsLock) {
            int count = _services.McpBreakpoints.Count;
            foreach (BreakPoint bp in _services.McpBreakpoints.Values) {
                _services.BreakpointsManager.ToggleBreakPoint(bp, false);
            }
            _services.McpBreakpoints.Clear();
            return new EmulatorControlResponse { Success = true, Message = $"{count} breakpoints removed." };
        }
    }
}
