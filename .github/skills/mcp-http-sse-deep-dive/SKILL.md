---
name: mcp-http-sse-deep-dive
description: 'Code-first deep-dive workflow for using Spice86 MCP over HTTP and SSE-oriented clients without transport mistakes. Use when you need hard facts from source code, reliable handshake flow, and verified input/output shapes.'
argument-hint: 'Target host/port, client type, and workflow goal (diagnostics, automation, or integration).'
user-invocable: true
disable-model-invocation: false
---

# Spice86 MCP HTTP and SSE Deep Dive

## What This Skill Produces

This skill produces a reliable MCP connection and call workflow for Spice86 with:
1. A hard-facts table extracted from source code before any client debugging.
2. Deterministic request/response examples for initialize, tools/list, tools/call, and notifications/initialized.
3. Transport troubleshooting branches for common HTTP and SSE-client mismatches.
4. A completion checklist that confirms both connectivity and payload correctness.

## When to Use

Use this skill when you need one of these outcomes:
1. Connect an MCP client to Spice86 over HTTP with minimal trial and error.
2. Make an SSE-oriented MCP client work against a stateless HTTP MCP server.
3. Verify expected MCP input and output JSON shapes before automation work.
4. Avoid recurring mistakes by resolving facts directly from code instead of assumptions.
5. Debug failures such as missing session headers, bad tool arguments, empty tool lists, or non-deterministic tool payload assumptions.

## Prerequisites

1. Spice86 is running with MCP enabled.
2. You know the MCP HTTP port (default: 8081).
3. You have endpoint access to:
- http://localhost:<port>/health
- http://localhost:<port>/mcp

## Hard Facts First (Mandatory)

Before any diagnosis, resolve these facts from code and treat them as source of truth:
1. Endpoint mapping and health payload:
- `src/Spice86.Core/Emulator/Mcp/McpHttpHost.cs`
- `/mcp` is mapped via `MapMcp("/mcp")`.
- `/health` returns JSON with `status` and `service`.
2. Transport behavior:
- `src/Spice86.Core/Emulator/Mcp/McpHttpHost.cs`
- HTTP transport is explicitly configured stateless (`options.Stateless = true`).
3. Tool shape behavior:
- `src/Spice86.Core/Emulator/Mcp/EmulatorMcpTools.cs`
- Success path returns `CallToolResult.StructuredContent`.
- Error path sets `IsError = true`, includes `StructuredContent`, and text `Content`.
4. Client-side SSE compatibility behavior in Spice86 UI:
- `src/Spice86/ViewModels/McpStatusViewModel.cs`
- Requests include `Accept: application/json` and optionally `text/event-stream`.
- UI normalizes SSE payloads by extracting the latest `data:` line.
- UI still sends `notifications/initialized` after initialize.

If your observed behavior conflicts with these facts, fix environment/proxy/client assumptions before changing payloads.

## Transport Reality Check (Important)

Spice86 MCP is implemented as stateless HTTP transport:
1. Transport mode is stateless.
2. The server does not rely on Mcp-Session-Id for correctness.
3. Standard MCP flow is initialize, tools/list, tools/call.

Decision point:
1. If your client supports stateless HTTP MCP, use it directly.
2. If your client is SSE-first and assumes server sessions, force stateless mode if available.
3. If your client strictly requires SSE session semantics, use a bridge/proxy layer that adapts SSE expectations to HTTP stateless MCP.

## Step-by-Step Procedure

### 1. Preflight Health and Endpoint Check

1. Call GET /health and require HTTP 200.
2. Confirm /mcp is reachable and not blocked by firewall or reverse proxy policy.

Expected health output shape:

{
  "status": "ok",
  "service": "Spice86 MCP Server"
}

Acceptance criteria:
1. Health endpoint returns success.
2. MCP endpoint is reachable from the same client runtime context.

### 2. Initialize Flow

Send an initialize request to /mcp.

Expected input shape:

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2024-11-05",
    "capabilities": {},
    "clientInfo": {
      "name": "my-client",
      "version": "1.0.0"
    }
  }
}

Expected output shape:

{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "2024-11-05",
    "capabilities": { ... },
    "serverInfo": {
      "name": "...",
      "version": "..."
    }
  }
}

Troubleshooting branch:
1. If client errors on missing Mcp-Session-Id, disable session requirement in client transport settings.
2. If protocolVersion mismatch occurs, align to the version accepted by client and server pair and retry.

### 3. Send notifications/initialized (Compatibility-Safe)

Although stateless mode is enabled, some clients and intermediaries behave better when the standard initialized notification is sent.

Expected input shape:

{
  "jsonrpc": "2.0",
  "method": "notifications/initialized",
  "params": {}
}

Expected output shape:

{}

### 4. Discover Tools Deterministically

Send tools/list after initialize.

Expected input shape:

{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/list",
  "params": {}
}

Expected output shape:

{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "tools": [
      {
        "name": "mcp_about",
        "description": "...",
        "inputSchema": {
          "type": "object",
          "properties": { ... },
          "required": [ ... ]
        }
      }
    ]
  }
}

