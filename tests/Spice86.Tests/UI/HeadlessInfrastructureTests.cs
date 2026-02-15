namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

using FluentAssertions;

using Spice86.Views;

using Xunit;

/// <summary>
/// Basic UI tests to verify the headless testing infrastructure works correctly.
/// </summary>
public class HeadlessInfrastructureTests {
    /// <summary>
    /// Verifies that the headless testing infrastructure is correctly configured
    /// by creating and showing a basic window.
    /// </summary>
    [AvaloniaFact]
    public void TestHeadlessWindowCanBeShown() {
        // Arrange
        Window window = new() {
            Width = 800,
            Height = 600,
            Title = "Test Window"
        };

        // Act
        window.Show();

        // Assert
        window.IsVisible.Should().BeTrue();
        window.Width.Should().Be(800);
        window.Height.Should().Be(600);

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that the DebugWindow can be created and shown in headless mode.
    /// </summary>
    [AvaloniaFact]
    public void TestDebugWindowCanBeCreated() {
        // Arrange & Act
        DebugWindow debugWindow = new();

        // Assert
        debugWindow.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the DisassemblyView can be created in headless mode.
    /// </summary>
    [AvaloniaFact]
    public void TestDisassemblyViewCanBeCreated() {
        // Arrange & Act
        DisassemblyView disassemblyView = new();

        // Assert
        disassemblyView.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the BreakpointsView can be created in headless mode.
    /// </summary>
    [AvaloniaFact]
    public void TestBreakpointsViewCanBeCreated() {
        // Arrange & Act
        BreakpointsView breakpointsView = new();

        // Assert
        breakpointsView.Should().NotBeNull();
    }
}




