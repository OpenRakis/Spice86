namespace Spice86.Tests.Dos.Xms;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

/// <summary>
/// Tests the eXtended Memory Manager (XMS) functionality.
/// Based on the HITEST.ASM tool from Microsoft's XMS driver validation suite.
/// </summary>
public class XmsUnitTests
{
    private readonly ExtendedMemoryManager _xms;
    private readonly State _state;
    private readonly Memory _memory;
    private readonly A20Gate _a20Gate;
    private readonly ILoggerService _loggerService;
    private readonly CallbackHandler _callbackHandler;
    private readonly MemoryAsmWriter _asmWriter;
    private readonly DosTables _dosTables;

    public XmsUnitTests()
    {
        // Setup memory and state
        _state = new State();
        _a20Gate = new A20Gate(false);
        _memory = new Memory(new(), new Ram(A20Gate.EndOfHighMemoryArea), _a20Gate);
        _loggerService = Substitute.For<ILoggerService>();
        _callbackHandler = new CallbackHandler(_state, _loggerService);
        _dosTables = new DosTables();
        _asmWriter = new MemoryAsmWriter(_memory, new(0, 0), _callbackHandler);

        // Create XMS manager
        _xms = new ExtendedMemoryManager(_memory, _state, _a20Gate, _asmWriter,
            _dosTables, _loggerService);
    }

    [Fact]
    public void GetXmsVersion_ShouldReturnCorrectValues()
    {
        // Arrange
        _state.AH = 0x00; // Get XMS Version

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(ExtendedMemoryManager.XmsVersion, "XMS version should be 0x0300 (3.00)");
        _state.BX.Should().Be(ExtendedMemoryManager.XmsInternalVersion, "Internal version should match");
        _state.DX.Should().Be(0x0001, "HMA should exist");
    }

