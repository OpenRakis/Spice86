namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using FluentAssertions;

using Spice86.ViewModels;
using Spice86.Views;

using Xunit;

[Collection(HttpApiUiCollection.Name)]
public sealed class HttpApiWindowUiTests {
    private readonly HttpApiUiFixture _fixture;

    public HttpApiWindowUiTests(HttpApiUiFixture fixture) {
        _fixture = fixture;
    }

    [AvaloniaFact]
    public async Task RefreshStatus_ShowsServerState() {
        HttpApiViewModel viewModel = new(true);
        HttpApiWindow window = new() {
            DataContext = viewModel
        };

        window.Show();
        await viewModel.RefreshStatusCommand.ExecuteAsync(null);

        viewModel.Status.Should().Be("Running");
        viewModel.CpuPointer.Should().Be("1234:5678");
        viewModel.Cycles.Should().Be(64);

        window.Close();
        viewModel.Dispose();
    }

    [AvaloniaFact]
    public async Task ReadWriteByte_UsesHttpApiEndToEnd() {
        HttpApiViewModel viewModel = new(true);
        HttpApiWindow window = new() {
            DataContext = viewModel
        };

        window.Show();

        viewModel.MemoryAddressInput = "0x40";
        await viewModel.ReadByteCommand.ExecuteAsync(null);
        viewModel.LastReadValue.Should().Be("0x12");

        viewModel.MemoryValueInput = "0xAB";
        await viewModel.WriteByteCommand.ExecuteAsync(null);
        _fixture.Memory[0x40].Should().Be(0xAB);

        await viewModel.ReadByteCommand.ExecuteAsync(null);
        viewModel.LastReadValue.Should().Be("0xAB");

        window.Close();
        viewModel.Dispose();
    }

    [AvaloniaFact]
    public async Task InvalidAddress_ShowsValidationMessage() {
        HttpApiViewModel viewModel = new(true);
        HttpApiWindow window = new() {
            DataContext = viewModel
        };

        window.Show();

        viewModel.MemoryAddressInput = "-1";
        await viewModel.ReadByteCommand.ExecuteAsync(null);

        viewModel.LastMessage.Should().Be("Invalid address");

        window.Close();
        viewModel.Dispose();
    }
}
