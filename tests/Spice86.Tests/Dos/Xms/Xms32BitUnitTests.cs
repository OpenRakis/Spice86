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

using Xunit;

/// <summary>
/// Tests for the 32-bit extended XMS 3.0 functions.
/// These functions (88h, 89h, 8Eh, 8Fh) were added in XMS 3.0 to support memory beyond 64MB.
/// </summary>
public class Xms32BitFunctionsTests {
    private readonly ExtendedMemoryManager _xms;
    private readonly State _state;
    private readonly Memory _memory;
    private readonly A20Gate _a20Gate;
    private readonly ILoggerService _loggerService;
    private readonly CallbackHandler _callbackHandler;
    private readonly MemoryAsmWriter _asmWriter;
    private readonly DosTables _dosTables;

    public Xms32BitFunctionsTests() {
        // Setup memory and state
        _state = new State(CpuModel.INTEL_80286);
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
    public void QueryAnyFreeExtendedMemory_ShouldReturnCorrectValues() {
        // Arrange
        _state.AH = 0x88; // Query Any Free Extended Memory

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.EAX.Should().BeGreaterThan(0, "Largest free block size should be reported");
        _state.BL.Should().Be(0, "No error should be reported");
        _state.ECX.Should().BeGreaterThan(0, "Highest ending address should be reported");
        _state.EDX.Should().BeGreaterThan(0, "Total free memory should be reported");

        // Additionally check that the returned values make sense
        _state.EAX.Should().Be(_state.EDX, "With no allocations, largest block should equal total free memory");
        _state.ECX.Should().BeGreaterThanOrEqualTo(A20Gate.StartOfHighMemoryArea + (_state.EDX * 1024),
            "Highest address should be at least start + total free memory");
    }

    [Fact]
    public void AllocateAnyExtendedMemory_ShouldAllocateLargeBlock() {
        // Arrange
        _state.AH = 0x89; // Allocate Any Extended Memory
        _state.EDX = ExtendedMemoryManager.XmsMemorySize; // Request entire XMS size
        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "Allocation should succeed");
        _state.DX.Should().BeGreaterThan(0, "Valid handle should be returned");
        _state.BL.Should().Be(0, "No error should be reported");

        // Verify the block size using Get Extended EMB Handle
        ushort handle = _state.DX;
        _state.AH = 0x8E; // Get Extended EMB Handle
        _state.DX = handle;
        _xms.RunMultiplex();

        _state.AX.Should().Be(1, "Handle info request should succeed");
        _state.EDX.Should().Be(ExtendedMemoryManager.XmsMemorySize, "Block size should be 15MB");
    }

    [Fact]
    public void AllocateAnyExtendedMemory_ExceedingAvailableMemory_ShouldFail() {
        // Arrange
        _state.AH = 0x89; // Allocate Any Extended Memory
        _state.EDX = 500000; // Request 500MB (way beyond available memory)

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(0, "Allocation should fail");
        _state.BL.Should().Be(0xA0, "Out of memory error should be reported");
    }

