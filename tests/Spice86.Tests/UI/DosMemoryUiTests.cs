namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using FluentAssertions;

using Spice86.Core.CLI.RuntimeOptions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.ViewModels;
using Spice86.Views;

/// <summary>
/// Verifies DOS memory views render conventional and upper-memory data independently.
/// </summary>
public class DosMemoryUiTests : BreakpointUiTestBase {
    /// <summary>
    /// Verifies the MCB inspector renders allocatable UMBs once for either UMB chain state and the
    /// system memory table keeps conventional and system totals distinct while displaying KB units.
    /// </summary>
    [AvaloniaFact]
    public void DosMemoryViewsSeparateConventionalAndUpperMemory() {
        // Arrange
        ILogger loggerService = CreateMockLoggerService();
        DosMemoryManager memoryManager = CreateUpperMemoryManager(loggerService);
        PauseHandler pauseHandler = CreatePauseHandler(loggerService);
        pauseHandler.RequestPause();

        // Act & Assert
        AssertMcbView(memoryManager, pauseHandler);
        memoryManager.SetUmbChainLinkState(1).Should().BeTrue();
        AssertMcbView(memoryManager, pauseHandler);
        AssertMemoryOverview(memoryManager, pauseHandler);
    }

    private static DosMemoryManager CreateUpperMemoryManager(ILogger loggerService) {
        (Memory memory, _, _) = CreateMemory();
        const ushort programEntryPointSegment = 0x1000;
        const ushort initialPspSegment = programEntryPointSegment - 0x10;
        DosSwappableDataArea swappableDataArea = new(memory,
            MemoryUtils.ToPhysicalAddress(DosSwappableDataArea.BaseSegment, 0));
        swappableDataArea.CurrentProgramSegmentPrefix = initialPspSegment;
        DosOptions options = new(null, string.Empty, programEntryPointSegment, false, false, false,
            new DosRuntimeState(null));
        NullDevice nullDevice = new(loggerService, memory, 0);
        DosSysVars dosSysVars = new(options, nullDevice, memory,
            MemoryUtils.ToPhysicalAddress(DosSysVars.Segment, 0));
        return new DosMemoryManager(memory, initialPspSegment, loggerService, dosSysVars, true, false);
    }

    private static void AssertMcbView(DosMemoryManager memoryManager, PauseHandler pauseHandler) {
        using DosMcbsViewModel viewModel = new(memoryManager, pauseHandler);
        DosMcbsView view = new() {
            DataContext = viewModel
        };
        Window window = new() {
            Width = 900,
            Height = 600,
            Content = view
        };

        ShowWindowAndWait(window);

        viewModel.Items.Should().HaveCount(2);
        viewModel.Items.Select(static item => item.HeaderSegment).Should().NotContain("0x9FFF");
        viewModel.Blocks.Should().HaveCount(2);
        viewModel.TotalMcbCount.Should().Be(1);
        viewModel.TotalUpperMemoryMcbCount.Should().Be(1);
        viewModel.LayoutSummary.Should().Contain("Conventional:");
        viewModel.LayoutSummary.Should().Contain("UMB:");
        Grid? barHost = view.FindControl<Grid>("BarHost");
        barHost.Should().NotBeNull();
        if (barHost is null) {
            throw new InvalidOperationException("BarHost was not found in the DOS MCB view.");
        }
        barHost.Children.Should().HaveCount(2);

        CloseWindowAndWait(window);
    }

    private static void AssertMemoryOverview(DosMemoryManager memoryManager, PauseHandler pauseHandler) {
        using DosMemoryOverviewViewModel viewModel = new(memoryManager, null, null, pauseHandler);
        DosMemoryOverviewView view = new() {
            DataContext = viewModel
        };
        Window window = new() {
            Width = 1000,
            Height = 800,
            Content = view
        };

        ShowWindowAndWait(window);

        MemoryReportRow conventionalMemory = viewModel.Rows.Single(
            static row => row.Category == "Conventional memory");
        MemoryReportRow upperMemory = viewModel.Rows.Single(
            static row => row.Category == "Upper memory (UMB)");
        MemoryReportRow systemTotal = viewModel.Rows.Single(
            static row => row.Category == "System total");
        conventionalMemory.TotalKilobytes.Should().Be(
            conventionalMemory.TotalBytes / 1024.0);
        systemTotal.TotalBytes.Should().Be(
            conventionalMemory.TotalBytes + upperMemory.TotalBytes);
        DataGrid memorySummaryGrid = view.GetVisualDescendants().OfType<DataGrid>().Single();
        IEnumerable<object?> headers = memorySummaryGrid.Columns.Select(static column => column.Header);
        headers.Should().Contain("Total (KB)");
        headers.Should().Contain("Used (KB)");
        headers.Should().Contain("Free (KB)");

        CloseWindowAndWait(window);
    }
}
