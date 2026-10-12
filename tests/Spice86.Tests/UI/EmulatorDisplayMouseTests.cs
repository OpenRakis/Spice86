namespace Spice86.Tests.UI;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Tests.UI.CfgCpu;
using Spice86.ViewModels;

using Xunit;

public class EmulatorDisplayMouseTests {
    [AvaloniaTheory]
    [InlineData(320, 200, 0, 0, 48, 16)]
    [InlineData(320, 200, 319, 199, 688, 416)]
    [InlineData(960, 600, 0, 0, 48, 16)]
    [InlineData(960, 600, 959, 599, 688, 416)]
    public void PointerAtLastDisplayPixel_ReachesInclusiveDosMouseRange(
        int width, int height, int x, int y, int expectedX, int expectedY) {
        // Arrange
        SharedMouseData mouseData = new() {
            CurrentMinX = 48,
            CurrentMaxX = 688,
            CurrentMinY = 16,
            CurrentMaxY = 416
        };
        using EmulatorDisplayViewModel display = new(new InlineUIDispatcher(),
            Substitute.For<IPauseHandler>(), mouseData, NullLogger.Instance);
        display.UpdateResolution(320, 200, 1.0);
        display.MouseMoved += (_, e) => {
            mouseData.MouseXRelative = e.X;
            mouseData.MouseYRelative = e.Y;
        };
        Image image = new() { Source = display.Bitmap };
        image.PointerMoved += (_, e) => display.OnMouseMoved(e, image);
        Window window = new() {
            Width = width,
            Height = height,
            Content = new Viewbox { Child = image }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try {
            // Act
            window.MouseMove(new Point(x, y));

            // Assert
            MouseStatusRecord status = mouseData.CurrentMouseStatus;
            status.X.Should().Be(expectedX);
            status.Y.Should().Be(expectedY);
            display.MouseX.Should().BeInRange(0.0, 1.0);
            display.MouseY.Should().BeInRange(0.0, 1.0);
        } finally {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
