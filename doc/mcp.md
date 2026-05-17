# Spice86 MCP HTTP Server

## Overview

Spice86 exposes a **Model Context Protocol (MCP) server** over HTTP, enabling structured programmatic control of the emulator. AI clients, automation scripts, and external tooling can use MCP to inspect, manipulate, and drive execution of DOS programs running in Spice86.

This server provides **65+ built-in tools** covering:
- CPU state inspection (registers, flags, instruction pointer)
- Memory read/write/search/disassembly
- I/O port read/write
- Execution control (pause, resume, step, step-over)
- Breakpoint management (execution, memory read/write)
- Function discovery and CFG (Control Flow Graph) traversal
- Video state inspection and screenshot capture
- Sound device state (SoundBlaster, OPL, MIDI, PC Speaker)
- DOS structures (PSP, MCB, File handle table)
- EMS and XMS memory management

The HTTP transport is **stateless** by default to maximize compatibility with real-world AI clients that may skip session negotiation or reuse connection state unpredictably.

---

## Endpoints

| Endpoint | Description |
|----------|-------------|
| `http://localhost:<port>/mcp` | MCP protocol endpoint (JSON-RPC over HTTP) |
| `http://localhost:<port>/health` | Health check endpoint returning `{"status":"ok"}` |

**Default port:** `8081`

**CLI option:** `--mcp-http-port <port>`

---

## Quick Start

### 1. Start Spice86 with MCP enabled (enabled by default)

```bash
Spice86 -e program.exe --mcp-http-port 8081
```

### 2. Connect your MCP client

Point your MCP client to:
```
http://localhost:8081/mcp
```

### 3. Discover available tools

```json
POST /mcp
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2024-11-05",
    "capabilities": {},
    "clientInfo": { "name": "MyClient", "version": "1.0" }
  }
}
```

Then call `tools/list` to enumerate all available tools, or call `mcp_about` for high-level capability metadata.

---

## Protocol Behavior

- **Transport:** HTTP (stateless by default)
- **Protocol:** JSON-RPC 2.0
- **Session management:** The server does **not** issue `Mcp-Session-Id` headers by default (`options.Stateless = true`). This prevents 404 errors when AI clients reuse session IDs from fresh TCP connections or skip the `notifications/initialized` handshake.
- **Standard MCP flow:** `initialize` → `tools/list` → `tools/call`

**Stateless mode** is recommended for AI agents and automated workflows. If you need stateful sessions (e.g., for long-lived interactive clients), you can modify `McpHttpHost.cs` to set `options.Stateless = false`.

---

## Built-in Tool Categories

Spice86 ships with **65+ MCP tools** organized into these capability scopes:

### CPU State & Execution Control
- `read_cpu_state`: Read all general-purpose registers, segment registers, IP, flags, and cycle count
- `pause`: Pause emulation
- `resume`: Resume emulation
- `step`: Execute one instruction and pause
- `step_over`: Step over CALL instructions (run until RET)
- `get_pause_status`: Check if emulator is paused

### Memory Operations
- `read_memory`: Read a memory range (segment, offset, length) as hex bytes
- `write_memory`: Write hex bytes to memory
- `search_memory`: Search for a hex pattern in conventional RAM
- `read_disassembly`: Disassemble instructions at a given address

### I/O Ports
- `read_io_port`: Read from an I/O port
- `write_io_port`: Write to an I/O port

### Breakpoints
- `add_execution_breakpoint`: Break when CS:IP reaches an address
- `add_memory_read_breakpoint`: Break on memory read
- `add_memory_write_breakpoint`: Break on memory write
- `remove_execution_breakpoint`
- `remove_memory_read_breakpoint`
- `remove_memory_write_breakpoint`
- `list_execution_breakpoints`
- `list_memory_breakpoints`

### Functions & CFG
- `list_functions`: List discovered functions sorted by call count
- `read_cfg_cpu_graph`: Dump the Control Flow Graph (CFG) built by `CfgCpu`

### Video
- `read_video_state`: Current video mode, resolution, text/graphics flag, cursor position
- `read_video_state_detailed`: Full VGA register dump
- `video_set_mode`: Change video mode
- `read_vga_memory`: Read VGA VRAM
- `write_vga_memory`: Write VGA VRAM
- `vga_set_palette_entry`: Set VGA palette color
- `capture_screenshot`: Save a screenshot to disk and return the path

### Sound Devices
- **SoundBlaster:** `read_sound_blaster_state`, `sound_blaster_set_speaker`, `read_sound_blaster_dsp_version`, `sound_blaster_write_mixer_register`, etc.
- **OPL (Adlib/SB OPL2/OPL3):** `read_opl_state`, `opl_write_register`
- **PC Speaker:** `read_pc_speaker_state`, `pc_speaker_set_control`
- **MIDI:** `read_midi_state`, `midi_reset`, `midi_enter_uart_mode`, `midi_send_bytes`