    [Fact]
    public void GetExtendedEmbHandle_ShouldReturnCorrectInfo() {
        // Arrange - First allocate a large block
        _state.AH = 0x89;
        _state.EDX = ExtendedMemoryManager.XmsMemorySize;
        _xms.RunMultiplex();
        ushort handle = _state.DX;

        // Lock the block to increase lock count
        _state.AH = 0x0C;
        _state.DX = handle;
        _xms.RunMultiplex();

        // Act - Get extended handle info
        _state.AH = 0x8E;
        _state.DX = handle;
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "Get handle info should succeed");
        _state.BH.Should().Be(1, "Lock count should be 1");
        _state.CX.Should().BeGreaterThan(0, "Free handles count should be positive");
        _state.EDX.Should().Be(ExtendedMemoryManager.XmsMemorySize, "Block size should be 15MB");
    }

    [Fact]
    public void GetExtendedEmbHandle_WithInvalidHandle_ShouldFail() {
        // Arrange
        _state.AH = 0x8E;
        _state.DX = 0xFFFF; // Invalid handle

        // Act
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(0, "Operation should fail");
        _state.BL.Should().Be(0xA2, "Invalid handle error should be reported");
    }

    [Fact]
    public void ReallocateAnyExtendedMemory_GrowingLargeBlock_ShouldSucceed() {
        // Arrange - Allocate a block
        _state.AH = 0x89;
        _state.EDX = 4096; // 4MB block
        _xms.RunMultiplex();
        ushort handle = _state.DX;

        // Act - Grow the block to 8MB
        _state.AH = 0x8F;
        _state.EBX = 8192;
        _state.DX = handle;
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "Reallocation should succeed");
        _state.BL.Should().Be(0, "No error should be reported");

        // Verify new size
        _state.AH = 0x8E;
        _state.DX = handle;
        _xms.RunMultiplex();
        _state.EDX.Should().Be(8192, "Block should now be 8MB");
    }

    [Fact]
    public void ReallocateAnyExtendedMemory_ShrinkingLargeBlock_ShouldSucceed() {
        // Arrange - Allocate a large block
        _state.AH = 0x89;
        _state.EDX = 8192; // 8MB block
        _xms.RunMultiplex();
        ushort handle = _state.DX;

        // Act - Shrink the block to 10MB
        _state.AH = 0x8F;
        _state.EBX = 10240;
        _state.DX = handle;
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(1, "Reallocation should succeed");
        _state.BL.Should().Be(0, "No error should be reported");

        // Verify new size
        _state.AH = 0x8E;
        _state.DX = handle;
        _xms.RunMultiplex();
        _state.EDX.Should().Be(10240, "Block should now be 10MB");
    }

    [Fact]
    public void ReallocateAnyExtendedMemory_LockedBlock_ShouldFail() {
        // Arrange - Allocate and lock a memory block
        _state.AH = 0x89;
        _state.EDX = 4096; // 4MB block
        _xms.RunMultiplex();
        ushort handle = _state.DX;

        // Lock the block
        _state.AH = 0x0C;
        _state.DX = handle;
        _xms.RunMultiplex();

        // Act - Try to reallocate the locked block
        _state.AH = 0x8F;
        _state.EBX = 8192;
        _state.DX = handle;
        _xms.RunMultiplex();

        // Assert
        _state.AX.Should().Be(0, "Reallocation should fail");
        _state.BL.Should().Be(0xAB, "Block locked error should be reported");
    }

    [Fact]
    public void CompareStandardAndExtendedQueryFunctions_ShouldReturnConsistentResults() {
        // First use standard query function
        _state.AH = 0x08; // Query Free Extended Memory (standard)
        _xms.RunMultiplex();

        ushort standardLargestBlock = _state.AX;
        ushort standardTotalFree = _state.DX;

        // Then use extended query function
        _state.AH = 0x88; // Query Any Free Extended Memory (extended)
        _xms.RunMultiplex();

        uint extendedLargestBlock = _state.EAX;
        uint extendedTotalFree = _state.EDX;

        // The extended function should return the full amount
        extendedLargestBlock.Should().BeGreaterThanOrEqualTo(standardLargestBlock,
            "Extended query should report equal or more memory than standard query");
        extendedTotalFree.Should().BeGreaterThanOrEqualTo(standardTotalFree,
            "Extended query should report equal or more total memory than standard query");
    }

    [Fact]
    public void AllocateStandardAndExtended_ShouldWork() {
        // First allocate using standard function
        _state.AH = 0x09;
        _state.DX = 1024; // 1MB
        _xms.RunMultiplex();
        ushort standardHandle = _state.DX;

        // Then allocate using extended function
        _state.AH = 0x89;
        _state.EDX = 10240; // 10MB
        _xms.RunMultiplex();
        ushort extendedHandle = _state.DX;

        // Both handles should be valid and different
        standardHandle.Should().BeGreaterThan(0, "Standard handle should be valid");
        extendedHandle.Should().BeGreaterThan(0, "Extended handle should be valid");
        extendedHandle.Should().NotBe(standardHandle, "Handles should be different");

        // Verify using extended query function
        _state.AH = 0x8E;
        _state.DX = standardHandle;
        _xms.RunMultiplex();
        _state.EDX.Should().Be(1024, "Standard block should be 1MB");

        _state.AH = 0x8E;
        _state.DX = extendedHandle;
        _xms.RunMultiplex();
        _state.EDX.Should().Be(10240, "Extended block should be 10MB");
    }

    [Fact]
    public void MixedUsageOfStandardAndExtendedFunctions_ShouldWork() {
        // 1. Allocate using extended function
        _state.AH = 0x89; // Allocate Any Extended Memory
        _state.EDX = 1024; // 1MB
        _xms.RunMultiplex();
        ushort handle = _state.DX;

        // 2. Get info using standard function
        _state.AH = 0x0E; // Standard Get EMB Handle Info
        _state.DX = handle;
        _xms.RunMultiplex();

        ushort reportedSize = _state.DX;
        reportedSize.Should().Be(1024 & 0xFFFF, "Standard function should return size modulo 64K");

        // 3. Reallocate using standard function (should work for smaller blocks)
        _state.AH = 0x0F; // Standard Reallocate
        _state.BX = 12000;
        _state.DX = handle;
        _xms.RunMultiplex();

        _state.AX.Should().Be(1, "Reallocation should succeed");
        _state.BL.Should().Be(0, "No error should be reported");

        // 4. Check size with extended function
        _state.AH = 0x8E; // Extended Get EMB Handle Info
        _state.DX = handle;
        _xms.RunMultiplex();

        _state.EDX.Should().Be(12000, "Size should be correctly updated to ~12MB");

        // 5. Try to shrink to 64K with standard function
        _state.AH = 0x0F; // Standard Reallocate
        _state.BX = ushort.MaxValue / 1024; // Just 64K
        _state.DX = handle;
        _xms.RunMultiplex();

        _state.AX.Should().Be(1, "Shrink to 64KB should succeed");
        _state.BL.Should().Be(0, "No error should be reported");
    }
}



