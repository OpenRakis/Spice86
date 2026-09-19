# Spice86 MCP HTTP Server

## Overview

Spice86 exposes a **Model Context Protocol (MCP) server** over HTTP, enabling structured programmatic control of the emulator. AI clients, automation scripts, and external tooling can use MCP to inspect, manipulate, and drive execution of DOS programs running in Spice86.

This server provides **73 built-in tools** covering:

- CPU state inspection (registers, flags, instruction pointer)
- Memory read/write/search/disassembly
- I/O port read/write
- Execution control (pause, resume, step, step-over)
- Breakpoint management (execution, memory, I/O)
- Function discovery and CFG (Control Flow Graph) traversal
- Video state inspection and screenshot capture
- Sound device state (Sound Blaster, OPL, MIDI, PC Speaker, Gravis UltraSound)
- DOS and BIOS structures
- EMS and XMS memory management

The HTTP transport is **stateless** by default to maximize compatibility with real-world AI clients that may skip session negotiation or reuse connection state unpredictably.

---

## Endpoints

| Endpoint | Description |
|----------|-------------|
| `http://localhost:<port>/mcp` | MCP protocol endpoint (JSON-RPC over HTTP) |
| `http://localhost:<port>/health` | Health check endpoint returning `{"status":"ok"}` |

**Default port:** `8081` (set the port to `0` to disable the server)

**CLI option:** `--McpHttpPort <port>`

---

## Quick Start

### 1. Start Spice86 with MCP enabled (enabled by default)

