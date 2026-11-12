---
name: spice86-mcp-debug
description: "Workflow for debugging Spice86 programs using the MCP HTTP server: CFG analysis, function discovery, breakpoints, live CPU/memory inspection, and step-through debugging. Use when: analyzing program control flow, setting breakpoints on specific addresses, inspecting emulator state programmatically, stepping through code via MCP tools, or investigating a bug by tracing execution."
---

# Spice86 MCP Debugging Workflow

## Prerequisites

Start Spice86 with MCP enabled (default port 8081):

```bash
dotnet run --project src/Spice86 -- -e program.exe
# Or disable default servers for parallel runs:
dotnet run --project src/Spice86 -- -e program.exe --HttpApiPort 0 --GdbPort 0
```

Verify MCP is running:
```bash
curl http://localhost:8081/health
# Expected: {"status":"ok"}
```

## MCP Protocol Basics

All tools use JSON-RPC 2.0 over HTTP POST to `http://localhost:8081/mcp`:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "read_cpu_state",
    "arguments": {}
  }
}
```

Most tools auto-pause the emulator before execution and resume after. The step family (`step`, `step_over`, `go`) is manual control — you drive pause/resume yourself.

## MCP HTTP Call Protocol (READ BEFORE FIRST CALL)

These rules were discovered the hard way against a live Spice86 instance and are
mandatory. `/health` and `tools/list` work without them, so a green health
check does **not** mean `tools/call` will work.

1. **Handshake first, same connection.** Before any `tools/call`, send on the
   SAME keep-alive HTTP connection, in order:
   - `{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"cli","version":"1.0"}}}`
   - `{"jsonrpc":"2.0","method":"notifications/initialized"}`
   Without both, every `tools/call` fails with
   `{"error":{"code":-32603,"message":"An error occurred."}}`.
2. **Headers.** `Content-Type: application/json` and `Accept` must list both
   `application/json` and `text/event-stream` (e.g. `Accept: application/json, text/event-stream`).
   Missing `text/event-stream` → HTTP 406.
3. **Response is SSE.** The body looks like
   `event: message\r\ndata: {…json…}`. Strip the `data:` line, then parse the JSON.
4. **Payload location.** The tool's actual result is under
   `result.structuredContent` (an object), NOT at the top level of `result`.
   `result.content[]` also carries text content (e.g. `screenshot` returns the
   PNG as base64 in `result.content[0].data`, `mimeType: image/png`).
   Errors come back as a top-level `error` object.
5. **PowerShell gotchas** (each tool call must be in ONE process that also runs
   the handshake, since `Invoke-WebRequest` connections don't persist across
   calls in separate shells):
   - Do not name a helper function `Do` (PowerShell reserved — parser error).
   - The 3-arg `StringContent($json, [Encoding]::UTF8, "application/json")`
     constructor throws "The format of value ... is invalid" on PS 5.1 — use the
     2-arg form `New-Object StringContent($json, "application/json")`.

### Verified working PowerShell helper (one process)

```powershell
$ErrorActionPreference = "Stop"
$url = "http://localhost:8081/mcp"
$hdrs = @{ "Accept" = "application/json, text/event-stream" }
$client = New-Object System.Net.Http.HttpClient
$client.DefaultRequestHeaders.Accept.Add([System.Net.Http.HttpMediaTypeWithQualityHeaderValue]::new("application/json"))
$client.DefaultRequestHeaders.Accept.Add([System.Net.Http.HttpMediaTypeWithQualityHeaderValue]::new("text/event-stream"))

function Send-Rpc($body, $wantResponse) {
  $content = New-Object System.Net.Http.StringContent($body, "application/json")
  $resp = $client.PostAsync($url, $content).GetAwaiter().GetResult()
  $text = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
  if ($text -like "data: *") { $text = $text -replace '^data:\s*', '' }
  if (-not $resp.IsSuccessStatusCode) { throw "$($resp.StatusCode) : $text" }
  if ($wantResponse) { $text | ConvertFrom-Json } else { $text }
}

