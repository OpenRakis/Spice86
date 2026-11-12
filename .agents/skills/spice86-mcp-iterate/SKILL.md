---
name: spice86-mcp-iterate
description: "Iterative debugging workflow using Spice86 MCP server: start emulator, analyze CFG graph, set breakpoints, step through code, capture screenshots, restart with CFG reload disabled, repeat until the program reaches a successful visual state. Use when: debugging a DOS game/program that hangs or loops, advancing through unexplored code paths via CFG analysis, iterating with MCP breakpoints and screenshots, restarting without reloading previous CFG exploration."
---

# Spice86 Iterative MCP Debugging Workflow

## Core Loop

```
Start Spice86 (CFG reload disabled) → Wait for MCP → Analyze CFG → 
Set breakpoints on unexplored paths → Resume → Hit breakpoint → 
Inspect state → Step through → Capture screenshot → 
If not done: Restart with --ReloadCfgGraph false → Loop
```

## Prerequisites

### Start Spice86 Correctly

```bash
dotnet run --project src/Spice86 -- -e path\to\program.exe --McpHttpPort 8081 --HttpApiPort 0 --GdbPort 0 --ReloadCfgGraph false
```

**Critical flags:**
- `--ReloadCfgGraph false` — **always.** Prevents reloading previously explored CFG from disk. Each iteration starts fresh so unexplored paths are visible.
- `--McpHttpPort 8081` — enable MCP (default, but explicit is safer)
- `--HttpApiPort 0 --GdbPort 0` — disable unused servers to avoid port conflicts

### Wait for MCP Server

The MCP server takes a moment to start after Spice86 launches. Verify before proceeding:

```bash
curl http://localhost:8081/health
# Must return: {"status":"ok"}
# If it fails, wait 2-3 seconds and retry
```

---

## MCP HTTP Call Protocol (READ BEFORE FIRST CALL)

This section is distilled from a real debugging session. Ignoring it costs
30+ minutes of opaque failures.

### 1. Handshake is REQUIRED before any `tools/call`

`tools/list` and `/health` work without a handshake — **do not use them as a
canary** that `tools/call` will work. Without a handshake, every `tools/call`
fails with an opaque error:

```json
{"error":{"code":-32603,"message":"An error occurred."},"id":1,"jsonrpc":"2.0"}
```

Fix: send `initialize`, then `notifications/initialized`, then your calls,
**on the same HTTP connection** (keep-alive). The handshake is per-connection:
a fresh process/connection needs its own handshake.

### 2. Headers

- `Accept` must list **both** `application/json` **and** `text/event-stream`.
  Missing either → HTTP 406 `Not Acceptable: Client must accept both ...`.
- `Content-Type: application/json` for request bodies.

### 3. Responses are SSE

The body is `event: message\r\ndata: {json}\r\n\r\n`. Parse the `data:` line.
The payload is **not** top-level: it is under `result.structuredContent`.
Reading it top-level yields mysterious `null`s. A failing tool appears as a
top-level `error` key.

### 4. Copy-paste PowerShell helper (verified, Windows PowerShell 5.1)

Each `powershell` tool call is a **fresh process**, so the helper, the
handshake, and the calls must be in the **same command**. This exact block was
verified against a live emulator for `read_cpu_state`, `read_cfg_cpu_graph`,
`list_functions`, `read_disassembly`, `add_breakpoint`, `list_breakpoints`,
`remove_breakpoint`, and `screenshot`:

