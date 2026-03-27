namespace Spice86.Core.Emulator.Mcp;

using ModelContextProtocol.Server;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

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

    /// <summary>
    /// Gets or sets the Intel 8042 PS/2 controller used for keyboard/mouse MCP automation.
    /// </summary>
    public Intel8042Controller? Intel8042Controller { get; set; }

    /// <summary>
    /// Gets or sets the Sound Blaster device used by medium-level MCP sound tools.
    /// </summary>
    public SoundBlaster? SoundBlaster { get; set; }

    /// <summary>
    /// Gets or sets the OPL synthesizer used by medium-level MCP FM tools.
    /// </summary>
    public Opl3Fm? Opl3Fm { get; set; }

    /// <summary>
    /// Gets or sets the PC speaker device used by MCP speaker tools.
    /// </summary>
    public PcSpeaker? PcSpeaker { get; set; }

    /// <summary>
    /// Gets or sets the MPU-401 MIDI device used by MCP MIDI tools.
    /// </summary>
    public Midi? Midi { get; set; }

    /// <summary>
    /// Gets or sets the high-level VGA functionality used by video MCP tools.
    /// </summary>
    public IVgaFunctionality? VgaFunctionality { get; set; }

    /// <summary>
    /// Gets or sets the BIOS data area accessor used by BIOS and video MCP tools.
    /// </summary>
    public BiosDataArea? BiosDataArea { get; set; }

    /// <summary>
    /// Gets or sets the interrupt vector table used by BIOS and DOS MCP tools.
    /// </summary>
    public InterruptVectorTable? InterruptVectorTable { get; set; }

    /// <summary>
    /// Gets or sets the DOS kernel used by DOS MCP tools.
    /// </summary>
    public Dos? Dos { get; set; }

    // Shared MCP breakpoint tracking state (survives transient tool instances)
    private readonly object _mcpBreakpointsLock = new();
    private readonly Lock _toolsLock = new();
    private readonly object _toolStateLock = new();
    private readonly Dictionary<string, BreakPoint> _mcpBreakpoints = new();
    private readonly Dictionary<string, bool> _toolEnabledState = BuildInitialToolState();
    private int _nextBreakpointId = 1;

    public object McpBreakpointsLock => _mcpBreakpointsLock;

    public Lock ToolsLock => _toolsLock;

    public Dictionary<string, BreakPoint> McpBreakpoints => _mcpBreakpoints;

    public int GetNextBreakpointId() => _nextBreakpointId++;

    internal static IReadOnlyCollection<string> DiscoverToolNames() {
        return GetToolMethods().Select(GetToolName).ToArray();
    }

    internal static string GetToolDescription(string toolName) {
        MethodInfo? method = GetToolMethods().FirstOrDefault(m => GetToolName(m) == toolName);
        return method == null ? toolName : method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? toolName;
    }

    internal static string GetToolArgumentsTemplateJson(string toolName) {
        MethodInfo? method = GetToolMethods().FirstOrDefault(m => GetToolName(m) == toolName);
        return method == null ? "{}" : BuildArgumentsTemplateJson(method);
    }

    public IReadOnlyCollection<string> GetAllToolNames() {
        lock (_toolStateLock) {
            return _toolEnabledState.Keys.ToArray();
        }
    }

    public bool IsToolEnabled(string toolName) {
        lock (_toolStateLock) {
            return _toolEnabledState.GetValueOrDefault(toolName, true);
        }
    }

    public void SetToolEnabled(string toolName, bool isEnabled) {
        lock (_toolStateLock) {
            if (_toolEnabledState.ContainsKey(toolName)) {
                _toolEnabledState[toolName] = isEnabled;
            }
        }
    }

    private static Dictionary<string, bool> BuildInitialToolState() {
        return DiscoverToolNames().ToDictionary(static x => x, static _ => true, StringComparer.Ordinal);
    }

    private static IReadOnlyList<MethodInfo> GetToolMethods() {
        return typeof(Spice86.Mcp.EmulatorMcpTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(static method => method.GetCustomAttribute<McpServerToolAttribute>() != null)
            .ToArray();
    }

    private static string GetToolName(MethodInfo method) {
        McpServerToolAttribute? attribute = method.GetCustomAttribute<McpServerToolAttribute>();
        if (attribute == null || string.IsNullOrWhiteSpace(attribute.Name)) {
            return method.Name;
        }

        return attribute.Name;
    }

    private static string BuildArgumentsTemplateJson(MethodInfo method) {
        Dictionary<string, object?> template = new(StringComparer.Ordinal);
        foreach (ParameterInfo parameter in method.GetParameters()) {
            template[parameter.Name ?? "arg"] = CreateDefaultParameterValue(parameter.ParameterType);
        }

        return JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object? CreateDefaultParameterValue(Type parameterType) {
        Type? nullableType = Nullable.GetUnderlyingType(parameterType);
        if (nullableType != null) {
            return null;
        }
        if (parameterType == typeof(string)) {
            return string.Empty;
        }
        if (parameterType == typeof(bool)) {
            return false;
        }
        if (parameterType.IsEnum) {
            Array values = Enum.GetValues(parameterType);
            return values.Length == 0 ? string.Empty : values.GetValue(0)?.ToString();
        }
        if (parameterType.IsPrimitive || parameterType == typeof(decimal)) {
            return 0;
        }

        return null;
    }
}
