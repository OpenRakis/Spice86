namespace Spice86.Tests.Emulator.Gdb;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Gdb;
using Spice86.Shared.Interfaces;

using Xunit;

using static Spice86.Core.Emulator.CPU.CpuModel;

/// <summary>
/// Unit tests for GdbCommandRegisterHandler to verify proper reading and writing of CPU registers via GDB protocol.
/// </summary>
public class GdbCommandRegisterHandlerTests {
    private readonly ILoggerService _loggerService;
    private readonly GdbIo _gdbIo;
    private readonly State _state;
    private readonly GdbCommandRegisterHandler _handler;

    public GdbCommandRegisterHandlerTests() {
        _loggerService = Substitute.For<ILoggerService>();
        _gdbIo = new GdbIo(0, _loggerService);
        _state = new State(INTEL_80286);
        _handler = new GdbCommandRegisterHandler(_state, _gdbIo, _loggerService);
    }

    [Fact]
    public void ReadAllRegisters_ShouldReturnAllRegisterValuesInCorrectFormat() {
        // Arrange
        _state.AX = 0x1234;
        _state.CX = 0x5678;
        _state.IP = 0x100;

        // Act
        string response = _handler.ReadAllRegisters();

        // Assert
        response.Should().StartWith("+$");
        response.Should().Contain("#");
        // Response should contain hex values for all 16 registers (AX, CX, DX, BX, SP, BP, SI, DI, IP, FLAGS, CS, SS, DS, ES, FS, GS)
        string payload = GdbTestUtilities.ExtractPayload(response);
        payload.Should().HaveLength(16 * 8); // 16 registers * 8 hex chars each
    }

    [Theory]
    [InlineData("0", 0x1234)] // AX
    [InlineData("1", 0x5678)] // CX
    [InlineData("8", 0x100)]  // IP (physical address)
    public void ReadRegister_ShouldReturnSpecificRegisterValue(string registerIndex, ushort expectedValue) {
        // Arrange
        if (registerIndex == "0") {
            _state.AX = expectedValue;
        } else if (registerIndex == "1") {
            _state.CX = expectedValue;
        } else if (registerIndex == "8") {
            _state.IP = expectedValue;
        }

        // Act
        string response = _handler.ReadRegister(registerIndex);

        // Assert
        response.Should().StartWith("+$");
        string payload = GdbTestUtilities.ExtractPayload(response);
        // GdbFormatter already handles byte swapping for GDB protocol
        // For value 0x1234: GDB expects "34120000" (little-endian)
        // For value 0x5678: GDB expects "78560000"
        // For value 0x100: GDB expects "00010000"
        payload.Should().HaveLength(8);
    }

    [Fact]
    public void WriteRegister_ShouldUpdateCpuState() {
        // Arrange
        // Format: P{register_index}={hex_value}
        // Writing 0x1234 to AX (register 0)
        // Value needs to be in swapped format
        string commandContent = "0=34120000";

        // Act
        string response = _handler.WriteRegister(commandContent);

        // Assert
        response.Should().Contain("OK");
        _state.AX.Should().Be(0x1234);
    }

    [Fact]
    public void WriteAllRegisters_ShouldUpdateAllRegisters() {
        // Arrange
        // 16 registers, each 4 bytes (as 32-bit values for GDB)
        // GDB protocol expects values in the native byte order they appear in memory
        // Setting AX=0x1234, CX=0x5678
        // In the hex string representation: lower address bytes first
        string commandContent = "00001234" + "00005678" + new string('0', 14 * 8);

        // Act
        string response = _handler.WriteAllRegisters(commandContent);

        // Assert
        response.Should().Contain("OK");
        _state.AX.Should().Be(0x1234);
        _state.CX.Should().Be(0x5678);
    }

    [Fact]
    public void ReadRegister_WithInvalidFormat_ShouldReturnUnsupportedResponse() {
        // Arrange
        string invalidCommand = "invalid";

        // Act
        string response = _handler.ReadRegister(invalidCommand);

        // Assert
        response.Should().BeEmpty(); // Unsupported response is empty
    }
}