```powershell
$c = [System.Net.Http.HttpClient]::new()
$c.DefaultRequestHeaders.Add("Accept","application/json")
$c.DefaultRequestHeaders.Add("Accept","text/event-stream")
function SendRpc($p) {
  $j = $p | ConvertTo-Json -Depth 12
  $r = $c.PostAsync("http://localhost:8081/mcp", [System.Net.Http.StringContent]::new($j,"application/json")).GetAwaiter().GetResult()
  $b = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult()
  $l = ($b -split "`n" | Where-Object { $_ -like "data:*" }) -join ""
  return ($l -replace "^data:\s*","")
}
function Mcp([string]$name, [object]$a) {
  $o = (SendRpc @{ jsonrpc="2.0"; id=2; method="tools/call"; params=@{ name=$name; arguments=$a } }) | ConvertFrom-Json
  if ($o.error) { throw $o.error.message }
  return $o.result.structuredContent
}
# Handshake: exactly once per process, before the first Mcp call
SendRpc @{ jsonrpc="2.0"; id=1; method="initialize"; params=@{ protocolVersion="2025-03-26"; capabilities=@{}; clientInfo=@{name="agent";version="1"} } } | Out-Null
SendRpc @{ jsonrpc="2.0"; method="notifications/initialized"; params=@{} } | Out-Null
# ... now call tools:
$cpu = Mcp "read_cpu_state" @{}                     # $cpu.CS, $cpu.IP, $cpu.EAX, ...
$cfg = Mcp "read_cfg_cpu_graph" @{ nodeLimit = 50 } # $cfg.blocks, $cfg.entryPointAddresses, ...
$fns = Mcp "list_functions" @{ limit = 100 }        # $fns.Functions (Address, Name, CalledCount), $fns.TotalCount
$dis = Mcp "read_disassembly" @{ segment = $cpu.CS; offset = $cpu.IP; instructionCount = 10 }
$bp  = Mcp "add_breakpoint" @{ address = ($cpu.CS -shl 4) + $cpu.IP; type = "CPU_EXECUTION_ADDRESS"; condition = $null }
```

PowerShell 5.1 gotchas hit during the session:
- Do NOT name a function `Do` — reserved keyword, parser error.
- `StringContent(string, Encoding, string)` 3-arg ctor throws "The format of
  value ... is invalid" in PS 5.1. Use the 2-arg form `new($json,"application/json")`.

### 5. Verified tool names (doc/mcp.md and older skill text use WRONG names)

| Wrong (old docs) | Correct name | Arguments |
|---|---|---|
| `add_execution_breakpoint` | `add_breakpoint` | `address` (linear physical, long), `type`, `condition` (string or null) |
| `list_execution_breakpoints` | `list_breakpoints` | — |
| `remove_execution_breakpoint` | `remove_breakpoint` | `id` (string, e.g. `"1"`) |
| `add_memory_write_breakpoint` | `add_breakpoint` | `type = "MEMORY_WRITE"`, `address` linear |
| `capture_screenshot` | `screenshot` | — |
| `resume` | `resume_emulator` | — (alias: `go`) |
| `pause` | `pause_emulator` | — |
| `get_pause_status` | does not exist | poll `pause_emulator` instead |

Breakpoint `type` values: `CPU_EXECUTION_ADDRESS`, `MEMORY_ACCESS`,
`MEMORY_READ`, `MEMORY_WRITE`, `IO_ACCESS`, `IO_READ`, `IO_WRITE`.
`clear_breakpoints` removes all MCP-managed breakpoints.

### 6. Response payload notes (verified)

- `screenshot`: `result.content[0]` is `{type:"image", data:<base64 PNG>,
  mimeType:"image/png"}`; `result.structuredContent` has `Width`, `Height`,
  and `FilePath` (written under `%TEMP%\spice86-mcp-screenshots`).
- `pause_emulator` returns `Message` = `"Already paused"` or `"Paused"`.
  **This is the safe way to wait for a breakpoint hit**: poll it while the
  emulator runs; `"Already paused"` means a breakpoint fired (no state change);
  `"Paused"` means it was still running (resume and keep polling). Polling
  `read_cpu_state` instead would itself pause a running emulator and break the
  wait.
- `resume_emulator` returns `"Already running"` or `"Resumed"`.
- Most tools auto-pause the emulator before running and auto-resume after;
  if the emulator is already paused, it stays paused. `pause_emulator`,
  `resume_emulator`, `go`, `step`, `step_over` are manual-control (no
  auto-pause).

### 7. Curl variant (single keep-alive connection via `--next`)

```bash
curl -sS --no-buffer http://localhost:8081/mcp \
  -H "Accept: application/json, text/event-stream" -H "Content-Type: application/json" \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"agent","version":"1"}}}' \
  --next --data '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}' \
  --next --data '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"read_cpu_state","arguments":{}}}'
```

---

## Phase 1: CFG Analysis (Start of Each Iteration)

### Step 1: List Discovered Functions

```json
POST http://localhost:8081/mcp
Content-Type: application/json

