# Spice86 MCP Server

## Overview

The Spice86 MCP (Model Context Protocol) Server exposes emulator state inspection capabilities to AI models and applications via standard I/O (stdio) transport. MCP is a standardized protocol introduced by Anthropic that enables AI models to interact with external tools and resources in a consistent way.

The server uses stdio transport (reading JSON-RPC requests from stdin, writing responses to stdout), which is the standard transport mechanism for MCP servers. This enables external tools and AI models to communicate with the emulator through standard input/output streams.

## Features

The MCP server provides four tools for inspecting the emulator state:

### 1. Read CPU Registers (`read_cpu_registers`)

Retrieves the current values of all CPU registers, including:
- General purpose registers (EAX, EBX, ECX, EDX, ESI, EDI, ESP, EBP)
- Segment registers (CS, DS, ES, FS, GS, SS)
- Instruction pointer (IP)
- CPU flags (Carry, Parity, Auxiliary, Zero, Sign, Direction, Overflow, Interrupt)

**Usage:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_cpu_registers",
    "arguments": {}
  },
  "id": 1
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [{
      "type": "text",
      "text": "{
        \"generalPurpose\": {
          \"EAX\": 305419896,
          \"EBX\": 2882400001,
          ...
        },
        \"segments\": {
          \"CS\": 4096,
          ...
        },
        \"instructionPointer\": {
          \"IP\": 256
        },
        \"flags\": {
          \"CarryFlag\": false,
          ...
        }
      }"
    }]
  }
}
```

### 2. Read Memory (`read_memory`)

Reads a range of bytes from the emulator's memory.

**Parameters:**
- `address` (integer, required): The starting linear memory address
- `length` (integer, required): The number of bytes to read (maximum 4096)

**Usage:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_memory",
    "arguments": {
      "address": 4096,
      "length": 16
    }
  },
  "id": 2
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [{
      "type": "text",
      "text": "{
        \"address\": 4096,
        \"length\": 16,
        \"data\": \"0102030405060708090A0B0C0D0E0F10\"
      }"
    }]
  }
}
```

### 3. List Functions (`list_functions`)

Lists known functions from the function catalogue, ordered by call count (most frequently called first).

**Parameters:**
- `limit` (integer, optional): Maximum number of functions to return (default: 100)

**Usage:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "list_functions",
    "arguments": {
      "limit": 10
    }
  },
  "id": 3
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [{
      "type": "text",
      "text": "{
        \"functions\": [
          {
            \"address\": \"1000:0000\",
            \"name\": \"MainFunction\",
            \"calledCount\": 42,
            \"hasOverride\": false
          },
          ...
        ],
        \"totalCount\": 125
      }"
    }]
  }
}
```

### 4. Read CFG CPU Graph (`read_cfg_cpu_graph`)

Inspects the Control Flow Graph CPU state, providing insights into the dynamic CFG construction during emulation. This tool is **only available when CFG CPU is enabled** (use `--CfgCpu` command-line flag).

**About CFG CPU:**
The CFG CPU builds a dynamic Control Flow Graph during execution, tracking:
- Instruction execution flow and relationships
- Self-modifying code as graph branches
- Execution contexts for hardware interrupts
- Entry points for different execution contexts

See [`doc/cfgcpuReadme.md`](cfgcpuReadme.md) for detailed CFG CPU architecture documentation.

**Parameters:** None

**Usage:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_cfg_cpu_graph",
    "arguments": {}
  },
  "id": 4
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "result": {
    "content": [{
      "type": "text",
      "text": "{
        \"currentContextDepth\": 0,
        \"currentContextEntryPoint\": \"F000:FFF0\",
        \"totalEntryPoints\": 42,
        \"entryPointAddresses\": [
          \"F000:FFF0\",
          \"F000:E05B\",
          \"0000:7C00\",
          ...
        ],
        \"lastExecutedAddress\": \"1000:0234\"
      }"
    }]
  }
}
```

**Response Fields:**
- `currentContextDepth`: Execution context nesting level (0 = initial, higher = interrupt contexts)
- `currentContextEntryPoint`: Entry point address of current execution context
- `totalEntryPoints`: Total number of CFG graph entry points across all contexts
- `entryPointAddresses`: Array of all entry point addresses in the CFG
- `lastExecutedAddress`: Address of the most recently executed instruction