### Input Automation
- `send_keyboard_key`: Send a keystroke (press + release or hold)
- `send_mouse_packet`: Send raw mouse data packet
- `send_mouse_move`: Move mouse cursor
- `send_mouse_button`: Press/release mouse button

### DOS & BIOS Structures
- `read_dos_psp`: Read the DOS Program Segment Prefix
- `read_dos_mcb_chain`: Dump the DOS Memory Control Block chain
- `read_dos_file_handle_table`: List open file handles
- `read_dos_current_directory`: Get current drive and directory path
- `read_bios_equipment_word`: Read BIOS equipment flags

### EMS & XMS
- `read_ems_state`: EMS handle allocation, page frame mapping
- `read_xms_state`: XMS handle allocation, HMA usage
- `read_ems_page_frame`: Dump EMS page frame content
- `read_xms_block`: Read an XMS memory block

### Metadata & Diagnostics
- `mcp_about`: High-level server metadata, capability scopes, extension points, tool count

For a complete tool list with parameter details, call `tools/list` via the MCP endpoint.

---

## Tool Invocation & Auto-Pause

Most tools automatically **pause** the emulator before execution and **resume** after. This ensures consistent state during inspection and prevents race conditions. A few tools (marked with `[McpManualControl]`) skip auto-pause and require the client to explicitly call `pause` if needed.

**Example: Reading CPU state**

```json
POST /mcp
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "read_cpu_state",
    "arguments": {}
  }
}
```

**Response:**

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      {
        "type": "resource",
        "resource": {
          "uri": "data:application/json;base64,eyJFQVgiOjEyMywgIkVCWCI6NDU2LCAuLi59"
        }
      }
    ]
  }
}
```

The structured content (JSON) is base64-encoded in the resource field.

**Example: Writing memory**

```json
POST /mcp
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "write_memory",
    "arguments": {
      "segment": 4096,
      "offset": 256,
      "data": "B80200CD21"
    }
  }
}
```

---

## Extending MCP from User Projects

Spice86 supports **external MCP tool registration**. You can add project-specific tools for game-specific operations (e.g., reading player stats, manipulating inventory, triggering events).

### Extension Model

1. **Implement `IMcpToolSupplier`** in your project to provide custom tool assemblies and injectable services.
2. **Mark your tool classes with `[McpServerToolType]`** and individual methods with `[McpServerTool(Name = "...")]`.
3. **Register your tools at startup** by passing additional assemblies and services to `McpHttpHost.Start`.

### Extension Entry Points

| Method | Purpose |
|--------|---------|
| `IMcpToolSupplier.GetMcpToolAssemblies()` | Return additional assemblies containing `[McpServerToolType]` classes |
| `IMcpToolSupplier.GetMcpServices()` | Return injectable services used by custom tools |
| `McpHttpHost.Start(additionalToolAssemblies, additionalServices)` | Register external tools at server start |

### Minimal Extension Example

```csharp
using Spice86.Core.Emulator.Mcp;
using ModelContextProtocol.Server;

// 1. Implement IMcpToolSupplier in your override project
public class MyGameMcpToolSupplier : IMcpToolSupplier {
    public IEnumerable<Assembly> GetMcpToolAssemblies() {
        return [typeof(MyGameMcpTools).Assembly];
    }

    public IEnumerable<object> GetMcpServices() {
        return [new MyGameContext(...)];
    }
}

// 2. Define your custom tools
[McpServerToolType]
public sealed class MyGameMcpTools {
    private readonly MyGameContext _context;

    public MyGameMcpTools(MyGameContext context, EmulatorMcpServices emulatorServices) {
        _context = context;
    }

    [McpServerTool(Name = "read_player_health", UseStructuredContent = true)]
    public object ReadPlayerHealth() {
        // Read from memory via _context or emulatorServices
        int health = _context.GetPlayerHealth();
        return new { Health = health, MaxHealth = 100 };
    }

