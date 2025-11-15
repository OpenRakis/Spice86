namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels;

using Xunit;

/// <summary>
/// Tests for the BreakpointDialogViewModel.
/// </summary>
public class BreakpointDialogViewModelTests {
    [Fact]
    public void Constructor_SetsAddressCorrectly() {
        // Arrange
        var address = new SegmentedAddress(0x1000, 0x0100);
        
        // Act
        var viewModel = new BreakpointDialogViewModel(address);
        
        // Assert
        viewModel.Address.Should().Be("1000:0100");
        viewModel.Condition.Should().Be(string.Empty);
        viewModel.DialogResult.Should().BeFalse();
    }
    
    [Fact]
    public void OkCommand_SetsDialogResultToTrue() {
        // Arrange
        var address = new SegmentedAddress(0x1000, 0x0100);
        var viewModel = new BreakpointDialogViewModel(address);
        
        // Act
        viewModel.OkCommand.Execute(null);
        
        // Assert
        viewModel.DialogResult.Should().BeTrue();
    }
    
    [Fact]
    public void CancelCommand_SetsDialogResultToFalse() {
        // Arrange
        var address = new SegmentedAddress(0x1000, 0x0100);
        var viewModel = new BreakpointDialogViewModel(address);
        
        // Act
        viewModel.CancelCommand.Execute(null);
        
        // Assert
        viewModel.DialogResult.Should().BeFalse();
    }
    
    [Fact]
    public void Condition_CanBeSetAndRetrieved() {
        // Arrange
        var address = new SegmentedAddress(0x1000, 0x0100);
        var viewModel = new BreakpointDialogViewModel(address);
        var expectedCondition = "ax == 0x1234";
        
        // Act
        viewModel.Condition = expectedCondition;
        
        // Assert
        viewModel.Condition.Should().Be(expectedCondition);
    }
}