function Mcp-Call($name, $argsObj) {
  $argsJson = if ($argsObj) { $argsObj | ConvertTo-Json -Depth 8 -Compress } else { "{}" }
  $send = @{ jsonrpc="2.0"; id=1; method="tools/call";
            params=@{ name=$name; arguments=(@{} + $argsObj) } } | ConvertTo-Json -Depth 8 -Compress
  $r = Send-Rpc $send $true
  if ($r.error) { throw "MCP error: $($r.error | ConvertTo-Json -Depth 5)" }
  $r.result
}

# Handshake (MUST run before the first Mcp-Call):
Send-Rpc '{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"cli","version":"1.0"}}}' $true | Out-Null
Send-Rpc '{"jsonrpc":"2.0","method":"notifications/initialized"}' $false | Out-Null

# Examples:
Mcp-Call "read_cpu_state" $null | ConvertTo-Json -Depth 4
Mcp-Call "add_breakpoint" @{ address = 96696; type = "CPU_EXECUTION_ADDRESS"; condition = $null } | ConvertTo-Json -Depth 4
```

### curl variant (handshake + call in one keep-alive command)

```bash
curl -s -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"cli","version":"1.0"}}}' \
  --next -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  --next -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"read_cpu_state","arguments":{}}}'
```

### Tool-name corrections vs older docs

| Old / wrong name | Correct name | Notes |
|---|---|---|
| `add_execution_breakpoint` | `add_breakpoint` | args: `address` (long, linear), `type`, `condition` |
| `list_execution_breakpoints` | `list_breakpoints` | — |
| `remove_execution_breakpoint` | `remove_breakpoint` | arg: `id` (string), not an index |
| `add_memory_write_breakpoint` | `add_breakpoint` | `type` = `MEMORY_WRITE` (or `MEMORY_ACCESS`/`MEMORY_READ`) |
| `pause` | `pause_emulator` | returns "Already paused" / "Paused" |
| `resume` | `resume_emulator` | returns "Already running" / "Resumed" |
| `get_pause_status` | — (no such tool) | call `pause_emulator`; "Already paused" = paused |
| `capture_screenshot` | `screenshot` | base64 PNG in `result.content[0].data` + `FilePath` |
| `read_dos_psp` | `read_dos_program_state` | PSP segment, env, drive, etc. |
| `read_dos_mcb_chain` | `read_dos_state` | closest; no MCB-chain tool exists |
| `read_dos_file_handle_table` | `read_dos_state` | device/file summary, not a full FCB table |
| `read_xms_block` | `read_xms_memory` | args: `handle`, `offset`, `length` |

Breakpoint `type` enum: `CPU_EXECUTION_ADDRESS`, `MEMORY_ACCESS`, `MEMORY_READ`,
`MEMORY_WRITE`, `IO_ACCESS`, `IO_READ`, `IO_WRITE`. `add_breakpoint` takes ONE
linear address (no range/length).

Waiting for a breakpoint hit: poll `pause_emulator`. "Already paused" = it hit;
"Paused" = was running (call `resume_emulator` and keep polling). Do NOT poll
with `read_cpu_state` (it auto-pauses a running emulator).

---

## Phase 1: CFG Analysis (Primary Workflow)

### Step 1: Discover Functions

```json
POST /mcp
{
  "jsonrpc": "2.0", "id": 1,
  "method": "tools/call",
  "params": {
    "name": "list_functions",
    "arguments": {}
  }
}
```

Returns functions sorted by call count. High-call-count functions are likely hot paths or frequently-called BIOS/DOS services.

### Step 2: Dump the Full CFG

```json
{
  "jsonrpc": "2.0", "id": 2,
  "method": "tools/call",
  "params": {
    "name": "read_cfg_cpu_graph",
    "arguments": {}
  }
}
```

Returns the full CfgCpu Control Flow Graph: nodes (basic blocks), edges (successors), and instruction details per block. Use this to understand program structure without stepping through every instruction.

### Step 3: Analyze CFG Structure

Key fields in the CFG dump:
- **Nodes**: Each basic block with its CS:IP range, parsed instructions, and call targets
- **Edges**: Successor relationships between blocks (conditional branches, calls, returns)
- **Call count**: How many times each function was called during execution
- **Speculative nodes**: If `--EnableSpeculativeCfgExploration true` (default), unobserved branches are pre-marked

### Step 4: Target Specific Functions

Run the program to exercise a code path, then re-call `list_functions` to see newly discovered functions. Compare call counts between runs to identify which paths were exercised.

---

## Phase 2: Breakpoint-Driven Debugging

### Set Execution Breakpoints

Break when CS:IP reaches a specific address. `address` is the **linear**
physical address = `segment * 16 + offset` (e.g. segment 0x1234 / offset 0x5678
→ `0x179B8 = 96696`):

```json
{
  "jsonrpc": "2.0", "id": 3,
  "method": "tools/call",
  "params": {
    "name": "add_breakpoint",
    "arguments": {
      "address": 96696,
      "type": "CPU_EXECUTION_ADDRESS",
      "condition": null
    }
  }
}
```

Returns `{Id, Address, Type, Condition, IsEnabled}`; keep the `Id` (a string
like `"1"`) for removal.

### List Active Breakpoints

```json
{
  "jsonrpc": "2.0", "id": 4,
  "method": "tools/call",
  "params": {
    "name": "list_breakpoints",
    "arguments": {}
  }
}
```

### Remove Breakpoints

```json
{
  "jsonrpc": "2.0", "id": 5,
  "method": "tools/call",
  "params": {
    "name": "remove_breakpoint",
    "arguments": {
      "id": "1"
    }
  }
}
```

`clear_breakpoints` (no args) removes all MCP-managed breakpoints.

### Memory Breakpoints (Advanced)

Break on a memory access at one linear address — useful for tracing writes to
specific data structures. Same tool, different `type` (`MEMORY_WRITE`,
`MEMORY_READ`, or `MEMORY_ACCESS` for any access). It targets ONE address —
there is no range/length:

```json
{
  "jsonrpc": "2.0", "id": 6,
  "method": "tools/call",
  "params": {
    "name": "add_breakpoint",
    "arguments": {
      "address": 10352,
      "type": "MEMORY_WRITE",
      "condition": null
    }
  }
}
```

---

## Phase 3: Live Inspection

### CPU State

```json
{
  "jsonrpc": "2.0", "id": 7,
  "method": "tools/call",
  "params": {
    "name": "read_cpu_state",
    "arguments": {}
  }
}
```

Returns all general-purpose registers (AX, BX, CX, DX, SI, DI, BP, SP), segment registers (CS, DS, ES, SS), IP, flags, and cycle count.

### Memory Inspection

Read a memory range as hex bytes:

```json
{
  "jsonrpc": "2.0", "id": 8,
  "method": "tools/call",
  "params": {
    "name": "read_memory",
    "arguments": {
      "segment": 0x07C0,
      "offset": 0,
      "length": 512
    }
  }
}
```

Write hex bytes to memory:

```json
{
  "jsonrpc": "2.0", "id": 9,
  "method": "tools/call",
  "params": {
    "name": "write_memory",
    "arguments": {
      "segment": 0x07C0,
      "offset": 0,
      "data": "EB\x3C\x90\x90"
    }
  }
}
```

Search for a hex pattern in conventional RAM:

```json
{
  "jsonrpc": "2.0", "id": 10,
  "method": "tools/call",
  "params": {
    "name": "search_memory",
    "arguments": {
      "segment": 0x0000,
      "pattern": "EB\x3C"
    }
  }
}
```

### Disassembly

```json
{
  "jsonrpc": "2.0", "id": 11,
  "method": "tools/call",
  "params": {
    "name": "read_disassembly",
    "arguments": {
      "segment": 0x1234,
      "offset": 0x5678,
      "instructionCount": 16
    }
  }
}
```

### I/O Ports

```json
{
  "jsonrpc": "2.0", "id": 12,
  "method": "tools/call",
  "params": {
    "name": "read_io_port",
    "arguments": {
      "port": 0x3D4
    }
  }
}
```

---

## Phase 4: Step-Through Debugging

### Pause & Resume

```json
{ "method": "tools/call", "params": { "name": "pause_emulator" } }
{ "method": "tools/call", "params": { "name": "resume_emulator" } }
```

`pause_emulator` returns "Already paused" or "Paused" — this is also how you
**check pause status** (there is no `get_pause_status` tool). `resume_emulator`
returns "Already running" or "Resumed".

### Step One Instruction

```json
{ "method": "tools/call", "params": { "name": "step" } }
```

After each step, inspect state:
```json
{ "method": "tools/call", "params": { "name": "read_cpu_state" } }
```

### Step Over Calls

For a CALL/INT, run until the return address. Requires `nextAddress` (the
linear address right after the call/instruction) and `isCallOrInterrupt`:

```json
{ "method": "tools/call", "params": { "name": "step_over", "arguments": { "nextAddress": 96704, "isCallOrInterrupt": true } } }
```

---

## Phase 5: DOS & BIOS Structure Inspection

### DOS Kernel State (drive, mounts, PSP segment, devices, EMS/XMS)

```json
{ "method": "tools/call", "params": { "name": "read_dos_state" } }
```

### Current Process (PSP segment, parent PSP, environment, drive, paragraphs)

```json
{ "method": "tools/call", "params": { "name": "read_dos_program_state" } }
```

### Current Directory

```json
{ "method": "tools/call", "params": { "name": "read_dos_current_directory" } }
```

> Note: there is no dedicated `read_dos_psp`, `read_dos_mcb_chain`, or
> `read_dos_file_handle_table` tool. Use `read_dos_state` and
> `read_dos_program_state` above.

---

## Phase 6: Video & Sound Inspection

### Video State

```json
{ "method": "tools/call", "params": { "name": "read_video_state" } }
{ "method": "tools/call", "params": { "name": "read_video_state_detailed" } }
{ "method": "tools/call", "params": { "name": "screenshot" } }
```

### SoundBlaster State

```json
{ "method": "tools/call", "params": { "name": "read_sound_blaster_state" } }
{ "method": "tools/call", "params": { "name": "read_sound_blaster_dsp_version" } }
```

### OPL State

```json
{ "method": "tools/call", "params": { "name": "read_opl_state" } }
```

---

## Debugging Patterns

> Every `tools/call` in these patterns needs the handshake on the same
> connection first. With curl, prepend the `--next` initialize +
> `notifications/initialized` pair shown in "MCP HTTP Call Protocol". The
> examples below show only the final `tools/call` for brevity. Every curl
> also needs `-H "Content-Type: application/json" -H "Accept: application/json, text/event-stream"`
> (missing `text/event-stream` → HTTP 406).

### Pattern 1: Trace a Suspect Function

```bash
# 1. Run program to the suspect area
# 2. Set breakpoint at entry point (linear address = 0xF000*16 + 0xA1E8 = 0xFA1E8 = 1024488)
curl -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"add_breakpoint","arguments":{"address":1024488,"type":"CPU_EXECUTION_ADDRESS","condition":null}}}'

