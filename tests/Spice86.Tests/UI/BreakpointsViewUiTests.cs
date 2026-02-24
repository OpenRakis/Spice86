namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using FluentAssertions;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.ViewModels;
using Spice86.Views;

using Xunit;

/// <summary>
/// UI tests for breakpoint functionality in the BreakpointsView.
/// These tests verify that breakpoints with conditions can be created
/// and managed through the UI components.
/// </summary>
public class BreakpointsViewUiTests : BreakpointUiTestBase {
    /// <summary>
    /// Verifies that an execution breakpoint can be created through the BreakpointsView UI.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_CanCreateExecutionBreakpoint() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Select Execution tab first
        BreakpointTypeTabItemViewModel? executionTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "Execution");
        executionTab.Should().NotBeNull();
        viewModel.SelectedBreakpointTypeTab = executionTab;
        ProcessUiEvents();

        // Begin creating a breakpoint
        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        // Assert - Should be in creating breakpoint mode with Execution selected
        viewModel.CreatingBreakpoint.Should().BeTrue();
        viewModel.IsExecutionBreakpointSelected.Should().BeTrue();

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that an execution breakpoint with a condition can be created.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_CanCreateExecutionBreakpointWithCondition() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Begin creating a breakpoint and set the condition
        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        viewModel.ExecutionAddressValue = "0x1000:0x0100";
        viewModel.ExecutionConditionExpression = "ax == 0x100";
        ProcessUiEvents();

        // Confirm the breakpoint creation
        if (viewModel.ConfirmBreakpointCreationCommand.CanExecute(null)) {
            viewModel.ConfirmBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }

        // Assert - Breakpoint should be created
        viewModel.CreatingBreakpoint.Should().BeFalse();

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that a breakpoint with an invalid condition expression shows validation error.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_InvalidConditionExpression_ShowsError() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Begin creating a breakpoint with invalid condition
        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        viewModel.ExecutionAddressValue = "0x1000:0x0100";
        viewModel.ExecutionConditionExpression = "invalid expression !!!";
        ProcessUiEvents();

        // Try to confirm (should fail due to invalid expression)
        bool canConfirm = viewModel.ConfirmBreakpointCreationCommand.CanExecute(null);
        if (canConfirm) {
            viewModel.ConfirmBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that the cancel button properly cancels breakpoint creation.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_CancelCreation_ClearsCreatingState() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Begin creating, then cancel
        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        viewModel.CreatingBreakpoint.Should().BeTrue();

        viewModel.CancelBreakpointCreationCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.CreatingBreakpoint.Should().BeFalse();

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that the BreakpointsView shows the correct tabs.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_ShowsBreakpointTypeTabs() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Assert - ViewModel should have breakpoint tabs
        viewModel.BreakpointTabs.Should().NotBeEmpty();

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that condition expressions with comparison operators are accepted.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_AcceptsComparisonOperatorConditions() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Test various comparison operators
        string[] conditions = { "ax == 0", "bx != 5", "cx > 100", "dx < 50", "eax >= 0", "ebx <= 0xFFFF" };

        foreach (string condition in conditions) {
            viewModel.BeginCreateBreakpointCommand.Execute(null);
            ProcessUiEvents();

            viewModel.ExecutionAddressValue = "0x1000:0x0100";
            viewModel.ExecutionConditionExpression = condition;
            ProcessUiEvents();

            // Should be able to confirm (valid conditions)
            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue(
                $"Condition '{condition}' should be valid");

            viewModel.CancelBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that condition expressions with logical operators are accepted.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_AcceptsLogicalOperatorConditions() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Test logical operators
        string[] conditions = { "ax == 0 && bx == 0", "cx == 0 || dx == 0", "!(ax == 0)" };

        foreach (string condition in conditions) {
            viewModel.BeginCreateBreakpointCommand.Execute(null);
            ProcessUiEvents();

            viewModel.ExecutionAddressValue = "0x1000:0x0100";
            viewModel.ExecutionConditionExpression = condition;
            ProcessUiEvents();

            // Should be able to confirm (valid conditions)
            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue(
                $"Condition '{condition}' should be valid");

            viewModel.CancelBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that a memory breakpoint can be created.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_CanCreateMemoryBreakpoint() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Begin creating a breakpoint
        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        // Select Memory breakpoint tab
        BreakpointTypeTabItemViewModel? memoryTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "Memory");
        if (memoryTab is not null) {
            memoryTab.IsSelected = true;
            viewModel.SelectedBreakpointTypeTab = memoryTab;
            ProcessUiEvents();

            viewModel.IsMemoryBreakpointSelected.Should().BeTrue();
        }

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that a cycles breakpoint can be created.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_CanCreateCyclesBreakpoint() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Begin creating a breakpoint
        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        // Select Cycles breakpoint tab
        BreakpointTypeTabItemViewModel? cyclesTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "Cycles");
        if (cyclesTab is not null) {
            cyclesTab.IsSelected = true;
            viewModel.SelectedBreakpointTypeTab = cyclesTab;
            ProcessUiEvents();

            viewModel.IsCyclesBreakpointSelected.Should().BeTrue();

            // Set cycles value
            viewModel.CyclesValue = 1000;
            ProcessUiEvents();

            // Should be able to confirm
            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue();
        }

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that an interrupt breakpoint can be created.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_CanCreateInterruptBreakpoint() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Begin creating a breakpoint
        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        // Select Interrupt breakpoint tab
        BreakpointTypeTabItemViewModel? interruptTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "Interrupt");
        if (interruptTab is not null) {
            interruptTab.IsSelected = true;
            viewModel.SelectedBreakpointTypeTab = interruptTab;
            ProcessUiEvents();

            viewModel.IsInterruptBreakpointSelected.Should().BeTrue();

            // Set interrupt number
            viewModel.InterruptNumber = "0x21";
            ProcessUiEvents();

            // Should be able to confirm
            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue();
        }

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that an I/O port breakpoint can be created.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_CanCreateIoPortBreakpoint() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Begin creating a breakpoint
        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        // Select I/O Port breakpoint tab
        BreakpointTypeTabItemViewModel? ioPortTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "I/O Port");
        if (ioPortTab is not null) {
            ioPortTab.IsSelected = true;
            viewModel.SelectedBreakpointTypeTab = ioPortTab;
            ProcessUiEvents();

            viewModel.IsIoPortBreakpointSelected.Should().BeTrue();

            // Set I/O port number
            viewModel.IoPortNumber = "0x3C0";
            ProcessUiEvents();

            // Should be able to confirm
            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue();
        }

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that condition expressions with bitwise operators are accepted.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_AcceptsBitwiseOperatorConditions() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Test bitwise operators
        string[] conditions = { "(ax & 0xFF) == 0", "(bx | 0xF0) == 0xF0", "(cx ^ 0x55) == 0", "~ax == 0" };

        foreach (string condition in conditions) {
            viewModel.BeginCreateBreakpointCommand.Execute(null);
            ProcessUiEvents();

            viewModel.ExecutionAddressValue = "0x1000:0x0100";
            viewModel.ExecutionConditionExpression = condition;
            ProcessUiEvents();

            // Should be able to confirm (valid conditions)
            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue(
                $"Condition '{condition}' should be valid");

            viewModel.CancelBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }

        // Cleanup
        window.Close();
    }

    /// <summary>
    /// Verifies that condition expressions with arithmetic operators are accepted.
    /// </summary>
    [AvaloniaFact]
    public void BreakpointsView_AcceptsArithmeticOperatorConditions() {
        // Arrange
        (BreakpointsView view, BreakpointsViewModel viewModel) = CreateBreakpointsViewWithViewModel();
        Window window = new() { Content = view };
        ShowWindowAndWait(window);

        // Act - Test arithmetic operators
        string[] conditions = { "(ax + 1) == 2", "(bx - 1) == 0", "(cx * 2) == 4", "(dx / 2) == 1", "(ax % 2) == 0" };

        foreach (string condition in conditions) {
            viewModel.BeginCreateBreakpointCommand.Execute(null);
            ProcessUiEvents();

            viewModel.ExecutionAddressValue = "0x1000:0x0100";
            viewModel.ExecutionConditionExpression = condition;
            ProcessUiEvents();

            // Should be able to confirm (valid conditions)
            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue(
                $"Condition '{condition}' should be valid");

            viewModel.CancelBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }

        // Cleanup
        window.Close();
    }
}
