namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.Input;

using FluentAssertions;

using Spice86.ViewModels;
using Spice86.ViewModels.Services;

public class MemoryViewUiTests : BreakpointUiTestBase {
    private static readonly byte[] DuneAsciiPayload = [0x44, 0x55, 0x4E, 0x45, 0x32];
    private const string DuneSearchValue = "DUNE2";

    [AvaloniaFact]
    public async Task FirstOccurrence_Reinvoked_ReappliesSelectionOnFoundAddress() {
        // Arrange
        uint searchAddress = 0x120;
        MemoryViewHarness harness = ArrangeMemoryView(
            startAddress: "0x100",
            endAddress: "0x3FF",
            searchPayloadAddress: searchAddress,
            searchPayload: DuneAsciiPayload,
            memorySearchValue: DuneSearchValue,
            useAsciiSearch: true);
        ulong expectedByteIndex = searchAddress - 0x100;

        // Act
        await ((IAsyncRelayCommand)harness.ViewModel.FirstOccurrenceCommand).ExecuteAsync(null);
        ProcessUiEvents();
        ProcessUiEvents();

        harness.HexEditor.Selection.Range = new BitRange(new BitLocation(0), new BitLocation(1));

        await harness.ViewModel.FirstOccurrenceCommand.ExecuteAsync(null);
        ProcessUiEvents();
        ProcessUiEvents();

        // Assert
        harness.HexEditor.Selection.Range.Start.ByteIndex.Should().Be(expectedByteIndex);

        // Cleanup
        CloseWindowAndWait(harness.Window);
    }

    [AvaloniaFact]
    public async Task FirstOccurrence_WhenFoundAddressIsOutsideCurrentWindow_ReframesAndSelectsFoundAddress() {
        // Arrange
        uint searchAddress = 0x0200;
        MemoryViewHarness harness = ArrangeMemoryView(
            startAddress: "0x8000",
            endAddress: "0x8FFF",
            searchPayloadAddress: searchAddress,
            searchPayload: DuneAsciiPayload,
            memorySearchValue: DuneSearchValue,
            useAsciiSearch: true);

        // Act
        await harness.ViewModel.FirstOccurrenceCommand.ExecuteAsync(null);
        ProcessUiEvents();
        ProcessUiEvents();

        // Assert
        harness.ViewModel.AddressOFoundOccurence.Should().Be(searchAddress);
        AddressAndValueParser.TryParseAddressString(harness.ViewModel.StartAddress, harness.State, out uint? reframedStartAddress).Should().BeTrue();
        reframedStartAddress.Should().NotBeNull();
        reframedStartAddress.Value.Should().Be(searchAddress);
        harness.HexEditor.Selection.Range.Start.ByteIndex.Should().Be(0UL);

        // Cleanup
        CloseWindowAndWait(harness.Window);
    }
}
