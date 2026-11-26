namespace Spice86.Tests.Dos.Ems;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Utils;

using System.Text.Json;

using Xunit;

/// <summary>
/// Checks <see cref="ExpandedMemoryManager.CaptureState" /> and <see cref="ExpandedMemoryManager.RestoreState" />:
/// capture, change the driver through its EMS functions and the page frame, restore, and check that the
/// functions and the page frame answer as they did at capture.
/// </summary>
public sealed class EmsStateTests {
    private const string HandleName = "SAVED";

    private readonly Memory _memory;
    private readonly ExpandedMemoryManager _ems;

    public EmsStateTests() {
        State state = new(CpuModel.INTEL_80286);
        _memory = new Memory(new(), new Ram(A20Gate.EndOfHighMemoryArea), new A20Gate(false), new RealModeMmu386(), false);
        _ems = new ExpandedMemoryManager(_memory, Substitute.For<IFunctionHandlerProvider>(), new Stack(_memory, state),
            state, new ServiceCallEvents(new HashSet<byte>(), false), Substitute.For<ILogger>());
    }

    [Fact]
    public void AllocationAfterEachRestoreReturnsTheHandleItReturnedAfterCapture() {
        // Arrange
        EmsState captured = _ems.CaptureState();
        ushort firstHandle = Allocate(1);

        // Act
        _ems.RestoreState(captured);
        ushort afterFirstRestore = Allocate(1);
        _ems.RestoreState(captured);
        ushort afterSecondRestore = Allocate(1);

        // Assert
        afterFirstRestore.Should().Be(firstHandle);
        afterSecondRestore.Should().Be(firstHandle);
    }

    [Fact]
    public void RestoreBringsBackThePageFrameMappingAndThePageContent() {
        // Arrange
        ushort handle = Allocate(2);
        Map(0, 0, handle);
        WritePageFrame(0, 0x42);
        Map(1, 1, handle);
        WritePageFrame(1, 0x43);
        EmsState captured = _ems.CaptureState();

        // Act
        Map(1, 0, handle);
        WritePageFrame(0, 0x99);
        _ems.DeallocatePages(handle).Should().Be(EmmStatus.EmmNoError);
        _ems.RestoreState(captured);

        // Assert
        ReadPageFrame(0).Should().Be(0x42);
        ReadPageFrame(1).Should().Be(0x43);
        Map(1, 0, handle);
        ReadPageFrame(0).Should().Be(0x43, "logical page 1 held 0x43 at capture");
    }

    [Fact]
    public void RestoreBringsBackTheSavedPageMap() {
        // Arrange
        ushort handle = Allocate(2);
        Map(0, 0, handle);
        WritePageFrame(0, 0x42);
        _ems.SavePageMap(handle).Should().Be(EmmStatus.EmmNoError);
        EmsState captured = _ems.CaptureState();

        // Act
        _ems.RestorePageMap(handle).Should().Be(EmmStatus.EmmNoError);
        Map(1, 0, handle);
        _ems.RestoreState(captured);

        // Assert
        _ems.SavePageMap(handle).Should().Be(EmmStatus.EmmPageMapSaved);
        Map(1, 0, handle);
        _ems.RestorePageMap(handle).Should().Be(EmmStatus.EmmNoError);
        ReadPageFrame(0).Should().Be(0x42, "the saved map puts logical page 0 back at physical page 0");
    }

    [Fact]
    public void StateReadBackFromJsonRestoresTheSameDriver() {
        // Arrange
        ushort handle = Allocate(1);
        Map(0, 0, handle);
        WritePageFrame(0, 0x42);
        _ems.SetHandleName(handle, HandleName);
        string json = JsonSerializer.Serialize(_ems.CaptureState());

        // Act
        WritePageFrame(0, 0x99);
        _ems.DeallocatePages(handle);
        EmsState? readBack = JsonSerializer.Deserialize<EmsState>(json);
        _ems.RestoreState(readBack ?? throw new InvalidOperationException("The state did not deserialize."));

        // Assert
        ReadPageFrame(0).Should().Be(0x42);
        _ems.EmmHandles[handle].Name.Should().Be(HandleName);
        Allocate(1).Should().Be((ushort)(handle + 1));
    }

    private ushort Allocate(ushort pages) {
        (byte status, ushort handle) = _ems.AllocatePageHandle(pages, 0);
        status.Should().Be(EmmStatus.EmmNoError);
        return handle;
    }

    private void Map(ushort logicalPage, ushort physicalPage, ushort handle) {
        _ems.MapUnmapHandlePage(logicalPage, physicalPage, handle).Should().Be(EmmStatus.EmmNoError);
    }

    private void WritePageFrame(ushort physicalPage, byte value) {
        _memory.UInt8[PageFrameAddress(physicalPage)] = value;
    }

    private byte ReadPageFrame(ushort physicalPage) {
        return _memory.UInt8[PageFrameAddress(physicalPage)];
    }

    private static uint PageFrameAddress(ushort physicalPage) {
        return MemoryUtils.ToPhysicalAddress(ExpandedMemoryManager.EmmPageFrameSegment,
            (ushort)(physicalPage * ExpandedMemoryManager.EmmPageSize));
    }
}