# 3. Resume, then poll pause_emulator until it reports "Already paused"
curl -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"resume_emulator","arguments":{}}}'

# 4. Inspect state at breakpoint
curl -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"read_cpu_state","arguments":{}}}'

# 5. Step through instruction by instruction
curl -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"step","arguments":{}}}'
```

### Pattern 2: Compare CFG Before/After a Fix

```bash
# Run with build A, dump CFG
curl -X POST http://localhost:8081/mcp \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"read_cfg_cpu_graph"}}' > cfg_a.json

# Run with build B, dump CFG
curl -X POST http://localhost:8081/mcp \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"read_cfg_cpu_graph"}}' > cfg_b.json

# Diff the graphs
diff cfg_a.json cfg_b.json
```

### Pattern 3: Find All Calls to an Address

```bash
# 1. List all functions and their call targets
curl -X POST http://localhost:8081/mcp \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_functions"}}'

# 2. Dump CFG and grep for the target address in successor edges
curl -X POST http://localhost:8081/mcp \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"read_cfg_cpu_graph"}}' | rg "0x1234"
```

### Pattern 4: Trace Memory Writes to a Data Structure

```bash
# Set a write breakpoint on one linear address (0x0600*16 + 0 = 0x6000 = 24576)
# Note: one address at a time — no length parameter.
curl -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"add_breakpoint","arguments":{"address":24576,"type":"MEMORY_WRITE","condition":null}}}'

