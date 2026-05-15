namespace Spice86.Tests.Dos.Ems;

using FluentAssertions;

using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Integration tests for EMS functionality that run machine code through the emulation stack.
/// </summary>
public class EmsIntegrationTests {
    private enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    [Fact]
    public void EmsDetection_ViaInterruptVector_ShouldBePresent() {
        AssertEmsResourcePasses("ems_detection_interrupt_vector.com", "INT 67h should be functional");
    }

    [Fact]
    public void EmsDetection_ViaDeviceDriverName_ShouldFindEMMXXXX0() {
        AssertEmsResourcePasses("ems_detection_device_name.com", "EMMXXXX0 device should be accessible");
    }

    [Fact]
    public void EmsGetStatus_ShouldReturnNoError() {
        AssertEmsResourcePasses("ems_get_status.com", "EMS GetStatus should return no error");
    }

    [Fact]
    public void EmsGetPageFrameSegment_ShouldReturnE000() {
        AssertEmsResourcePasses("ems_get_page_frame_segment.com", "GetPageFrameSegment should return 0xE000");
    }

    [Fact]
    public void EmsGetUnallocatedPageCount_ShouldReturnValidCounts() {
        AssertEmsResourcePasses("ems_get_unallocated_page_count.com", "GetUnallocatedPageCount should return valid counts");
    }

    [Fact]
    public void EmsAllocatePages_ShouldReturnValidHandle() {
        AssertEmsResourcePasses("ems_allocate_pages.com", "AllocatePages should return a valid handle");
    }

    [Fact]
    public void EmsMapHandlePage_ShouldSucceed() {
        AssertEmsResourcePasses("ems_map_handle_page.com", "MapHandlePage should succeed");
    }

    [Fact]
    public void EmsMemoryAccess_ThroughMappedPages_ShouldWork() {
        AssertEmsResourcePasses("ems_memory_access_mapped_pages.com", "EMS memory should be readable and writable");
    }

    [Fact]
    public void EmsDeallocatePages_ShouldSucceed() {
        AssertEmsResourcePasses("ems_deallocate_pages.com", "DeallocatePages should succeed");
    }

    [Fact]
    public void EmsGetVersion_ShouldReturn32() {
        AssertEmsResourcePasses("ems_get_version.com", "GetEmmVersion should return 3.2");
    }

    [Fact]
    public void EmsSavePageMap_ShouldSucceed() {
        AssertEmsResourcePasses("ems_save_page_map.com", "SavePageMap should succeed");
    }

    [Fact]
    public void EmsRestorePageMap_ShouldSucceed() {
        AssertEmsResourcePasses("ems_restore_page_map.com", "RestorePageMap should succeed");
    }

    [Fact]
    public void EmsGetHandleCount_ShouldReturnValidCount() {
        AssertEmsResourcePasses("ems_get_handle_count.com", "GetHandleCount should return valid count");
    }

    [Fact]
    public void EmsGetHandlePages_ShouldReturnCorrectCount() {
        AssertEmsResourcePasses("ems_get_handle_pages.com", "GetHandlePages should return correct count");
    }

    [Fact]
    public void EmsAllocateZeroPages_ShouldFail() {
        AssertEmsResourcePasses("ems_allocate_zero_pages.com", "Allocating zero pages should fail with error 89h");
    }

    [Fact]
    public void EmsMapWithInvalidHandle_ShouldFail() {
        AssertEmsResourcePasses("ems_map_invalid_handle.com", "Mapping with invalid handle should fail with error 83h");
    }

    [Fact]
    public void EmsLogicalPages_ShouldBeIndependent() {
        AssertEmsResourcePasses("ems_logical_pages_independent.com", "Logical pages should maintain independent data");
    }

    [Fact]
    public void EmsMapWithPhysicalPage4_ShouldFail() {
        AssertEmsResourcePasses("ems_map_physical_page_4.com", "Physical page 4 should be rejected (only 0-3 valid)");
    }

    [Fact]
    public void EmsHandleAllocationAfterDeallocation_ShouldNotCollide() {
        AssertEmsResourcePasses("ems_handle_allocation_after_deallocation.com", "Handle2's data should not be corrupted after handle1 deallocation");
    }

    private static void AssertEmsResourcePasses(string resourceName, string because) {
        TestIoPortHandler testHandler = RunEmsResource(resourceName, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, because);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    private static TestIoPortHandler RunEmsResource(string resourceName, bool enableEms) {
        string filePath = Path.Join(AppContext.BaseDirectory, "Resources", "EmsTests", resourceName);
        if (!string.Equals(Path.GetExtension(filePath), ".com", StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("EMS resource tests require a DOS COM program.", nameof(resourceName));
        }

        using Spice86Creator creator = new(
            binName: filePath,
            enablePit: true,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: enableEms
        );
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();

        TestIoPortHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

}
