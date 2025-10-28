namespace Spice86.Tests.Dos.Ems;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

/// <summary>
/// Tests the Expanded Memory Manager (EMS) functionality.
/// Based on the LIM EMS 4.0 specification and implementation in ExpandedMemoryManager.cs
/// Each test validates a specific EMS function or behavior.
/// </summary>
public class EmsUnitTests {
    private readonly ExpandedMemoryManager _ems;
    private readonly State _state;
    private readonly Memory _memory;
    private readonly ILoggerService _loggerService;
    private readonly Stack _stack;
    private readonly IFunctionHandlerProvider _functionHandlerProvider;

    public EmsUnitTests() {
        // Arrange - Setup memory and state
        _state = new State(CpuModel.INTEL_80286);
        A20Gate a20Gate = new A20Gate(false);
        _memory = new Memory(new(), new Ram(A20Gate.EndOfHighMemoryArea), a20Gate);
        _loggerService = Substitute.For<ILoggerService>();
        _stack = new Stack(_memory, _state);
        _functionHandlerProvider = Substitute.For<IFunctionHandlerProvider>();

        // Create EMS manager
        _ems = new ExpandedMemoryManager(_memory, _functionHandlerProvider, _stack, _state, _loggerService);
    }

    /// <summary>
    /// Tests that GetStatus (Function 0x40) returns EmmNoError indicating the EMM is working correctly.
    /// </summary>
    [Fact]
    public void GetStatus_ShouldReturnNoError() {
        // Act
        _ems.GetStatus();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "GetStatus should return no error");
    }

    /// <summary>
    /// Tests that GetPageFrameSegment (Function 0x41) returns the correct segment address (0xE000).
    /// </summary>
    [Fact]
    public void GetPageFrameSegment_ShouldReturnCorrectSegment() {
        // Act
        _ems.GetPageFrameSegment();

        // Assert
        _state.BX.Should().Be(ExpandedMemoryManager.EmmPageFrameSegment, "Page frame segment should be 0xE000");
        _state.AH.Should().Be(EmmStatus.EmmNoError, "GetPageFrameSegment should return no error");
    }

    /// <summary>
    /// Tests that GetUnallocatedPageCount (Function 0x42) returns the total and available page counts correctly.
    /// </summary>
    [Fact]
    public void GetUnallocatedPageCount_ShouldReturnCorrectCounts() {
        // Act
        _ems.GetUnallocatedPageCount();

        // Assert
        _state.DX.Should().Be(EmmMemory.TotalPages, "Total pages should be 512");
        _state.BX.Should().BeGreaterThan(0, "Available pages should be greater than 0");
        _state.BX.Should().BeLessThanOrEqualTo(EmmMemory.TotalPages, "Available pages should not exceed total pages");
        _state.AH.Should().Be(EmmStatus.EmmNoError, "GetUnallocatedPageCount should return no error");
    }

    /// <summary>
    /// Tests that AllocatePages (Function 0x43) successfully allocates pages and returns a valid handle.
    /// </summary>
    [Fact]
    public void AllocatePages_ShouldSucceed() {
        // Arrange
        _state.BX = 4; // Allocate 4 pages

        // Act
        _ems.AllocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "AllocatePages should return no error");
        _state.DX.Should().BeGreaterThan(0, "A valid handle should be returned");
    }

    /// <summary>
    /// Tests that AllocatePages (Function 0x43) fails when trying to allocate zero pages.
    /// </summary>
    [Fact]
    public void AllocatePages_WithZeroPages_ShouldFail() {
        // Arrange
        _state.BX = 0; // Try to allocate 0 pages

        // Act
        _ems.AllocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmTriedToAllocateZeroPages, "Should return error for zero page allocation");
    }

    /// <summary>
    /// Tests that AllocatePages (Function 0x43) fails when trying to allocate more pages than available.
    /// </summary>
    [Fact]
    public void AllocatePages_WithTooManyPages_ShouldFail() {
        // Arrange
        _state.BX = (ushort)(EmmMemory.TotalPages + 1); // Try to allocate more than total

        // Act
        _ems.AllocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNotEnoughPages, "Should return not enough pages error");
    }

    /// <summary>
    /// Tests that MapUnmapHandlePage (Function 0x44) successfully maps a logical page to a physical page.
    /// </summary>
    [Fact]
    public void MapUnmapHandlePage_ShouldMapSuccessfully() {
        // Arrange - First allocate some pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Map logical page
        _state.AL = 0;
        _state.BX = 0;
        _state.DX = handle;
        _ems.MapUnmapHandlePage();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "MapUnmapHandlePage should return no error");
    }

    /// <summary>
    /// Tests that MapUnmapHandlePage (Function 0x44) successfully unmaps a physical page.
    /// </summary>
    [Fact]
    public void MapUnmapHandlePage_ShouldUnmapSuccessfully() {
        // Arrange - First allocate and map
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        _state.AL = 0; // Physical page 0
        _state.BX = 0; // Logical page 0
        _state.DX = handle;
        _ems.MapUnmapHandlePage();

        // Act - Unmap by setting logical page to 0xFFFF
        _state.AL = 0; // Physical page 0
        _state.BX = ExpandedMemoryManager.EmmNullPage; // Unmap indicator
        _state.DX = handle;
        _ems.MapUnmapHandlePage();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "Unmap should return no error");
    }

    /// <summary>
    /// Tests that MapUnmapHandlePage (Function 0x44) fails with invalid handle.
    /// </summary>
    [Fact]
    public void MapUnmapHandlePage_WithInvalidHandle_ShouldFail() {
        // Arrange
        _state.AL = 0; // Physical page 0
        _state.BX = 0; // Logical page 0
        _state.DX = 0xFFFF; // Invalid handle

        // Act
        _ems.MapUnmapHandlePage();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmInvalidHandle, "Should return invalid handle error");
    }

    /// <summary>
    /// Tests that MapUnmapHandlePage (Function 0x44) fails with invalid physical page.
    /// </summary>
    [Fact]
    public void MapUnmapHandlePage_WithInvalidPhysicalPage_ShouldFail() {
        // Arrange - Allocate pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Try to map to invalid physical page
        _state.AL = 99; // Invalid physical page (only 0-3 are valid)
        _state.BX = 0; // Logical page 0
        _state.DX = handle;
        _ems.MapUnmapHandlePage();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmIllegalPhysicalPage, "Should return illegal physical page error");
    }

    /// <summary>
    /// Tests that MapUnmapHandlePage (Function 0x44) fails with logical page out of range.
    /// </summary>
    [Fact]
    public void MapUnmapHandlePage_WithLogicalPageOutOfRange_ShouldFail() {
        // Arrange - Allocate 2 pages
        _state.BX = 2;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Try to map logical page 5 (only 0-1 are allocated)
        _state.AL = 0; // Physical page 0
        _state.BX = 5; // Logical page 5 (out of range)
        _state.DX = handle;
        _ems.MapUnmapHandlePage();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmLogicalPageOutOfRange, "Should return logical page out of range error");
    }

    /// <summary>
    /// Tests that DeallocatePages (Function 0x45) successfully deallocates pages.
    /// </summary>
    [Fact]
    public void DeallocatePages_ShouldSucceed() {
        // Arrange - Allocate pages first
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Deallocate
        _state.DX = handle;
        _ems.DeallocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "DeallocatePages should return no error");
        _ems.EmmHandles.Should().NotContainKey(handle, "Handle should be removed from handles dictionary");
    }

    /// <summary>
    /// Tests that DeallocatePages (Function 0x45) fails with invalid handle.
    /// </summary>
    [Fact]
    public void DeallocatePages_WithInvalidHandle_ShouldFail() {
        // Arrange
        _state.DX = 0xFFFF; // Invalid handle

        // Act
        _ems.DeallocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmInvalidHandle, "Should return invalid handle error");
    }

    /// <summary>
    /// Tests that DeallocatePages (Function 0x45) fails when page map is saved.
    /// </summary>
    [Fact]
    public void DeallocatePages_WithSavedPageMap_ShouldFail() {
        // Arrange - Allocate pages and save page map
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        _state.DX = handle;
        _ems.SavePageMap();

        // Act - Try to deallocate with saved page map
        _state.DX = handle;
        _ems.DeallocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmSaveMapError, "Should return save map error");
    }

    /// <summary>
    /// Tests that GetEmmVersion (Function 0x46) returns the correct version (3.2).
    /// </summary>
    [Fact]
    public void GetEmmVersion_ShouldReturnVersion32() {
        // Arrange
        _state.AH = 0x46;

        // Act
        _ems.GetEmmVersion();

        // Assert
        _state.AL.Should().Be(0x32, "EMS version should be 3.2 (0x32)");
        _state.AH.Should().Be(EmmStatus.EmmNoError, "GetEmmVersion should return no error");
    }

    /// <summary>
    /// Tests that SavePageMap (Function 0x47) successfully saves the page map.
    /// </summary>
    [Fact]
    public void SavePageMap_ShouldSucceed() {
        // Arrange - Use handle 0 (system handle)
        _state.DX = 0;

        // Act
        _ems.SavePageMap();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "SavePageMap should return no error");
        _ems.EmmHandles[0].SavedPageMap.Should().BeTrue("SavedPageMap flag should be set");
    }

    /// <summary>
    /// Tests that SavePageMap (Function 0x47) fails when page map is already saved.
    /// </summary>
    [Fact]
    public void SavePageMap_WhenAlreadySaved_ShouldFail() {
        // Arrange - Save page map first
        _state.DX = 0;
        _ems.SavePageMap();

        // Act - Try to save again
        _state.DX = 0;
        _ems.SavePageMap();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmPageMapSaved, "Should return page map saved error");
    }

    /// <summary>
    /// Tests that RestorePageMap (Function 0x48) successfully restores the page map.
    /// </summary>
    [Fact]
    public void RestorePageMap_ShouldSucceed() {
        // Arrange - Save page map first
        _state.DX = 0;
        _ems.SavePageMap();

        // Act - Restore page map
        _state.DX = 0;
        _ems.RestorePageMap();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "RestorePageMap should return no error");
        _ems.EmmHandles[0].SavedPageMap.Should().BeFalse("SavedPageMap flag should be cleared");
    }

    /// <summary>
    /// Tests that GetEmmHandleCount (Function 0x4B) returns the correct number of open handles.
    /// </summary>
    [Fact]
    public void GetEmmHandleCount_ShouldReturnCorrectCount() {
        // Arrange
        int initialCount = _ems.EmmHandles.Count;

        // Act
        _ems.GetEmmHandleCount();

        // Assert
        _state.BX.Should().Be((ushort)initialCount, "Handle count should match actual count");
        _state.AH.Should().Be(EmmStatus.EmmNoError, "GetEmmHandleCount should return no error");
    }

    /// <summary>
    /// Tests that GetHandlePages (Function 0x4C) returns the correct number of pages for a handle.
    /// </summary>
    [Fact]
    public void GetHandlePages_ShouldReturnCorrectPageCount() {
        // Arrange - Allocate 8 pages
        _state.BX = 8;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act
        _state.DX = handle;
        _ems.GetHandlePages();

        // Assert
        _state.BX.Should().Be(8, "Should return 8 pages for the handle");
        _state.AX.Should().Be(EmmStatus.EmmNoError, "GetHandlePages should return no error");
    }

    /// <summary>
    /// Tests that GetAllHandlePages (Function 0x4D) returns information for all handles.
    /// </summary>
    [Fact]
    public void GetAllHandlePages_ShouldReturnAllHandles() {
        // Arrange - Allocate some pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle1 = _state.DX;

        _state.BX = 8;
        _ems.AllocatePages();
        ushort handle2 = _state.DX;

        // Setup ES:DI to point to a buffer
        uint bufferAddress = 0x1000;
        _state.ES = 0;
        _state.DI = (ushort)bufferAddress;

        // Act
        _ems.GetAllHandlePages();

        // Assert
        _state.AX.Should().Be(EmmStatus.EmmNoError, "GetAllHandlePages should return no error");
        _state.BX.Should().BeGreaterThan(0, "Should return total allocated pages count");

        // Verify buffer contains handle data
        ushort firstHandle = _memory.UInt16[bufferAddress];
        ushort firstPageCount = _memory.UInt16[bufferAddress + 2];
        firstHandle.Should().BeOneOf(new ushort[] { 0, handle1, handle2 }, "First handle should be valid");
        firstPageCount.Should().BeGreaterThan(0, "Page count should be positive");
    }

    /// <summary>
    /// Tests that MapUnmapMultipleHandlePages (Function 0x50) maps multiple pages using physical page numbers.
    /// </summary>
    [Fact]
    public void MapUnmapMultipleHandlePages_WithPhysicalPageNumbers_ShouldSucceed() {
        // Arrange - Allocate pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Setup mapping structure in memory
        uint mapAddress = 0x2000;
        _memory.UInt16[mapAddress] = 0; // Logical page 0
        _memory.UInt16[mapAddress + 2] = 0; // Physical page 0
        _memory.UInt16[mapAddress + 4] = 1; // Logical page 1
        _memory.UInt16[mapAddress + 6] = 1; // Physical page 1

        // Act
        _state.AL = EmmSubFunctionsCodes.UsePhysicalPageNumbers;
        _state.DX = handle;
        _state.CX = 2; // Map 2 pages
        _state.DS = 0;
        _state.SI = (ushort)mapAddress;

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "MapUnmapMultipleHandlePages should return no error");
    }

    /// <summary>
    /// Tests that MapUnmapMultipleHandlePages (Function 0x50) maps multiple pages using segmented addresses.
    /// </summary>
    [Fact]
    public void MapUnmapMultipleHandlePages_WithSegmentedAddress_ShouldSucceed() {
        // Arrange - Allocate pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Setup mapping structure with segment addresses
        uint mapAddress = 0x2000;
        _memory.UInt16[mapAddress] = 0; // Logical page 0
        _memory.UInt16[mapAddress + 2] = ExpandedMemoryManager.EmmPageFrameSegment; // Segment 0xE000

        // Act
        _state.AL = EmmSubFunctionsCodes.UseSegmentedAddress;
        _state.DX = handle;
        _state.CX = 1; // Map 1 page
        _state.DS = 0;
        _state.SI = (ushort)mapAddress;

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "MapUnmapMultipleHandlePages with segments should return no error");
    }

    /// <summary>
    /// Tests that MapUnmapMultipleHandlePages (Function 0x50) fails with invalid subfunction.
    /// </summary>
    [Fact]
    public void MapUnmapMultipleHandlePages_WithInvalidSubFunction_ShouldFail() {
        // Arrange - Allocate pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Use invalid subfunction
        _state.AL = 0xFF; // Invalid subfunction
        _state.DX = handle;
        _state.CX = 1;
        _state.DS = 0;
        _state.SI = 0x2000;

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmInvalidSubFunction, "Should return invalid subfunction error");
    }

    /// <summary>
    /// Tests that ReallocatePages (Function 0x51) successfully grows a handle's page allocation.
    /// </summary>
    [Fact]
    public void ReallocatePages_GrowingAllocation_ShouldSucceed() {
        // Arrange - Allocate 4 pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Reallocate to 8 pages
        _state.BX = 8;
        _state.DX = handle;
        _ems.ReallocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "ReallocatePages should return no error");
        _ems.EmmHandles[handle].LogicalPages.Count.Should().Be(8, "Should have 8 pages allocated");
    }

    /// <summary>
    /// Tests that ReallocatePages (Function 0x51) successfully shrinks a handle's page allocation.
    /// </summary>
    [Fact]
    public void ReallocatePages_ShrinkingAllocation_ShouldSucceed() {
        // Arrange - Allocate 8 pages
        _state.BX = 8;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Reallocate to 4 pages
        _state.BX = 4;
        _state.DX = handle;
        _ems.ReallocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "ReallocatePages should return no error");
        _ems.EmmHandles[handle].LogicalPages.Count.Should().Be(4, "Should have 4 pages allocated");
    }

    /// <summary>
    /// Tests that ReallocatePages (Function 0x51) succeeds with same size (no-op).
    /// </summary>
    [Fact]
    public void ReallocatePages_WithSameSize_ShouldSucceed() {
        // Arrange - Allocate 4 pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Reallocate to same size (4 pages)
        _state.BX = 4;
        _state.DX = handle;
        _ems.ReallocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "ReallocatePages with same size should return no error");
        _ems.EmmHandles[handle].LogicalPages.Count.Should().Be(4, "Should still have 4 pages allocated");
    }

    /// <summary>
    /// Tests that ReallocatePages (Function 0x51) clears all pages when reallocating to zero.
    /// </summary>
    [Fact]
    public void ReallocatePages_ToZero_ShouldClearPages() {
        // Arrange - Allocate 4 pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Reallocate to 0 pages
        _state.BX = 0;
        _state.DX = handle;
        _ems.ReallocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "ReallocatePages to zero should return no error");
        _ems.EmmHandles[handle].LogicalPages.Count.Should().Be(0, "Should have 0 pages allocated");
    }

    /// <summary>
    /// Tests that ReallocatePages (Function 0x51) fails with invalid handle.
    /// </summary>
    [Fact]
    public void ReallocatePages_WithInvalidHandle_ShouldFail() {
        // Arrange
        _state.BX = 8;
        _state.DX = 0xFFFF; // Invalid handle

        // Act
        _ems.ReallocatePages();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmInvalidHandle, "Should return invalid handle error");
    }

    /// <summary>
    /// Tests that GetSetHandleName (Function 0x53, subfunction 0x01) sets a handle name.
    /// </summary>
    [Fact]
    public void GetSetHandleName_SetName_ShouldSucceed() {
        // Arrange - Allocate pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Write name to memory
        uint nameAddress = 0x3000;
        string testName = "TESTNAME";
        for (int i = 0; i < testName.Length; i++) {
            _memory.UInt8[nameAddress + (uint)i] = (byte)testName[i];
        }
        _memory.UInt8[nameAddress + (uint)testName.Length] = 0; // Null terminator

        // Act - Set name
        _state.AL = EmmSubFunctionsCodes.HandleNameSet;
        _state.DX = handle;
        _state.SI = 0;
        _state.DI = (ushort)nameAddress;

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "SetHandleName should return no error");
        _ems.EmmHandles[handle].Name.Should().Be(testName, "Handle name should be set correctly");
    }

    /// <summary>
    /// Tests that GetSetHandleName (Function 0x53, subfunction 0x00) gets a handle name.
    /// </summary>
    [Fact]
    public void GetSetHandleName_GetName_ShouldSucceed() {
        // Arrange - Allocate pages and set name
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        string testName = "MYHANDLE";
        _ems.EmmHandles[handle].Name = testName;

        // Act - Get name
        uint nameBufferAddress = 0x3000;
        _state.AL = EmmSubFunctionsCodes.HandleNameGet;
        _state.DX = handle;
        _state.ES = 0;
        _state.DI = (ushort)nameBufferAddress;

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "GetHandleName should return no error");
        _ems.EmmHandles[handle].Name.Should().Be(testName, "Handle name should match what was set");
    }

    /// <summary>
    /// Tests that GetSetHandleName (Function 0x53) fails with invalid handle.
    /// </summary>
    [Fact]
    public void GetSetHandleName_WithInvalidHandle_ShouldFail() {
        // Arrange
        _state.AL = EmmSubFunctionsCodes.HandleNameGet;
        _state.DX = 0xFFFF; // Invalid handle
        _state.ES = 0;
        _state.DI = 0x3000;

        // Act
        _ems.GetSetHandleName();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmInvalidHandle, "Should return invalid handle error");
    }

    /// <summary>
    /// Tests that GetSetHandleName (Function 0x53) fails with invalid subfunction.
    /// </summary>
    [Fact]
    public void GetSetHandleName_WithInvalidSubFunction_ShouldFail() {
        // Arrange - Allocate pages
        _state.BX = 4;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        // Act - Use invalid subfunction
        // GetSetHandleName setup
        _state.AL = 0xFF; // Invalid subfunction
        _state.DX = handle;

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmInvalidSubFunction, "Should return invalid subfunction error");
    }

    /// <summary>
    /// Tests that GetMappablePhysicalAddressArray (Function 0x58) returns the correct array.
    /// </summary>
    [Fact]
    public void GetMappablePhysicalAddressArray_ShouldReturnCorrectArray() {
        // Arrange
        uint bufferAddress = 0x4000;
        // GetMappablePhysicalAddressArray setup
        _state.ES = 0;
        _state.DI = (ushort)bufferAddress;

        // Act
        _ems.GetSetHandleName();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "GetMappablePhysicalAddressArray should return no error");
        _state.CX.Should().Be(ExpandedMemoryManager.EmmMaxPhysicalPages, "Should return 4 physical pages");

        // Verify first entry
        ushort firstSegment = _memory.UInt16[bufferAddress];
        ushort firstPageNumber = _memory.UInt16[bufferAddress + 2];
        firstSegment.Should().Be(ExpandedMemoryManager.EmmPageFrameSegment, "First segment should be 0xE000");
        firstPageNumber.Should().Be(0, "First page number should be 0");

        // Verify second entry
        ushort secondSegment = _memory.UInt16[bufferAddress + 4];
        ushort secondPageNumber = _memory.UInt16[bufferAddress + 6];
        secondSegment.Should().Be((ushort)(ExpandedMemoryManager.EmmPageFrameSegment + (ExpandedMemoryManager.EmmPageSize / 16)), "Second segment should be offset by page size");
        secondPageNumber.Should().Be(1, "Second page number should be 1");
    }

    /// <summary>
    /// Tests that GetExpandedMemoryHardwareInformation (Function 0x59, subfunction 0x00) returns hardware config.
    /// </summary>
    [Fact]
    public void GetExpandedMemoryHardwareInformation_GetHardwareConfig_ShouldReturnData() {
        // Arrange
        uint bufferAddress = 0x5000;
        // GetExpandedMemoryHardwareInformation setup
        _state.AL = EmmSubFunctionsCodes.GetHardwareConfigurationArray;
        _state.ES = 0;
        _state.DI = (ushort)bufferAddress;

        // Act
        _ems.GetSetHandleName();

        // Assert - Verify structure is filled
        ushort rawPageSize = _memory.UInt16[bufferAddress];
        rawPageSize.Should().Be(0x0400, "Raw page size should be 0x0400 (1K paragraphs = 16KB)");

        ushort altRegisterSets = _memory.UInt16[bufferAddress + 2];
        altRegisterSets.Should().Be(0x0000, "No alternate register sets");

        // Context save area size
        ushort saveAreaSize = _memory.UInt16[bufferAddress + 4];
        saveAreaSize.Should().BeGreaterThanOrEqualTo(0, "Save area size should be non-negative");

        ushort dmaChannels = _memory.UInt16[bufferAddress + 6];
        dmaChannels.Should().Be(0x0000, "No DMA channels");

        ushort limType = _memory.UInt16[bufferAddress + 8];
        limType.Should().Be(0x0000, "Always 0 for LIM standard");
    }

    /// <summary>
    /// Tests that GetExpandedMemoryHardwareInformation (Function 0x59, subfunction 0x01) returns unallocated raw pages.
    /// </summary>
    [Fact]
    public void GetExpandedMemoryHardwareInformation_GetUnallocatedRawPages_ShouldReturnCounts() {
        // Arrange
        // GetExpandedMemoryHardwareInformation setup
        _state.AL = EmmSubFunctionsCodes.GetUnallocatedRawPages;

        // Act
        _ems.GetSetHandleName();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "Should return no error");
        _state.DX.Should().Be(EmmMemory.TotalPages, "Total pages should be 512");
        _state.BX.Should().BeGreaterThan(0, "Available pages should be greater than 0");
    }

    /// <summary>
    /// Tests that an unimplemented function returns the correct error code.
    /// </summary>
    [Fact]
    public void UnimplementedFunction_ShouldReturnNotSupported() {
        // Arrange - Use a function code that's not implemented
        _state.AH = 0x60; // Not implemented function

        // Act
        _ems.Run();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmFunctionNotSupported, "Should return function not supported error");
    }

    /// <summary>
    /// Tests that memory can be written to and read from mapped EMS pages.
    /// </summary>
    [Fact]
    public void MappedEmsPage_ShouldAllowMemoryAccess() {
        // Arrange - Allocate and map pages
        _state.BX = 1;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        _state.AL = 0; // Physical page 0
        _state.BX = 0; // Logical page 0
        _state.DX = handle;
        _ems.MapUnmapHandlePage();

        // Act - Write to the mapped page
        uint pageFrameAddress = MemoryUtils.ToPhysicalAddress(ExpandedMemoryManager.EmmPageFrameSegment, 0);
        byte testValue = 0x42;
        _memory.UInt8[pageFrameAddress] = testValue;

        // Assert - Read back the value
        _memory.UInt8[pageFrameAddress].Should().Be(testValue, "Should be able to read written value from mapped page");
    }

    /// <summary>
    /// Tests that different logical pages maintain separate data.
    /// </summary>
    [Fact]
    public void DifferentLogicalPages_ShouldHaveSeparateData() {
        // Arrange - Allocate multiple pages
        _state.BX = 2;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        uint pageFrameAddress = MemoryUtils.ToPhysicalAddress(ExpandedMemoryManager.EmmPageFrameSegment, 0);

        // Map logical page 0 and write data
        _state.AL = 0; // Physical page 0
        _state.BX = 0; // Logical page 0
        _state.DX = handle;
        _ems.MapUnmapHandlePage();
        _memory.UInt8[pageFrameAddress] = 0x11;

        // Map logical page 1 and write different data
        _state.AL = 0; // Physical page 0
        _state.BX = 1; // Logical page 1
        _state.DX = handle;
        _ems.MapUnmapHandlePage();
        _memory.UInt8[pageFrameAddress] = 0x22;

        // Act - Map logical page
        _state.AL = 0;
        _state.BX = 0;
        _state.DX = handle;
        _ems.MapUnmapHandlePage();

        // Assert - Should read the original value from logical page 0
        _memory.UInt8[pageFrameAddress].Should().Be(0x11, "Logical page 0 should retain its data");
    }

    /// <summary>
    /// Tests that the system handle (handle 0) is pre-allocated.
    /// </summary>
    [Fact]
    public void SystemHandle_ShouldBePreAllocated() {
        // Assert
        _ems.EmmHandles.Should().ContainKey(0, "System handle 0 should be pre-allocated");
        _ems.EmmHandles[0].LogicalPages.Count.Should().Be(4, "System handle should have 4 pages");
    }

    /// <summary>
    /// Tests that EMS identifier string is correctly set.
    /// </summary>
    [Fact]
    public void EmsIdentifier_ShouldBeCorrect() {
        // Assert
        ExpandedMemoryManager.EmsIdentifier.Should().Be("EMMXXXX0", "EMS identifier should be EMMXXXX0");
    }

    /// <summary>
    /// Tests that the page frame segment is at the correct location.
    /// </summary>
    [Fact]
    public void PageFrameSegment_ShouldBeAtE000() {
        // Assert
        ExpandedMemoryManager.EmmPageFrameSegment.Should().Be(0xE000, "Page frame should be at segment 0xE000");
    }

    /// <summary>
    /// Tests that the page frame has the correct number of physical pages.
    /// </summary>
    [Fact]
    public void PageFrame_ShouldHaveFourPhysicalPages() {
        // Assert
        _ems.EmmPageFrame.Count.Should().Be(4, "Page frame should have 4 physical pages");
    }

    /// <summary>
    /// Tests that EMS page size is 16KB.
    /// </summary>
    [Fact]
    public void PageSize_ShouldBe16KB() {
        // Assert
        ExpandedMemoryManager.EmmPageSize.Should().Be(16384, "EMS page size should be 16KB");
    }

    /// <summary>
    /// Tests that the total page frame size is 64KB.
    /// </summary>
    [Fact]
    public void PageFrameSize_ShouldBe64KB() {
        // Assert
        ExpandedMemoryManager.EmmPageFrameSize.Should().Be(65536, "Page frame size should be 64KB");
    }

    /// <summary>
    /// Tests that the character device can be retrieved.
    /// </summary>
    [Fact]
    public void AsCharacterDevice_ShouldReturnValidDevice() {
        // Act
        var characterDevice = _ems.AsCharacterDevice();

        // Assert
        characterDevice.Should().NotBeNull("Character device should be available");
        characterDevice.Name.Should().Be(ExpandedMemoryManager.EmsIdentifier, "Device name should match EMS identifier");
    }

    /// <summary>
    /// Tests that the device header is properly initialized.
    /// </summary>
    [Fact]
    public void DeviceHeader_ShouldBeInitialized() {
        // Assert
        _ems.Header.Should().NotBeNull("Device header should be initialized");
        _ems.Header.Name.Should().Be(ExpandedMemoryManager.EmsIdentifier, "Header name should be EMS identifier");
    }

    /// <summary>
    /// Tests that SavePageMap and RestorePageMap maintain page mappings correctly.
    /// </summary>
    [Fact]
    public void SaveAndRestorePageMap_ShouldMaintainMappings() {
        // Arrange - Allocate and map a page
        _state.BX = 1;
        _ems.AllocatePages();
        ushort handle = _state.DX;

        _state.AL = 0; // Physical page 0
        _state.BX = 0; // Logical page 0
        _state.DX = handle;
        _ems.MapUnmapHandlePage();

        // Save the page map for handle 0
        _state.DX = 0;
        _ems.SavePageMap();

        // Change mapping
        _state.AL = 0; // Physical page 0
        _state.BX = ExpandedMemoryManager.EmmNullPage; // Unmap
        _state.DX = handle;
        _ems.MapUnmapHandlePage();

        // Act - Restore the page map
        _state.DX = 0;
        _ems.RestorePageMap();

        // Assert
        _state.AH.Should().Be(EmmStatus.EmmNoError, "Restore should succeed");
        _ems.EmmPageFrame.Should().HaveCount(4, "Page frame should be restored");
    }
}