```bash
Spice86 -e program.exe --McpHttpPort 8081
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

Spice86 ships with **73 MCP tools** organized into these capability scopes. The names below are the
authoritative `tools/list` names; call `tools/list` for the parameter schema of each one.

### CPU State & Execution Control

- `read_cpu_state`: Read all general-purpose registers, segment registers, IP, flags, and cycle count
- `read_stack`: Read the top N 16-bit words at SS:SP
- `pause_emulator`: Pause emulation
- `resume_emulator`: Resume emulation
- `go`: Alias for `resume_emulator`
- `step`: Execute one instruction and pause
- `step_over`: Step over CALL and INT instructions (run until the return address)

### Memory Operations

- `read_memory`: Read a memory range (segment, offset, length) as hex bytes
- `write_memory`: Write hex bytes to memory
- `search_memory`: Search for a hex pattern in conventional RAM
- `read_disassembly`: Disassemble instructions at a given address

### I/O Ports

- `read_io_port`: Read from an I/O port
- `write_io_port`: Write to an I/O port

### Breakpoints

Breakpoints created here are scoped to the MCP session.

- `add_breakpoint`: Add a breakpoint of type `CPU_EXECUTION_ADDRESS`, `MEMORY_ACCESS`, `MEMORY_WRITE`, `MEMORY_READ`, `IO_ACCESS`, `IO_WRITE` or `IO_READ`, with an optional condition
- `list_breakpoints`: List the MCP-managed breakpoints
- `remove_breakpoint`: Remove one breakpoint by ID
- `clear_breakpoints`: Remove all MCP-managed breakpoints

### Functions & CFG

- `list_functions`: List discovered functions sorted by call count
- `read_cfg_cpu_graph`: Dump the Control Flow Graph (CFG) built by `CfgCpu`

### Video

- `read_video_state`: Renderer width, height, and framebuffer size
- `read_video_state_detailed`: BIOS video mode, VGA mode, cursor, screen dimensions
- `read_video_cursor`, `video_set_cursor_position`, `video_set_active_page`
- `video_set_mode`: Change video mode
- `video_write_text`, `video_read_character`
- `video_write_pixel`, `video_read_pixel`
- `read_video_palette`: EGA/VGA palette registers, overscan, pixel mask
- `screenshot`: Capture a PNG and return it inline with its metadata

### Sound Devices

- **Sound Blaster:** `read_sound_blaster_state`, `sound_blaster_set_speaker`, `read_sound_blaster_dsp_version`, `read_sound_blaster_mixer_state`, `sound_blaster_write_mixer_register`
- **OPL (AdLib / SB OPL2 / OPL3):** `read_opl_state`, `opl_write_register`
- **PC Speaker:** `read_pc_speaker_state`, `pc_speaker_set_control`
- **MIDI (MPU-401):** `read_midi_state`, `midi_reset`, `midi_enter_uart_mode`, `midi_send_bytes`
- **Gravis UltraSound:** see the dedicated section below

### Input Automation

- `send_keyboard_key`: Send a key press or release
- `send_mouse_packet`: Send a raw PS/2 AUX packet
- `send_mouse_move`: Move the mouse to a normalized screen position
- `send_mouse_button`: Press or release a mouse button

### DOS & BIOS Structures

- `read_bios_data_area`: Key BIOS Data Area fields
- `read_interrupt_vector`: Read one IVT entry
- `read_dos_state`: Current drive, mounted drives, PSP segment, device count, EMS/XMS availability
- `read_dos_program_state`: PSP segment, parent PSP, environment segment, open-file limit, command tail
- `read_dos_current_directory`, `dos_set_current_directory`
- `dos_set_default_drive`

### EMS & XMS

- `read_ems_state`: Page frame segment, page counts, handles, physical-to-logical mappings
- `read_ems_page_frame`: Dump one EMS page frame physical page
- `read_ems_memory`: Read one EMS handle's logical page
- `search_ems_memory`: Search within one EMS logical page
- `read_xms_state`: Total/free/largest block, HMA availability, allocated handles
- `read_xms_memory`: Read one XMS handle at an offset
- `search_xms_memory`: Search within one XMS block

### Metadata & Diagnostics

- `mcp_about`: High-level server metadata, capability scopes, extension points, tool count

For a complete tool list with parameter details, call `tools/list` via the MCP endpoint.

---

## Gravis UltraSound Tools

The GUS tools require the card to be enabled, which is the default. Disable it with `--GusEnable false`;
the base port, IRQ, DMA channel and patch directory are controlled by `--GusBase`, `--GusIrq`, `--GusDma`
and `--GusUltradir`. When the card is disabled, every GUS tool returns `Gravis UltraSound is not available`.

| Tool | Purpose |
| ------ | --------- |
| `read_gus_state` | Base port, playback/recording IRQ and DMA, reset-register flags, active voice count and mask, output sample rate, the mix/timer/sample/DMA/IRQ registers, the selected voice and register, DRAM size and pointer, the `ULTRASND`/`ULTRADIR` values, mixer channel info, and both hardware timers |
| `read_gus_voices` | Per-voice state for a range of the 32 voices: wave start/end/position/increment, wave rate, volume ramp, pan position, decoded wave and volume control flags, pending IRQs, and generated-audio counters |
| `read_gus_dram` | Read up to 4096 raw bytes from the 1 MiB on-board sample DRAM as a hex string |
| `search_gus_dram` | Find a hex byte pattern in the sample DRAM and return the matching DRAM addresses |
| `read_gus_dma_state` | DMA channels, the DMA control register with decoded flags, the sampling control register, transfer width, the DMA address register and nibble, the resolved DRAM offset, and any pending terminal-count IRQ |
| `read_gus_irq_state` | IRQ lines, the IRQ status register with decoded flags, IRQ and latch enables, the per-voice wave and volume IRQ bitmasks, the next voice to be reported, and both timers. Unlike a port 0x246 read, this does not clear the status |
| `read_gus_register` | Read a GF1 register through the emulated I/O ports after selecting a voice |
| `gus_write_register` | Write a GF1 register through the emulated I/O ports after selecting a voice |
| `gus_set_voice_control` | Set the wave-control (0x00) and volume-control (0x0D) bytes of one voice |
| `gus_start_stop_voice` | Clear or set the Reset and Stopped bits of a voice's wave control, leaving the other bits untouched |

Positions returned by `read_gus_voices` are fixed-point in units of 1/512 of a sample, so the DRAM byte
address of a voice is `wavePos / 512`. The decoded control flags are `Reset`, `Stopped`, `Bit16`, `Loop`,
`Bidirectional`, `RaiseIrq` and `Decreasing`.

A typical inspection loop is: `read_gus_state` to confirm the GF1 is running with the DAC enabled,
`read_gus_voices` to find which voices are playing and where they point, then `read_gus_dram` at
`wavePos / 512` to look at the sample data the DOS driver uploaded.

---

## MCP Control Center

The emulator UI exposes the same catalogue under the **MCP** menu. The Control Center lists every
advertised tool with its description, lets you enable or disable tools individually (a disabled tool
returns `Tool 'x' is disabled.`), and provides a JSON-RPC playground that talks to the running server
over HTTP.

The catalogue is discovered by reflection over `EmulatorMcpTools`, so new tools appear without any UI
change. Tools are grouped into categories derived from their names (`Gravis UltraSound`, `Sound Blaster`,
`OPL / AdLib`, `PC Speaker`, `MIDI`, `Video`, `Input`, `Memory`, `CPU`, `Execution`, `DOS`, `BIOS`,
`EMS`, `XMS`, `Breakpoints`, `Functions`, `I/O Ports`, `Server`). Use the category selector together with
the text filter to narrow the list, then **Enable shown** / **Disable shown** to toggle the visible subset,
or **Enable all** / **Disable all** for the whole catalogue.

---

## Tool Invocation & Auto-Pause

Most tools automatically **pause** the emulator before execution and **resume** after. This ensures consistent state during inspection and prevents race conditions. A few tools (marked with `[McpManualControl]`) skip auto-pause and require the client to explicitly call `pause_emulator` if needed.

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
| -------- | --------- |
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
4. Call `add_breakpoint` to pause at a suspect function.
5. Call `list_functions` to see which functions are called most often.
6. Call `screenshot` to see the current video output.

### Automated Testing

A test script can:

1. Call `pause_emulator` to halt execution.
2. Call `write_memory` to inject test data.
3. Call `resume_emulator` and wait for a breakpoint.
4. Call `read_dos_program_state` to verify the loaded process.
5. Call `screenshot` and compare against a baseline image.

### Game Trainer / Cheat Tool

A trainer tool can:

1. Call `read_dos_program_state` to locate the game's PSP and environment.
2. Call `search_memory` to find the player's health value.
3. Call `write_memory` to set health to max.
4. Call `add_breakpoint` with type `MEMORY_WRITE` to detect when the game decrements health.

### Live Debugging Dashboard

A web dashboard can:

1. Poll `read_cpu_state` every 500ms to display registers.
2. Call `read_video_state` to show current video mode.
3. Call `read_sound_blaster_state` and `read_gus_state` to visualize audio device state.
4. Call `list_functions` to show a live call-count heatmap.

---

## Logs & Troubleshooting

- **MCP server log:** `logs/mcp.log` (warning level by default)
- **Emulator logs:** Console or file (controlled by `--VerboseLogs`, `--WarningLogs`, `--SilencedLogs`)
- **Health check:** `GET http://localhost:8081/health` should return `{"status":"ok","service":"Spice86 MCP Server"}`

**Common issues:**

| Problem | Solution |
| --------- | ---------- |
| Client gets 404 on `/mcp` | Check that `--McpHttpPort` is non-zero and the server started successfully |
| Tools return "Tool 'x' is disabled." | The tool was switched off in the MCP Control Center; re-enable it there |
| Tools return "... is not available" | The underlying device is not present in this configuration (for example the GUS with `--GusEnable false`) |
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
- `Spice86/ViewModels/McpStatusViewModel.cs`, `Spice86/Views/McpToolsView.axaml` - MCP Control Center UI

### Further Reading

- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [Spice86 CFG CPU Documentation](cfgcpuReadme.md)
- [Spice86 Internal Debugger Wiki](https://github.com/OpenRakis/Spice86/wiki/Spice86-internal-debugger)
- [Cryogenic Project (MCP Extension Example)](https://github.com/OpenRakis/Cryogenic)

**Quick links:**

- Health check: `http://localhost:8081/health`
- MCP endpoint: `http://localhost:8081/mcp`
- Tool discovery: Call `tools/list` or `mcp_about`
