# MCP Server Usage Examples

This document provides practical examples of using the Spice86 MCP server to inspect emulator state.

## Stdio Transport Usage (Standard MCP)

The MCP server uses stdio transport, the standard communication method for MCP servers. Enable it with the `--McpServer` flag:

```bash
dotnet run --project src/Spice86 -- --Exe program.exe --McpServer true
```

When enabled, the server:
- Reads JSON-RPC requests from **stdin** (newline-delimited)
- Writes JSON-RPC responses to **stdout** (newline-delimited)
- Runs in a background task until Spice86 exits

External tools and AI models can communicate with the emulator by sending JSON-RPC requests to stdin and reading responses from stdout.

### Example: External Tool Communication

```bash
# Start Spice86 with MCP server enabled
dotnet run --project src/Spice86 -- --Exe program.exe --McpServer true &

# Send an initialize request
echo '{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2025-06-18"},"id":1}' | dotnet run --project src/Spice86 -- --Exe program.exe --McpServer true

# Send a tools/list request
echo '{"jsonrpc":"2.0","method":"tools/list","id":2}' | dotnet run --project src/Spice86 -- --Exe program.exe --McpServer true

# Send a read_cpu_registers request
echo '{"jsonrpc":"2.0","method":"tools/call","params":{"name":"read_cpu_registers","arguments":{}},"id":3}' | dotnet run --project src/Spice86 -- --Exe program.exe --McpServer true
```

## In-Process API Usage (Testing/Debugging)

For testing and debugging, you can also use the MCP server in-process via the `HandleRequest` API:

```csharp
using Spice86;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.Mcp;

// Create configuration for a DOS program
Configuration configuration = new Configuration {
    Exe = "path/to/program.exe",
    HeadlessMode = HeadlessType.Minimal,
    GdbPort = 0 // Disable GDB server if not needed
};

// Create the emulator with dependency injection
using Spice86DependencyInjection spice86 = new Spice86DependencyInjection(configuration);

// Access the MCP server
IMcpServer mcpServer = spice86.McpServer;

// Example 1: Initialize the MCP connection
string initRequest = """
{
  "jsonrpc": "2.0",
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-06-18"
  },
  "id": 1
}
""";

string initResponse = mcpServer.HandleRequest(initRequest);
Console.WriteLine("Initialize Response:");
Console.WriteLine(initResponse);

// Example 2: List available tools
string toolsListRequest = """
{
  "jsonrpc": "2.0",
  "method": "tools/list",
  "id": 2
}
""";

string toolsListResponse = mcpServer.HandleRequest(toolsListRequest);
Console.WriteLine("\nAvailable Tools:");
Console.WriteLine(toolsListResponse);

// Example 3: Read CPU registers
string readRegistersRequest = """
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_cpu_registers",
    "arguments": {}
  },
  "id": 3
}
""";

string registersResponse = mcpServer.HandleRequest(readRegistersRequest);
Console.WriteLine("\nCPU Registers:");
Console.WriteLine(registersResponse);

// Example 4: Read memory at a specific address
string readMemoryRequest = """
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_memory",
    "arguments": {
      "address": 0,
      "length": 256
    }
  },
  "id": 4
}
""";

string memoryResponse = mcpServer.HandleRequest(readMemoryRequest);
Console.WriteLine("\nMemory Contents:");
Console.WriteLine(memoryResponse);

// Example 5: List functions
string listFunctionsRequest = """
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "list_functions",
    "arguments": {
      "limit": 20
    }
  },
  "id": 5
}
""";

string functionsResponse = mcpServer.HandleRequest(listFunctionsRequest);
Console.WriteLine("\nFunction Catalogue:");
Console.WriteLine(functionsResponse);
```

## Integration with Debuggers

The MCP server can be used alongside the GDB server for comprehensive debugging:

```csharp
Configuration configuration = new Configuration {
    Exe = "game.exe",
    HeadlessMode = HeadlessType.Minimal,
    GdbPort = 10000,  // Enable GDB server on port 10000
    Debug = true      // Start paused
};

using Spice86DependencyInjection spice86 = new Spice86DependencyInjection(configuration);

// Use GDB for step-by-step debugging
// Use MCP server for programmatic state inspection
IMcpServer mcpServer = spice86.McpServer;

// You can query state at any point
string state = mcpServer.HandleRequest("""
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_cpu_registers",
    "arguments": {}
  },
  "id": 1
}
""");
```

## Automated Testing