{
  "jsonrpc": "2.0", "id": 1,
  "method": "tools/call",
  "params": { "name": "list_functions", "arguments": {} }
}
```

Returns functions sorted by call count. Focus on **zero-call-count functions** — these are unexplored code paths.

### Step 2: Dump the Full CFG

```json
{
  "jsonrpc": "2.0", "id": 2,
  "method": "tools/call",
  "params": { "name": "read_cfg_cpu_graph", "arguments": {} }
}
```

Returns the complete CfgCpu Control Flow Graph: nodes (basic blocks with CS:IP ranges and parsed instructions), edges (successor relationships), and per-function call counts.

### Step 3: Identify Unexplored Paths

From the CFG dump, find:
- **Nodes with zero incoming edges** — unreachable from current execution
- **Edges leading to `FailAsUntested` blocks** — speculative nodes not yet observed
- **Functions with zero call count** — never entered during this run
- **Conditional branches that always took the same arm** — check if both successors were explored

### Step 4: Set Breakpoints on Unexplored Paths

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

`address` is a **linear physical address** (`segment * 16 + offset`, e.g.
segment 0x1234 / offset 0x5678 → `0x1234 * 16 + 0x5678 = 0x179B8 = 96696`),
not a segment/offset pair. `type` is one of `CPU_EXECUTION_ADDRESS`,
`MEMORY_ACCESS`, `MEMORY_READ`, `MEMORY_WRITE`, `IO_ACCESS`, `IO_READ`,
`IO_WRITE`. Returns `{Id, Address, Type, Condition, IsEnabled}`; keep the
`Id` (a string like `"1"`) for `remove_breakpoint`.

Set breakpoints at the entry of unexplored functions or at conditional branch targets that were not taken.

---

## Phase 2: Execution & Inspection

### Step 5: Resume Execution, Then Wait for the Breakpoint

```json
{
  "jsonrpc": "2.0", "id": 4,
  "method": "tools/call",
  "params": { "name": "resume_emulator", "arguments": {} }
}
```

Execution continues until a breakpoint is hit or the program completes.
Wait for the hit by **polling `pause_emulator`** with a short sleep between
calls (each call still needs the handshake on its connection — see the
protocol section):

- `Message: "Already paused"` → a breakpoint fired; proceed to Step 6.
- `Message: "Paused"` → it was still running; call `resume_emulator` again and
  keep polling.

Do NOT poll with `read_cpu_state`: it auto-pauses a running emulator and
would destroy the wait.

### Step 6: On Breakpoint Hit — Inspect State

```json
{
  "jsonrpc": "2.0", "id": 5,
  "method": "tools/call",
  "params": { "name": "read_cpu_state", "arguments": {} }
}
```

Check registers, flags, and IP to understand what code path was taken.

### Step 7: Inspect Memory at Key Addresses

```json
{
  "jsonrpc": "2.0", "id": 6,
  "method": "tools/call",
  "params": {
    "name": "read_memory",
    "arguments": {
      "segment": 0x0600,
      "offset": 0,
      "length": 256
    }
  }
}
```

Read memory at addresses relevant to the bug (e.g., game state variables, screen buffers, interrupt vectors).

### Step 8: Disassemble Around Current IP

```json
{
  "jsonrpc": "2.0", "id": 7,
  "method": "tools/call",
  "params": {
    "name": "read_disassembly",
    "arguments": {
      "segment": 0x1234,
      "offset": 0x5678,
      "instructionCount": 32
    }
  }
}
```

See what instructions are at and after the current IP to plan the next step.

### Step 9: Step Through Instructions

```json
{
  "jsonrpc": "2.0", "id": 8,
  "method": "tools/call",
  "params": { "name": "step", "arguments": {} }
}
```

After each step, re-inspect CPU state and memory to trace the effect of each instruction.

### Step 10: Capture Screenshot

The tool is named **`screenshot`** (not `capture_screenshot`).

```json
{
  "jsonrpc": "2.0", "id": 9,
  "method": "tools/call",
  "params": { "name": "screenshot", "arguments": {} }
}
```

The PNG is returned inline: `result.content[0].data` is base64 with
`mimeType: image/png`. `result.structuredContent.FilePath` also points to a
copy under `%TEMP%\spice86-mcp-screenshots` with `Width`/`Height`. Use this
to verify visual progress toward the game's successful state.

---

## Phase 3: Decision & Restart

### Step 11: Evaluate Progress

Check the screenshot against the target (successful game state). Ask:
- **Is the game further along?** → Continue stepping, set more breakpoints on remaining unexplored paths
- **Is the game stuck in a loop?** → Find the loop in the CFG, set a breakpoint at its entry, restart with fresh CFG
- **Did we reach the target?** → Done

### Step 12: Restart for New Iteration

If not done, close Spice86 and restart with `--ReloadCfgGraph false`:

```bash
# Kill the running Spice86 process (Ctrl+C in the terminal)

