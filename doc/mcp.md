# Spice86 MCP HTTP server

## Overview

Spice86 exposes a Model Context Protocol (MCP) server over HTTP.
It can be used to inspect emulator state and drive execution through structured tool calls.

## Endpoints

- MCP endpoint: <http://localhost>:<port>/mcp
- Health endpoint: <http://localhost>:<port>/health

Default port is 8081.
Use --mcp-http-port to change it.

## Quick start

Start Spice86 with MCP enabled (enabled by default):

```bash
Spice86 -e program.exe --mcp-http-port 8081
```

Then connect your MCP client to:

- <http://localhost:8081/mcp>

## Protocol behavior

- Transport mode is stateless.
- The server does not issue Mcp-Session-Id.
- Standard MCP flow is supported: initialize, tools/list, tools/call.

## Tooling scope

The built-in toolset covers emulator-oriented operations such as:

- CPU and execution state inspection
- Memory read/write/search
- Pause, resume, step, and step-over control
- Breakpoint management
- Video state and screenshot access
- EMS and XMS inspection helpers
- Input automation helpers

## Extending MCP from user projects

Spice86 supports external MCP tool registration.

McpHttpHost.Start accepts:

- additionalToolAssemblies
- additionalServices

Additional assemblies are scanned for classes marked with McpServerToolType.
Additional services are registered for constructor injection in those tool classes.

### Minimal setup

```csharp
using Spice86.Core.Emulator.Mcp;

McpHttpHost host = new(loggerService);
host.Start(
    services: emulatorMcpServices,
    port: configuration.McpHttpPort,
    additionalToolAssemblies: [typeof(MyProjectMcpTools).Assembly],
    additionalServices: [new MyProjectMcpContext(...)]);
```

```csharp
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class MyProjectMcpTools {
    [McpServerTool(Name = "my_project_ping", UseStructuredContent = true)]
    public object Ping() {
        return new { success = true, message = "pong" };
    }
}
```

## Important startup note

Current default Spice86 startup initializes MCP with built-in tools only.
In the default dependency wiring, McpHttpHost.Start is called without additional assemblies or services.

To expose project-specific tools, use a custom startup path (or patch startup wiring) and pass your extension assemblies and services.

## Practical guidance for extension authors

- Keep tool behavior deterministic.
- Prefer semantic tools for common operations.
- Keep low-level tools available for fallback diagnostics.
- Return compact structured payloads.
- Add integration tests that execute real MCP tools/calls.
