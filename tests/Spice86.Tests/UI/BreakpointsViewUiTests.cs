namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using FluentAssertions;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.ViewModels;

public class BreakpointsViewUiTests : BreakpointUiTestBase {
    [AvaloniaFact]
    public void BreakpointsView_CanCreateExecutionBreakpoint() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        BreakpointTypeTabItemViewModel? executionTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "Execution");
        executionTab.Should().NotBeNull();
        viewModel.SelectedBreakpointTypeTab = executionTab;
        ProcessUiEvents();

        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        viewModel.CreatingBreakpoint.Should().BeTrue();
        viewModel.IsExecutionBreakpointSelected.Should().BeTrue();
    }

    [AvaloniaFact]
    public void BreakpointsView_CanCreateExecutionBreakpointWithCondition() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        viewModel.ExecutionAddressValue = "0x1000:0x0100";
        viewModel.ExecutionConditionExpression = "ax == 0x100";
        ProcessUiEvents();

        if (viewModel.ConfirmBreakpointCreationCommand.CanExecute(null)) {
            viewModel.ConfirmBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }

        viewModel.CreatingBreakpoint.Should().BeFalse();
    }

    [AvaloniaFact]
    public void BreakpointsView_InvalidConditionExpression_ShowsError() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        viewModel.ExecutionAddressValue = "0x1000:0x0100";
        viewModel.ExecutionConditionExpression = "invalid expression !!!";
        ProcessUiEvents();

        bool canConfirm = viewModel.ConfirmBreakpointCreationCommand.CanExecute(null);
        if (canConfirm) {
            viewModel.ConfirmBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }

    }

    [AvaloniaFact]
    public void BreakpointsView_CancelCreation_ClearsCreatingState() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        viewModel.CreatingBreakpoint.Should().BeTrue();

        viewModel.CancelBreakpointCreationCommand.Execute(null);
        ProcessUiEvents();

        viewModel.CreatingBreakpoint.Should().BeFalse();
    }

    [AvaloniaFact]
    public void BreakpointsView_ShowsBreakpointTypeTabs() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        viewModel.BreakpointTabs.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void BreakpointsView_AcceptsComparisonOperatorConditions() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        string[] conditions = { "ax == 0", "bx != 5", "cx > 100", "dx < 50", "eax >= 0", "ebx <= 0xFFFF" };

        foreach (string condition in conditions) {
            viewModel.BeginCreateBreakpointCommand.Execute(null);
            ProcessUiEvents();

            viewModel.ExecutionAddressValue = "0x1000:0x0100";
            viewModel.ExecutionConditionExpression = condition;
            ProcessUiEvents();

            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue(
                $"Condition '{condition}' should be valid");

            viewModel.CancelBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }
    }

    [AvaloniaFact]
    public void BreakpointsView_AcceptsLogicalOperatorConditions() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        string[] conditions = { "ax == 0 && bx == 0", "cx == 0 || dx == 0", "!(ax == 0)" };

        foreach (string condition in conditions) {
            viewModel.BeginCreateBreakpointCommand.Execute(null);
            ProcessUiEvents();

            viewModel.ExecutionAddressValue = "0x1000:0x0100";
            viewModel.ExecutionConditionExpression = condition;
            ProcessUiEvents();

            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue(
                $"Condition '{condition}' should be valid");

            viewModel.CancelBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }
    }

    [AvaloniaFact]
    public void BreakpointsView_CanCreateMemoryBreakpoint() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        BreakpointTypeTabItemViewModel? memoryTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "Memory");
        if (memoryTab is not null) {
            memoryTab.IsSelected = true;
            viewModel.SelectedBreakpointTypeTab = memoryTab;
            ProcessUiEvents();

            viewModel.IsMemoryBreakpointSelected.Should().BeTrue();
        }
    }

    [AvaloniaFact]
    public void BreakpointsView_CanCreateCyclesBreakpoint() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        BreakpointTypeTabItemViewModel? cyclesTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "Cycles");
        if (cyclesTab is not null) {
            cyclesTab.IsSelected = true;
            viewModel.SelectedBreakpointTypeTab = cyclesTab;
            ProcessUiEvents();

            viewModel.IsCyclesBreakpointSelected.Should().BeTrue();

            viewModel.CyclesValue = 1000;
            ProcessUiEvents();

            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue();
        }
    }

    [AvaloniaFact]
    public void BreakpointsView_CanCreateInterruptBreakpoint() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        BreakpointTypeTabItemViewModel? interruptTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "Interrupt");
        if (interruptTab is not null) {
            interruptTab.IsSelected = true;
            viewModel.SelectedBreakpointTypeTab = interruptTab;
            ProcessUiEvents();

            viewModel.IsInterruptBreakpointSelected.Should().BeTrue();

            viewModel.InterruptNumber = "0x21";
            ProcessUiEvents();

            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue();
        }
    }

    [AvaloniaFact]
    public void BreakpointsView_CanCreateIoPortBreakpoint() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        viewModel.BeginCreateBreakpointCommand.Execute(null);
        ProcessUiEvents();

        BreakpointTypeTabItemViewModel? ioPortTab = viewModel.BreakpointTabs.FirstOrDefault(t => t.Header == "I/O Port");
        if (ioPortTab is not null) {
            ioPortTab.IsSelected = true;
            viewModel.SelectedBreakpointTypeTab = ioPortTab;
            ProcessUiEvents();

            viewModel.IsIoPortBreakpointSelected.Should().BeTrue();

            viewModel.IoPortNumber = "0x3C0";
            ProcessUiEvents();

            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue();
        }
    }

    [AvaloniaFact]
    public void BreakpointsView_AcceptsBitwiseOperatorConditions() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        string[] conditions = { "(ax & 0xFF) == 0", "(bx | 0xF0) == 0xF0", "(cx ^ 0x55) == 0", "~ax == 0" };

        foreach (string condition in conditions) {
            viewModel.BeginCreateBreakpointCommand.Execute(null);
            ProcessUiEvents();

            viewModel.ExecutionAddressValue = "0x1000:0x0100";
            viewModel.ExecutionConditionExpression = condition;
            ProcessUiEvents();

            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue(
                $"Condition '{condition}' should be valid");

            viewModel.CancelBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }
    }

    [AvaloniaFact]
    public void BreakpointsView_AcceptsArithmeticOperatorConditions() {
        BreakpointsViewModel viewModel = CreateBreakpointsContext().BreakpointsViewModel;
        ProcessUiEvents();

        string[] conditions = { "(ax + 1) == 2", "(bx - 1) == 0", "(cx * 2) == 4", "(dx / 2) == 1", "(ax % 2) == 0" };

        foreach (string condition in conditions) {
            viewModel.BeginCreateBreakpointCommand.Execute(null);
            ProcessUiEvents();

            viewModel.ExecutionAddressValue = "0x1000:0x0100";
            viewModel.ExecutionConditionExpression = condition;
            ProcessUiEvents();

            viewModel.ConfirmBreakpointCreationCommand.CanExecute(null).Should().BeTrue(
                $"Condition '{condition}' should be valid");

            viewModel.CancelBreakpointCreationCommand.Execute(null);
            ProcessUiEvents();
        }
    }
}
