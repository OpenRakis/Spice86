namespace Spice86.Tests.Emulator.Gdb;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Unit tests for GdbCommandMemoryHandler to verify proper reading and writing of memory via GDB protocol.
/// </summary>
public class GdbCommandMemoryHandlerTests {
    private readonly ILoggerService _loggerService;
    private readonly GdbIo _gdbIo;
    private readonly IMemory _memory;
    private readonly GdbCommandMemoryHandler _handler;

    public GdbCommandMemoryHandlerTests() {
        _loggerService = Substitute.For<ILoggerService>();
        _gdbIo = new GdbIo(0, _loggerService);
        _memory = Substitute.For<IMemory>();
        _handler = new GdbCommandMemoryHandler(_memory, _gdbIo, _loggerService);
    }

    [Fact]
    public void ReadMemory_WithInvalidFormat_ShouldReturnUnsupportedResponse() {
        // Arrange
        string invalidCommand = "invalid";

        // Act
        string response = _handler.ReadMemory(invalidCommand);

        // Assert
        response.Should().BeEmpty(); // Unsupported response is empty
    }

    [Fact]
    public void WriteMemory_WithInvalidFormat_ShouldReturnUnsupportedResponse() {
        // Arrange
        string invalidCommand = "invalid";

        // Act
        string response = _handler.WriteMemory(invalidCommand);

        // Assert
        response.Should().BeEmpty(); // Unsupported response is empty
    }

    private static string ExtractPayload(string response) {
        // Response format: +$payload#checksum
        int dollarIndex = response.IndexOf('$');
        int hashIndex = response.IndexOf('#');
        if (dollarIndex >= 0 && hashIndex > dollarIndex) {
            return response.Substring(dollarIndex + 1, hashIndex - dollarIndex - 1);
        }
        return string.Empty;
    }
}