The MCP server is particularly useful for automated testing and verification:

```csharp
// Load a test program
Configuration config = new Configuration {
    Exe = "test_program.com",
    HeadlessMode = HeadlessType.Minimal
};

using Spice86DependencyInjection emulator = new Spice86DependencyInjection(config);

// Run the program for a certain number of cycles
// (integrate with your execution logic)

// Verify final state using MCP server
IMcpServer mcpServer = emulator.McpServer;

// Check that AX register has expected value
string response = mcpServer.HandleRequest("""
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_cpu_registers",
    "arguments": {}
  },
  "id": 1
}
""");

// Parse response and assert values
// (integrate with your test framework)
```

## Real-time Monitoring

Create a monitoring tool that periodically samples emulator state:

```csharp
Configuration config = new Configuration {
    Exe = "application.exe",
    HeadlessMode = HeadlessType.Minimal
};

using Spice86DependencyInjection emulator = new Spice86DependencyInjection(config);
IMcpServer mcpServer = emulator.McpServer;

// Start emulation in a background task
Task.Run(() => emulator.ProgramExecutor.Run());

// Monitor state every 100ms
using System.Timers.Timer timer = new System.Timers.Timer(100);
timer.Elapsed += (sender, args) => {
    string registers = mcpServer.HandleRequest("""
    {
      "jsonrpc": "2.0",
      "method": "tools/call",
      "params": {
        "name": "read_cpu_registers",
        "arguments": {}
      },
      "id": 1
    }
    """);
    
    // Log or visualize state
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {registers}");
};
timer.Start();

// Keep running until user stops
Console.WriteLine("Press Enter to stop monitoring...");
Console.ReadLine();
```

## CFG CPU Graph Inspection

When the Control Flow Graph CPU is enabled, you can inspect its state:

```csharp
using Spice86;
using Spice86.Core.CLI;

// Enable CFG CPU in configuration
Configuration configuration = new Configuration {
    Exe = "path/to/program.exe",
    CfgCpu = true,  // Enable Control Flow Graph CPU
    HeadlessMode = HeadlessType.Minimal
};

using Spice86DependencyInjection spice86 = new Spice86DependencyInjection(configuration);
IMcpServer mcpServer = spice86.McpServer;

// Run some emulation steps first to build the CFG
spice86.ProgramExecutor.Run();

// Now inspect the CFG CPU state
string cfgCpuRequest = """
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_cfg_cpu_graph",
    "arguments": {}
  },
  "id": 1
}
""";

string response = mcpServer.HandleRequest(cfgCpuRequest);
Console.WriteLine("CFG CPU Graph State:");
Console.WriteLine(response);

// The response includes:
// - currentContextDepth: Execution context nesting level
// - currentContextEntryPoint: Entry point of current context
// - totalEntryPoints: Number of CFG entry points
// - entryPointAddresses: All entry point addresses
// - lastExecutedAddress: Most recently executed instruction
```

**Note**: The `read_cfg_cpu_graph` tool is only available when CFG CPU is enabled with `--CfgCpu` or `CfgCpu = true` in the configuration. Calling it when CFG CPU is disabled will return a JSON-RPC error with code `-32603`.

## Notes

- The MCP server is **thread-safe** and can be called from multiple threads concurrently
- The server uses an internal lock to serialize all requests, ensuring consistent state inspection
- The MCP server **automatically pauses** the emulator before inspecting state and resumes it afterward for thread-safe access
- If the emulator is already paused, the server preserves that state and doesn't auto-resume
- Requests are **synchronous** - each request is processed atomically while holding the lock
- The server does **not** modify emulator state - it's read-only by design
- All responses follow **JSON-RPC 2.0** format with proper error handling
- Memory reads are **limited to 4096 bytes** per request for safety
- The **CFG CPU graph tool** is only available when CFG CPU is enabled (`--CfgCpu` flag)

## Error Handling

Always handle potential errors in responses:

```csharp
string response = mcpServer.HandleRequest(request);
using JsonDocument doc = JsonDocument.Parse(response);
JsonElement root = doc.RootElement;

if (root.TryGetProperty("error", out JsonElement error)) {
    int code = error.GetProperty("code").GetInt32();
    string? message = error.GetProperty("message").GetString();
    Console.WriteLine($"Error {code}: {message}");
} else if (root.TryGetProperty("result", out JsonElement result)) {
    // Process successful result
    Console.WriteLine("Success!");
}
```