**Error Response (when CFG CPU not enabled):**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "error": {
    "code": -32603,
    "message": "Tool execution error: CFG CPU is not enabled. Use --CfgCpu to enable Control Flow Graph CPU."
  }
}
```

## Architecture

The MCP server implementation follows these key principles:

1. **External Dependency**: Uses the `ModelContextProtocol.Core` NuGet package for protocol types (e.g., `Tool`, `InitializeResult`, `ListToolsResult`) and standard .NET libraries (System.Text.Json) for JSON-RPC message handling
2. **No Microsoft DI**: Follows Spice86's manual dependency injection pattern
3. **In-Process**: Runs in the same process as the emulator for minimal latency
4. **JSON-RPC 2.0**: Implements the MCP protocol over JSON-RPC 2.0

### Components

- **`IMcpServer`**: Interface defining the MCP server contract
- **`McpServer`**: Implementation of the MCP server with tool handlers
- **`Tool`**: Type (from `ModelContextProtocol.Protocol`) describing available tools

## Integration

### Enabling the MCP Server

To enable the MCP server with stdio transport, use the `--McpServer` command-line flag:

```bash
dotnet run --project src/Spice86 -- --Exe program.exe --McpServer true
```

When enabled, the MCP server:
1. Starts automatically with Spice86
2. Reads JSON-RPC requests from **stdin**
3. Writes JSON-RPC responses to **stdout**
4. Stops automatically when Spice86 exits

### Architecture

The MCP server is instantiated in `Spice86DependencyInjection.cs` and receives:
- `IMemory` - for memory inspection
- `State` - for CPU register inspection  
- `FunctionCatalogue` - for function listing
- `CfgCpu` (nullable) - for CFG graph inspection (only when `--CfgCpu` is enabled)
- `IPauseHandler` - for automatic pause/resume during inspection
- `ILoggerService` - for diagnostic logging

The stdio transport layer (`McpStdioTransport`) runs in a background task and handles the newline-delimited JSON-RPC protocol communication.

### Thread-Safe State Inspection

The MCP server is fully thread-safe and can be called from multiple threads concurrently. It uses an internal lock to serialize all requests. For each request, the server:

1. **Acquires the lock** - ensures only one request is processed at a time
2. **Pauses the emulator** - stops execution to get consistent state snapshot
3. **Reads the state** - accesses registers, memory, or other data
4. **Resumes the emulator** - restarts execution (if it wasn't already paused)
5. **Releases the lock** - allows the next request to proceed

This ensures:
- **Concurrent access safety**: Multiple threads can call the server without coordination
- **Consistent snapshots**: State doesn't change mid-inspection
- **No race conditions**: Lock serializes all requests and pause protects state reads
- **Automatic management**: Tools handle locking and pause/resume transparently

If the emulator is already paused when a tool is called, the server preserves that state and doesn't resume automatically.

## Protocol Compliance

The server implements core MCP protocol methods:

- `initialize`: Handshake and capability negotiation
- `tools/list`: Enumerate available tools
- `tools/call`: Execute a specific tool

Error handling follows JSON-RPC 2.0 conventions with appropriate error codes:
- `-32700`: Parse error (invalid JSON)
- `-32600`: Invalid request (missing required fields)
- `-32601`: Method not found
- `-32602`: Invalid params
- `-32603`: Internal/tool execution error

## Testing

Integration tests in `tests/Spice86.Tests/McpServerTest.cs` verify:
- Protocol initialization and handshake
- Tool listing and discovery
- CPU register reading
- Memory reading with validation
- Function catalogue querying
- Error handling for malformed requests

All tests use the standard Spice86 test infrastructure with `Spice86Creator` for consistent emulator setup.

## Future Enhancements

Potential future additions:
- Write operations (memory, registers)
- Breakpoint management
- Single-step execution
- Disassembly inspection
- Real-time event streaming

## References

- [Model Context Protocol Specification](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)
- [JSON-RPC 2.0 Specification](https://www.jsonrpc.org/specification)
- Spice86 Dependency Injection: `src/Spice86/Spice86DependencyInjection.cs`
- GDB Server: `src/Spice86.Core/Emulator/Gdb/GdbServer.cs` (similar remote access pattern)
