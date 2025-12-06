# MCP Server Integration Test Plan

## Overview

This document outlines the strategy for comprehensive integration testing of the MCP (Model Context Protocol) server. The goal is to ensure the MCP server works reliably for all developers and environments.

## Current Test Coverage

The existing test suite (`McpServerTest.cs`) covers:
- ✅ Protocol initialization and handshake
- ✅ Tool discovery (list tools)
- ✅ CPU register reading
- ✅ Memory reading with validation
- ✅ Function catalogue querying
- ✅ Error handling for invalid JSON
- ✅ Error handling for unknown methods
- ✅ Error handling for invalid parameters

## Future Integration Test Requirements

### 1. Cross-Platform Compatibility Tests

**Goal**: Ensure MCP server works consistently across Windows, Linux, and macOS.

**Test Cases**:
- Verify JSON serialization produces identical output on all platforms
- Test that JsonElement conversions work correctly across different .NET runtimes
- Validate that file paths in error messages use platform-appropriate formats

**Implementation**:
```csharp
[Theory]
[InlineData("Windows")]
[InlineData("Linux")]
[InlineData("macOS")]
public void TestMcpServer_CrossPlatform(string platform) {
    // Skip if not running on target platform
    // Test basic protocol operations
    // Verify response format consistency
}
```

### 2. Concurrent Access Tests

**Goal**: Document and test thread-safety guarantees (currently not thread-safe).

**Test Cases**:
- Document that MCP server requires external synchronization
- Provide example of proper synchronization wrapper
- Test that sequential access works correctly

**Implementation**:
```csharp
[Fact]
public void TestMcpServer_SequentialAccess() {
    // Multiple sequential requests
    // Verify state consistency
}

[Fact]
public void TestMcpServer_ConcurrentAccess_RequiresLocking() {
    // Document that concurrent access requires locks
    // Provide example wrapper with locking
}
```

### 3. Large Data Handling Tests

**Goal**: Verify behavior with maximum and edge-case data sizes.

**Test Cases**:
- Memory reads at maximum size (4096 bytes)
- Memory reads at various addresses (0, max address, boundary conditions)
- Large function catalogues (1000+ functions)
- Register values at extremes (0, max uint/ushort)

**Implementation**:
```csharp
[Theory]
[InlineData(1)]
[InlineData(1024)]
[InlineData(4096)]
public void TestMcpServer_MemoryReadSizes(int size) {
    // Test various memory read sizes
}

[Fact]
public void TestMcpServer_LargeFunctionCatalogue() {
    // Create catalogue with 1000+ functions
    // Test list_functions with various limits
    // Verify performance is acceptable
}
```

### 4. Protocol Compliance Tests

**Goal**: Ensure strict compliance with MCP and JSON-RPC 2.0 specifications.

**Test Cases**:
- All error codes match JSON-RPC 2.0 spec (-32700, -32600, -32601, -32602, -32603)
- Response format matches MCP specification exactly
- Tool schemas follow JSON Schema Draft 7
- Protocol version negotiation works correctly

**Implementation**:
```csharp
[Fact]
public void TestMcpServer_JsonRpcCompliance() {
    // Test all error code scenarios
    // Verify response structure matches spec
}

[Fact]
public void TestMcpServer_ToolSchemaValidation() {
    // Validate InputSchema against JSON Schema spec
    // Ensure all required fields present
}
```

### 5. Real-World Integration Tests

**Goal**: Test MCP server with actual DOS programs running in the emulator.

**Test Cases**:
- Start emulator with simple DOS program
- Query registers during execution
- Read memory at known addresses
- Verify function tracking works
- Test with multiple sequential queries during execution

**Implementation**:
```csharp
[Fact]
public void TestMcpServer_WithRunningEmulator() {
    // Load simple DOS program (e.g., "add" test program)
    // Execute a few instructions
    // Query MCP server for state
    // Verify responses match actual emulator state
}

[Fact]
public void TestMcpServer_FunctionTracking() {
    // Load program with known functions
    // Execute until functions are called
    // Query function catalogue
    // Verify call counts are accurate
}
```

### 6. Error Recovery Tests