    [Fact]
    public void RequestHighMemoryArea_ShouldSucceed()
    {
        // Arrange
        _state.AH = 0x01; // Request HMA
        _state.DX = 0xFFFF; // Request full HMA for application

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "HMA request should succeed");
        _state.BL.Should().Be(0, "No error should be reported");
    }

    [Fact]
    public void RequestHighMemoryArea_SecondRequestShouldFail()
    {
        // Arrange - First request
        _state.AH = 0x01;
        _state.DX = 0xFFFF;
        _xms.RunMultiplex();

        // Act - Second request
        _state.AH = 0x01;
        _state.DX = 0xFFFF;
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(0, "Second HMA request should fail");
        _state.BL.Should().Be(0x91, "HMA in use error should be reported");
    }

    [Fact]
    public void ReleaseHighMemoryArea_ShouldSucceed()
    {
        // Arrange - Request HMA
        _state.AH = 0x01;
        _state.DX = 0xFFFF;
        _xms.RunMultiplex();

        // Act - Release HMA
        _state.AH = 0x02;
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "HMA release should succeed");
        _state.BL.Should().Be(0, "No error should be reported");
    }

    [Fact]
    public void ReleaseHighMemoryArea_WithoutRequestShouldFail()
    {
        // Act - Release HMA without requesting it
        _state.AH = 0x02;
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(0, "HMA release should fail");
        _state.BL.Should().Be(0x93, "HMA not allocated error should be reported");
    }

    [Fact]
    public void GlobalEnableA20_ShouldEnableA20Line()
    {
        // Arrange
        _state.AH = 0x03; // Global Enable A20

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "Global Enable A20 should succeed");
        _state.BL.Should().Be(0, "No error should be reported");
        _a20Gate.IsEnabled.Should().BeTrue("A20 line should be enabled");
    }

    [Fact]
    public void A20AlreadyEnabledAtStartup_ShouldPreventDisabling() {
        // Arrange - Create a new XMS manager with A20 already enabled
        var ram = new Ram(16384 * 1024);
        var state = new State();
        var a20Gate = new A20Gate(true); // A20 is ALREADY enabled at startup
        var memory = new Memory(new(), new Ram(A20Gate.EndOfHighMemoryArea), a20Gate);
        var loggerService = Substitute.For<ILoggerService>();
        var callbackHandler = new CallbackHandler(state, loggerService);
        var dosTables = new DosTables();
        var asmWriter = new MemoryAsmWriter(memory, new(0, 0), callbackHandler);
        var xms = new ExtendedMemoryManager(memory, state, a20Gate, asmWriter, dosTables, loggerService);

        // Verify initial state
        a20Gate.IsEnabled.Should().BeTrue("A20 gate should be enabled at startup");

        // Act - Try to disable A20 locally
        state.AH = 0x06; // Local Disable A20
        xms.RunMultiplex();

        // Assert - Disabling should "succeed" but A20 should remain enabled
        state.AX.Should().Be(1, "Local Disable A20 should report success");
        state.BL.Should().Be(0, "No error should be reported");
        a20Gate.IsEnabled.Should().BeTrue("A20 should remain enabled because it was already enabled at startup");

        // Act - Try to disable A20 globally
        state.AH = 0x04; // Global Disable A20
        xms.RunMultiplex();

        // Assert - Global disabling should also "succeed" but A20 should remain enabled
        state.AX.Should().Be(0, "Global Disable A20 should report success");
        state.BL.Should().Be(0x82, "Error code ERR_A20 (82h) should be reported");
        a20Gate.IsEnabled.Should().BeTrue("A20 should remain enabled because it was already enabled at startup");

        // Act - Try to enable A20 (which should already be enabled)
        state.AH = 0x05; // Local Enable A20
        xms.RunMultiplex();

        // Assert - Enabling should work normally
        state.AX.Should().Be(1, "Local Enable A20 should succeed");
        state.BL.Should().Be(0, "No error should be reported");
        a20Gate.IsEnabled.Should().BeTrue("A20 should remain enabled");
    }

    [Fact]
    public void LocalDisableA20_WithoutEnableShouldFail() {
        // Act - Disable A20 without enabling it
        _state.AH = 0x06;
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(0, "Local Disable A20 without enable should fail");
        _state.BL.Should().Be(0x82, "Error code ERR_A20 (82h) should be reported");
    }

    [Fact]
    public void NestedA20LocalEnablesDisables_ShouldWorkCorrectly() {
        // First enable - should enable A20 and set counter to 1
        _a20Gate.IsEnabled = false;
        _state.AH = 0x05;
        _xms.RunMultiplex();
        _a20Gate.IsEnabled.Should().BeTrue("First local enable should enable A20");
        _state.AX.Should().Be(1, "First enable should succeed");
        _state.BL.Should().Be(0, "No error should be reported");

        // Second enable - A20 should remain enabled and increment counter to 2
        _state.AH = 0x05;
        _xms.RunMultiplex();
        _a20Gate.IsEnabled.Should().BeTrue("Second local enable should keep A20 enabled");
        _state.AX.Should().Be(1, "Second enable should succeed");
        _state.BL.Should().Be(0, "No error should be reported");

        // First disable - A20 should remain enabled (counter = 1)
        _state.AH = 0x06;
        _xms.RunMultiplex();
        _a20Gate.IsEnabled.Should().BeTrue("First local disable with nested enable should keep A20 enabled");
        _state.AX.Should().Be(1, "First disable should succeed");
        _state.BL.Should().Be(0, "No error should be reported");

        // Second disable - A20 should now be disabled (counter = 0)
        _state.AH = 0x06;
        _xms.RunMultiplex();
        _a20Gate.IsEnabled.Should().BeFalse("Second local disable should disable A20");
        _state.AX.Should().Be(1, "Second disable should succeed");
        _state.BL.Should().Be(0, "No error should be reported");
    }

    [Fact]
    public void GlobalAndLocalA20Interaction_ShouldWorkCorrectly() {
        // Global enable A20
        _state.AH = 0x03;
        _xms.RunMultiplex();
        _a20Gate.IsEnabled.Should().BeTrue("Global enable should enable A20");
        _state.AX.Should().Be(1, "Global enable should succeed");
        _state.BL.Should().Be(0, "No error should be reported");

        _state.AH = 0x06;
        _xms.RunMultiplex();
        _a20Gate.IsEnabled.Should().BeFalse("A20 should be disabled because the counter reached 0");
        _state.AX.Should().Be(1, "Local disable should succeed");
        _state.BL.Should().Be(0, "No error should be reported");

        // Global disable - disables A20 if count reaches zero
        _state.AH = 0x04;
        _xms.RunMultiplex();
        _a20Gate.IsEnabled.Should().BeFalse("a20Gate should still be disabled");
        _state.AX.Should().Be(0, "Global disable should succeed");
        _state.BL.Should().Be(0, "No error should be reported");
    }

    [Fact]
    public void QueryA20_ShouldReturnCurrentA20State()
    {
        // Arrange - Set A20 enabled
        _a20Gate.IsEnabled = true;
        _state.AH = 0x07; // Query A20

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "A20 should be reported as enabled");
        _state.BL.Should().Be(0, "No error should be reported");

        // Arrange - Set A20 disabled
        _a20Gate.IsEnabled = false;
        _state.AH = 0x07; // Query A20

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(0, "A20 should be reported as disabled");
        _state.BL.Should().Be(0, "No error should be reported");
    }

    [Fact]
    public void QueryFreeExtendedMemory_ShouldReturnAvailableMemory()
    {
        // Arrange
        _state.AH = 0x08; // Query Free Extended Memory

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().BeGreaterThan(0, "Available extended memory should be reported");
        _state.DX.Should().BeGreaterThan(0, "Total free extended memory should be reported");
        _state.BL.Should().Be(0, "No error should be reported");
    }

    [Fact]
    public void AllocateExtendedMemoryBlock_ShouldSucceed()
    {
        // Arrange
        _state.AH = 0x09; // Allocate Extended Memory Block
        _state.DX = 64;   // Allocate 64K

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "Memory allocation should succeed");
        _state.DX.Should().BeGreaterThan(0, "Valid handle should be returned");
        _state.BL.Should().Be(0, "No error should be reported");
    }

    [Fact]
    public void FreeExtendedMemoryBlock_ShouldSucceed()
    {
        // Arrange - First allocate memory
        _state.AH = 0x09;
        _state.DX = 64;
        _xms.RunMultiplex();
        ushort handle = _state.DX;

        // Act - Free the allocated memory
        _state.AH = 0x0A;
        _state.DX = handle;
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "Memory free should succeed");
        _state.BL.Should().Be(0, "No error should be reported");
    }

    [Fact]
    public void MoveExtendedMemoryBlock_ShouldMoveData()
    {
        // Arrange - Allocate source and destination blocks
        _state.AH = 0x09;
        _state.DX = 1;  // 1K destination block
        _xms.RunMultiplex();
        ushort destHandle = _state.DX;

        // Write test pattern to source block
        _memory.UInt8[(int)0xF000] = 0x42;
        _memory.UInt8[(int)0xF000 + 1] = 0x43;

        // Create move structure in conventional memory
        uint moveStructAddr = 0x1000;
        ExtendedMemoryMoveStructure memoryMoveStructure = new(_memory, moveStructAddr);
        memoryMoveStructure.SourceHandle = 0;
        memoryMoveStructure.SourceOffset = 0xF000;
        memoryMoveStructure.DestOffset = 0;
        memoryMoveStructure.DestHandle = destHandle;
        memoryMoveStructure.Length = 2;

        // Set up move command
        _state.AH = 0x0B;
        _state.DS = 0;
        _state.SI = 0x1000;

        // Act
        _xms.RunMultiplex();

        // Assert - Check if move was successful
        _state.AX.Should().Be(1, "Move operation should succeed");
        _state.BL.Should().Be(0, "No error should be reported");

        // Lock destination block to check data
        _state.AH = 0x0C;
        _state.DX = destHandle;
        _xms.RunMultiplex();
        uint destAddress = MemoryUtils.To32BitAddress(_state.DX, _state.BX);

        // Verify data was copied
        _xms.XmsRam.Read(destAddress - A20Gate.StartOfHighMemoryArea).Should().Be(0x42, "First byte should be copied");
        _xms.XmsRam.Read(destAddress - A20Gate.StartOfHighMemoryArea + 1).Should().Be(0x43, "Second byte should be copied");
    }
}
