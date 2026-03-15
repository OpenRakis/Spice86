namespace Spice86.Core.Emulator.Mcp;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

/// <summary>
/// Holds emulator services needed by MCP tool implementations.
/// Registered as a singleton in DI so that tool classes can constructor-inject it.
/// </summary>
public sealed class EmulatorMcpServices(
    IMemory memory,
    State state,
    FunctionCatalogue functionCatalogue,
    CfgCpu cfgCpu,
    IOPortDispatcher ioPortDispatcher,
    IVgaRenderer vgaRenderer,
    IPauseHandler pauseHandler,
    ExpandedMemoryManager? emsManager,
    ExtendedMemoryManager? xmsManager,
    EmulatorBreakpointsManager breakpointsManager,
    ILoggerService loggerService) {
    public IMemory Memory { get; } = memory;
    public State State { get; } = state;
    public FunctionCatalogue FunctionCatalogue { get; } = functionCatalogue;
    public CfgCpu CfgCpu { get; } = cfgCpu;
    public IOPortDispatcher IoPortDispatcher { get; } = ioPortDispatcher;
    public IVgaRenderer VgaRenderer { get; } = vgaRenderer;
    public IPauseHandler PauseHandler { get; } = pauseHandler;
    public ExpandedMemoryManager? EmsManager { get; } = emsManager;
    public ExtendedMemoryManager? XmsManager { get; } = xmsManager;
    public EmulatorBreakpointsManager BreakpointsManager { get; } = breakpointsManager;
    public ILoggerService LoggerService { get; } = loggerService;

    // Shared MCP breakpoint tracking state (survives transient tool instances)
    private readonly object _mcpBreakpointsLock = new();
    private readonly Dictionary<string, BreakPoint> _mcpBreakpoints = new();
    private int _nextBreakpointId = 1;

    public object McpBreakpointsLock => _mcpBreakpointsLock;
    public Dictionary<string, BreakPoint> McpBreakpoints => _mcpBreakpoints;
    public int GetNextBreakpointId() => _nextBreakpointId++;
}