    [McpServerTool(Name = "set_player_gold", UseStructuredContent = true)]
    public object SetPlayerGold(int amount) {
        _context.SetPlayerGold(amount);
        return new { Success = true, Gold = amount };
    }
}
```

### Registering Custom Tools at Startup

Modify your startup wiring (typically in `Spice86DependencyInjection.cs` or a custom entry point):

```csharp
IMcpToolSupplier supplier = new MyGameMcpToolSupplier();
McpHttpHost host = new(loggerService);
host.Start(
    services: emulatorMcpServices,
    port: configuration.McpHttpPort,
    additionalToolAssemblies: supplier.GetMcpToolAssemblies(),
    additionalServices: supplier.GetMcpServices()
);
```

**Note:** The default `Spice86` startup does **not** load external tool assemblies. You must wire them explicitly if you want project-specific tools.

---

## Practical Guidance for Extension Authors

### Tool Design Best Practices

1. **Keep tools deterministic:** Avoid relying on global mutable state outside the emulator.
2. **Prefer semantic tools:** Expose high-level operations (e.g., `read_player_inventory`) instead of raw memory offsets.
3. **Keep low-level tools available:** Also provide `read_memory_at_player_inventory_address` for diagnostics when the semantic tool breaks.
4. **Return compact structured payloads:** Avoid dumping large arrays unless necessary. Use pagination or limits.
5. **Add integration tests:** Write real MCP `tools/call` tests that verify your tools work end-to-end.

### Debugging Extension Tools

- **Check the MCP log:** `logs/mcp.log` contains startup and invocation errors.
- **Verify tool registration:** Call `tools/list` and ensure your custom tools appear.
- **Test auto-pause behavior:** If your tool accesses emulator state, ensure it pauses correctly or mark it with `[McpManualControl]`.

---

## Common Use Cases

### AI-Driven Reverse Engineering

An AI agent can:
1. Call `read_cpu_state` to see where the program is stuck.
2. Call `read_disassembly` to inspect the next 10 instructions.
3. Call `search_memory` to find a string or data pattern.
4. Call `add_execution_breakpoint` to pause at a suspect function.
5. Call `list_functions` to see which functions are called most often.
6. Call `capture_screenshot` to see the current video output.

### Automated Testing

A test script can:
1. Call `pause` to halt execution.
2. Call `write_memory` to inject test data.
3. Call `resume` and wait for a breakpoint.
4. Call `read_dos_file_handle_table` to verify the program opened the expected file.
5. Call `capture_screenshot` and compare against a baseline image.

### Game Trainer / Cheat Tool

A trainer tool can:
1. Call `read_dos_psp` to locate the game's data segment.
2. Call `search_memory` to find the player's health value.
3. Call `write_memory` to set health to max.
4. Call `add_memory_write_breakpoint` to detect when the game decrements health.

### Live Debugging Dashboard

A web dashboard can:
1. Poll `read_cpu_state` every 500ms to display registers.
2. Call `read_video_state` to show current video mode.
3. Call `read_sound_blaster_state` to visualize audio channels.
4. Call `list_functions` to show a live call-count heatmap.

---

## Logs & Troubleshooting

- **MCP server log:** `logs/mcp.log` (warning level by default)
- **Emulator logs:** Console or file (controlled by `--VerboseLogs`, `--WarningLogs`, `--SilencedLogs`)
- **Health check:** `GET http://localhost:8081/health` should return `{"status":"ok","service":"Spice86 MCP Server"}`

**Common issues:**

| Problem | Solution |
|---------|----------|
| Client gets 404 on `/mcp` | Check that `--mcp-http-port` is set and the server started successfully |
| Tools return "Tool disabled" error | Some tools may be disabled if the emulator is not in the expected state (e.g., video tools when no VGA card is initialized) |
| Tools time out | If emulator is not responding to pause requests, ensure the emulation loop is running and not deadlocked |
| Client skips session ID and gets 404 | Ensure stateless mode is enabled (default); if using stateful mode, ensure client sends `Mcp-Session-Id` header |

---

## Reference

### Related Files

- `Spice86.Core/Emulator/Mcp/McpHttpHost.cs` - HTTP server setup and lifecycle
- `Spice86.Core/Emulator/Mcp/EmulatorMcpTools.cs` - Built-in tool implementations
- `Spice86.Core/Emulator/Mcp/EmulatorMcpServices.cs` - Injected services for built-in tools
- `Spice86.Core/Emulator/Mcp/IMcpToolSupplier.cs` - Extension interface for custom tools
- `Spice86.Core/Emulator/Mcp/Response/McpAboutResponse.cs` - Metadata response structure

### Further Reading

- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [Spice86 CFG CPU Documentation](cfgcpuReadme.md)
- [Spice86 Internal Debugger Wiki](https://github.com/OpenRakis/Spice86/wiki/Spice86-internal-debugger)
- [Cryogenic Project (MCP Extension Example)](https://github.com/OpenRakis/Cryogenic)

**Quick links:**
- Health check: `http://localhost:8081/health`
- MCP endpoint: `http://localhost:8081/mcp`
- Tool discovery: Call `tools/list` or `mcp_about`