Acceptance criteria:
1. Tool list is non-empty.
2. Each tool has name, description, and input schema.

### 5. Probe Server Metadata via mcp_about

Send tools/call for mcp_about.

Expected input shape:

{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "mcp_about",
    "arguments": {}
  }
}

Expected output shape:

{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "..."
      }
    ],
    "structuredContent": {
      "server": { ... },
      "capabilities": { ... },
      "discovery": ["initialize", "tools/list", "mcp_about"]
    }
  }
}

Decision point:
1. If your automation needs stable parsing, prefer structuredContent.
2. If structuredContent is absent for a tool, parse text content defensively.

### 6. Validate Tool Argument and Result Shapes

1. Read each tool inputSchema from tools/list.
2. Build arguments that satisfy required keys and basic types.
3. Reject or fix requests with unknown keys when strict validation fails.

Generic tools/call request shape:

{
  "jsonrpc": "2.0",
  "id": 10,
  "method": "tools/call",
  "params": {
    "name": "<tool-name>",
    "arguments": {
      "<arg1>": "<value1>"
    }
  }
}

Generic success shape for Spice86 tools:

{
  "structuredContent": {
    "...": "..."
  }
}

Generic error shape for Spice86 tools:

{
  "isError": true,
  "structuredContent": {
    "success": false,
    "message": "..."
  },
  "content": [
    {
      "type": "text",
      "text": "..."
    }
  ]
}

Hard-fact schema examples from code:
1. `read_memory` arguments:

{
  "segment": 4660,
  "offset": 22136,
  "length": 64
}

2. `read_memory` structured result:

{
  "Address": {
    "Segment": 4660,
    "Offset": 22136
  },
  "Length": 64,
  "Data": "B80000..."
}

3. `read_interrupt_vector` arguments:

{
  "interruptNumber": 33
}

4. `read_interrupt_vector` structured result shape:

{
  "InterruptNumber": 33,
  "Address": {
    "Segment": 61440,
    "Offset": 61752
  }
}

### 7. SSE-Oriented Client Compatibility Path

Use this path only when your MCP client stack is SSE-oriented.

1. Try HTTP stateless mode first if client supports both SSE and HTTP.
2. If client enforces SSE session semantics, configure adaptation:
- Disable mandatory session header checks.
- Keep request IDs deterministic.
- Keep retry policy idempotent for safe replays.
3. If adaptation is not possible, place a transport bridge in front of Spice86 MCP.

Bridge acceptance criteria:
1. initialize, tools/list, and tools/call complete end-to-end.
2. No synthetic session IDs are required by downstream server.
3. Tool call payloads are unchanged semantically after adaptation.

SSE hard-fact note:
1. Spice86 UI MCP tester accepts event-stream responses and extracts the latest SSE `data:` line.
2. This proves SSE framing can appear in client workflows, even though server transport is configured stateless HTTP.

## Troubleshooting Matrix

1. Symptom: initialize succeeds once, then client requires session header.
- Cause: client assumes stateful SSE/session transport.
- Fix: enforce stateless mode or use bridge adapter.

2. Symptom: tools/list returns empty tools.
- Cause: wrong endpoint, proxy path rewrite, or startup not fully initialized.
- Fix: verify /mcp path, check Spice86 startup args, retry after startup stabilization.

3. Symptom: tools/call fails with invalid params.
- Cause: arguments shape mismatches inputSchema.
- Fix: derive request payload strictly from inputSchema required/properties.

4. Symptom: intermittent failures behind reverse proxy.
- Cause: buffering, timeout, or body-size constraints.
- Fix: increase timeout, disable problematic buffering, verify JSON body passthrough.

5. Symptom: tools/call succeeds but parsing fails in automation.
- Cause: automation expects JSON-RPC envelope while client SDK surfaces CallToolResult payload only.
- Fix: parse the SDK return type first, then parse `structuredContent` and fallback `content`.

## Completion Checklist

1. Health endpoint validated.
2. initialize returns serverInfo and capabilities.
3. notifications/initialized sent after initialize.
4. tools/list returns non-empty tools with inputSchema.
5. mcp_about call succeeds.
6. At least one additional tool call succeeds with schema-valid arguments.
7. For SSE-oriented clients, either direct stateless mode or bridge adaptation is proven.
8. Input/output payload samples are captured and reusable in automation.
9. Hard-facts table has at least one citation in each category: endpoint mapping, transport mode, and tool payload shape.

## References

1. Spice86 MCP docs: [doc/mcp.md](../../../doc/mcp.md)
2. Host implementation: [McpHttpHost.cs](../../../src/Spice86.Core/Emulator/Mcp/McpHttpHost.cs)
3. Tool implementations: [EmulatorMcpTools.cs](../../../src/Spice86.Core/Emulator/Mcp/EmulatorMcpTools.cs)
4. UI MCP tester behavior: [McpStatusViewModel.cs](../../../src/Spice86/ViewModels/McpStatusViewModel.cs)