**Goal**: Ensure MCP server handles error conditions gracefully.

**Test Cases**:
- Malformed JSON at various levels
- Missing required fields
- Invalid data types in parameters
- Out-of-bounds memory addresses
- Negative lengths or addresses
- Extremely large values

**Implementation**:
```csharp
[Theory]
[InlineData("{malformed")]
[InlineData("{}")]
[InlineData("{\"jsonrpc\":\"2.0\"}")]
public void TestMcpServer_MalformedRequests(string request) {
    // Verify appropriate error responses
    // Ensure server remains operational
}

[Theory]
[InlineData(uint.MaxValue, 100)] // Address beyond memory
[InlineData(0, -1)] // Negative length
[InlineData(0, 10000)] // Length too large
public void TestMcpServer_InvalidMemoryParameters(uint address, int length) {
    // Verify appropriate error responses
}
```

### 7. Performance Tests

**Goal**: Ensure MCP server performs adequately under various conditions.

**Test Cases**:
- Response time for simple queries (< 10ms target)
- Response time for maximum-size memory reads
- Response time for large function catalogues
- Memory allocation per request (should be minimal)

**Implementation**:
```csharp
[Fact]
public void TestMcpServer_ResponseTime() {
    Stopwatch sw = Stopwatch.StartNew();
    // Execute 100 register queries
    sw.Stop();
    // Verify average < 10ms per query
}

[Fact]
public void TestMcpServer_MemoryUsage() {
    // Measure memory before
    // Execute many queries
    // Measure memory after
    // Verify no significant leaks
}
```

### 8. Documentation Accuracy Tests

**Goal**: Verify code examples in documentation actually work.

**Test Cases**:
- Extract and run code examples from mcpServerReadme.md
- Extract and run code examples from mcpServerExample.md
- Verify all API signatures match documentation

**Implementation**:
```csharp
[Fact]
public void TestMcpServer_DocumentationExamples() {
    // Run examples from documentation
    // Verify they produce expected output
}
```

## Test Environment Setup

### Prerequisites
- .NET 10 SDK
- Spice86 test infrastructure
- Sample DOS programs for testing

### Running Tests

```bash
# Run all MCP tests
dotnet test --filter "FullyQualifiedName~McpServerTest"

# Run specific test category
dotnet test --filter "Category=Integration&FullyQualifiedName~McpServerTest"

# Run with detailed output
dotnet test --filter "FullyQualifiedName~McpServerTest" --logger "console;verbosity=detailed"
```

## Continuous Integration

### CI Pipeline Requirements
1. Run on all supported platforms (Windows, Linux, macOS)
2. Run with different .NET configurations (Debug, Release)
3. Run with code coverage analysis
4. Fail build if coverage drops below threshold (e.g., 90%)

### Test Categories
- **Unit**: Fast tests, no external dependencies (~8 tests currently)
- **Integration**: Tests with running emulator (future)
- **Performance**: Benchmark tests (future)
- **Documentation**: Documentation accuracy tests (future)

## Success Criteria

A comprehensive test suite should:
- ✅ Have > 90% code coverage for MCP server
- ✅ Pass on all supported platforms
- ✅ Complete in < 30 seconds (excluding performance tests)
- ✅ Provide clear failure messages
- ✅ Be maintainable and easy to extend
- ✅ Validate both happy paths and error cases
- ✅ Test real-world usage scenarios

## Implementation Priority

1. **High Priority** (Implement First):
   - Error recovery tests
   - Large data handling tests
   - Protocol compliance tests

2. **Medium Priority** (Implement Next):
   - Real-world integration tests
   - Performance tests
   - Cross-platform compatibility tests

3. **Low Priority** (Nice to Have):
   - Documentation accuracy tests
   - Concurrent access documentation

## Maintenance

- Review and update this plan quarterly
- Add new test cases as bugs are discovered
- Keep tests synchronized with SDK updates
- Monitor test execution time and optimize as needed

## Resources

- [MCP Specification](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)
- [JSON-RPC 2.0 Specification](https://www.jsonrpc.org/specification)
- [ModelContextProtocol.Core SDK](https://www.nuget.org/packages/ModelContextProtocol.Core)