# Resume — execution pauses on first write at that address
curl -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"resume_emulator","arguments":{}}}'

# Inspect what was written and where execution stopped
curl -X POST http://localhost:8081/mcp \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"read_cpu_state"}}'
```

---

## EMS & XMS Inspection

```json
{ "method": "tools/call", "params": { "name": "read_ems_state" } }
{ "method": "tools/call", "params": { "name": "read_xms_state" } }
```

Then read specific memory (arguments per source):

```json
{ "method": "tools/call", "params": { "name": "read_ems_page_frame", "arguments": { "physicalPage": 0, "offset": 0, "length": 16 } } }
{ "method": "tools/call", "params": { "name": "read_ems_memory", "arguments": { "handle": 1, "logicalPage": 0, "offset": 0, "length": 16 } } }
{ "method": "tools/call", "params": { "name": "read_xms_memory", "arguments": { "handle": 1, "offset": 0, "length": 16 } } }
```

Handles and page counts come from `read_ems_state` / `read_xms_state`.
`search_ems_memory` / `search_xms_memory` find byte patterns the same way as `search_memory`.

---

## Verification Checklist

When using MCP for debugging:
- [ ] Verified MCP health endpoint responds
- [ ] Emulator is paused before inspection (or confirmed auto-pause worked)
- [ ] Breakpoints are removed after use (`list_breakpoints` → `remove_breakpoint`, or `clear_breakpoints`)
- [ ] `initialize` + `notifications/initialized` handshake completed on the same connection before every `tools/call`
- [ ] Tool payload read from `result.structuredContent`, not top-level `result`
- [ ] CFG dump reviewed for newly discovered functions and edges
- [ ] CPU state inspected at key decision points (flags, registers)
- [ ] Memory reads/writes verified against expected values
- [ ] Resume called when done inspecting

## Common Pitfalls

1. **Port conflicts**: Default ports are 8081 (MCP), 20000 (HTTP API), 10000 (GDB). Disable unused ones with `--HttpApiPort 0 --McpHttpPort 0 --GdbPort 0` for parallel runs.
2. **Auto-pause timing**: Most tools auto-pause, but check for `[McpManualControl]` marked tools that require explicit pause/resume.
3. **CFG not representative with overrides**: When `CodeOverridesActive` is true (code overrides in use), the CFG jumps between overrides and does NOT represent original asm flow.
4. **Missing MCP handshake**: `tools/call` fails with `-32603 An error occurred` unless `initialize` + `notifications/initialized` were sent on the same keep-alive connection first. `/health` and `tools/list` work without it, so don't use them as a call canary.
5. **Wrong tool names**: the old `add_execution_breakpoint` / `resume` / `capture_screenshot` / `get_pause_status` do not exist; use `add_breakpoint` / `resume_emulator` / `screenshot` / `pause_emulator` (see the name table above).
6. **Stateless transport**: MCP uses stateless HTTP by default — each call is independent. Don't rely on session state between calls.
7. **Address forms**: memory/DOS/BIOS tools take a `segment` + `offset` pair, but `add_breakpoint` takes ONE linear address (linear = segment * 16 + offset). Convert before calling.
8. **SSE responses**: strip the `data:` line before parsing; the payload is under `result.structuredContent`.