# Restart fresh (no previous CFG)
dotnet run --project src/Spice86 -- -e path\to\program.exe --McpHttpPort 8081 --HttpApiPort 0 --GdbPort 0 --ReloadCfgGraph false
```

**Wait for MCP health check again:**
```bash
curl http://localhost:8081/health
```

### Step 13: Repeat from Phase 1

Each restart gives a fresh CFG. Set new breakpoints based on what you learned in the previous iteration. The goal is to progressively explore unexplored code paths until the game reaches its successful state.

---

## Red Herring Filtering

**Ignore these logs — they are noise during iterative debugging:**
- "Program is not well behaved" — common for DOS games that use undocumented behavior
- "Unimplemented interrupt" warnings — if the program doesn't visibly hang on them, skip them
- Any log output — center exclusively on CFG graph analysis and screenshots

**Focus only on:**
1. CFG graph structure (nodes, edges, call counts)
2. Breakpoint hits at unexplored paths
3. Screenshot visual progress

---

## Iteration Strategy

### When the Game Loops Forever

1. Dump CFG and find cycles (nodes that can reach themselves)
2. Set a breakpoint at the cycle entry point
3. Resume — when hit, inspect what condition keeps the loop going
4. Find the loop-continuation condition in memory or registers
5. Restart with `--ReloadCfgGraph false`, set a breakpoint before the loop condition is evaluated

### When New Code Paths Appear After Each Run

1. Compare CFG between runs (dump both, diff them)
2. Identify newly discovered nodes/edges
3. Set breakpoints at entries of newly discovered functions
4. Resume and trace through the new paths

### When the Game Hangs on a Specific Screen

1. Capture screenshot of the hung state
2. Dump CFG — find which function has the highest call count (likely the hang loop)
3. Set breakpoint at that function's entry
4. Step through and check if any screen-drawing or input-handling code is being skipped
5. Restart, set breakpoints on the unexplored branches of that function

---

## Complete Example Session

Every `tools/call` needs the MCP handshake on the same connection (see
"MCP HTTP Call Protocol"). With curl, `--next` keeps the connection open, so
one command = initialize + notification + call:

```bash
# === ITERATION 1 ===

# 1. Start Spice86 (always fresh CFG)
dotnet run --project src/Spice86 -- -e C:\doom\DOOM1.WAD --McpHttpPort 8081 --HttpApiPort 0 --GdbPort 0 --ReloadCfgGraph false

# 2. Wait for MCP (retry until ok)
curl http://localhost:8081/health

# 3. List functions (handshake + call in one keep-alive connection)
curl -s -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"cli","version":"1.0"}}}' \
  --next -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  --next -X POST http://localhost:8081/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_functions","arguments":{}}}'

# 4. Dump CFG (same handshake prefix, then name: read_cfg_cpu_graph)
#    > cfg_iter1.json — strip "data: " SSE prefixes before parsing JSON

# 5. Find zero-call functions from the dump, set a breakpoint on one
#    (same handshake prefix, then:)
#    name: add_breakpoint, arguments: {"address": 48938, "type": "CPU_EXECUTION_ADDRESS", "condition": null}

# 6. Resume (same handshake prefix, then name: resume_emulator)

# 7. Poll for the hit (separate handshake + call each time, sleep 1s between):
#    name: pause_emulator — "Already paused" = hit; "Paused" = still running

# 8. Inspect (name: read_cpu_state), step (name: step)

# 9. Screenshot (name: screenshot) — base64 PNG in result.content[0].data,
#    file copy in result.structuredContent.FilePath

# 10. Not done? Restart.
# Ctrl+C to stop Spice86, then repeat from step 1 with --ReloadCfgGraph false
```

---

## Verification Checklist

After each iteration:
- [ ] MCP health endpoint confirmed responding before proceeding
- [ ] `initialize` + `notifications/initialized` handshake completed on the same connection before every `tools/call` (no `An error occurred.` failures)
- [ ] Tool payload read from `result.structuredContent`, not top-level `result`
- [ ] CFG dump analyzed for unexplored paths (zero-call functions, unvisited nodes)
- [ ] Breakpoints set on unexplored code paths
- [ ] Screenshot captured and compared to target state
- [ ] `--ReloadCfgGraph` chosen explicitly (not left to default) based on whether you need fresh or accumulated CFG
- [ ] HTTP API and GDB servers disabled (`--HttpApiPort 0 --GdbPort 0`)
- [ ] Red herring logs ignored (unimplemented interrupts, "not well behaved")

## Common Pitfalls

1. **Wrong ReloadCfgGraph value** — `false` when you want to accumulate exploration, or missing it when you need a clean slate. Choose explicitly based on the table above.
2. **Not waiting for MCP** — sending requests before the server is ready causes errors. Always check `/health` first.
3. **Missing MCP handshake** — `tools/call` fails with `-32603 An error occurred` unless `initialize` and `notifications/initialized` were sent on the same keep-alive connection first. `/health` and `tools/list` work without it, so do not use them as a call canary.
4. **Wrong tool names** — the old `add_execution_breakpoint` / `resume` / `capture_screenshot` do not exist; the real names are `add_breakpoint`, `resume_emulator`, `screenshot` (full table in the protocol section).
5. **Segment/offset for breakpoints** — `add_breakpoint` takes one linear `address`, not a `segment`/`offset` pair.
6. **Port conflicts** — if another Spice86 instance is running, ports 8081/20000/10000 may be in use. Disable unused servers.
7. **Focusing on logs instead of CFG** — unimplemented interrupt warnings are noise if the program doesn't visibly hang. Center on CFG structure and screenshots.
8. **Not capturing screenshots** — visual progress is the ultimate oracle. Capture at every decision point.
