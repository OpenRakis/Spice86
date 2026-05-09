namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.Input;

using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels.ValueViewModels.Debugging;

/// <summary>
///     Interface containing all the commands related to disassembly functionality.
/// </summary>
public interface IDisassemblyCommands {
    /// <summary>
    ///     Command to create a new disassembly view.
    /// </summary>
    IRelayCommand NewDisassemblyViewCommand { get; }

    /// <summary>
    ///     Command to step into the current instruction.
    /// </summary>
    IRelayCommand StepIntoCommand { get; }

    /// <summary>
    ///     Command to step over the current instruction.
    /// </summary>
    IRelayCommand StepOverCommand { get; }

    /// <summary>
    ///     Command to go to a specific function.
    /// </summary>
    IRelayCommand<FunctionInfo> GoToFunctionCommand { get; }

    /// <summary>
    ///     Command to go to the current CS:IP location
    /// </summary>
    IRelayCommand GoToCsIpCommand { get; }

    /// <summary>
    ///     Command to go to a specific address.
    /// </summary>
    IRelayCommand<SegmentedAddress?> GoToAddressCommand { get; }

    /// <summary>
    ///     Command to close the tab.
    /// </summary>
    IRelayCommand CloseTabCommand { get; }

    /// <summary>
    ///     Command to create an execution breakpoint at the current instruction.
    /// </summary>
    IRelayCommand<DebuggerLineViewModel> CreateExecutionBreakpointHereCommand { get; }

    /// <summary>
    ///     Command to create an execution breakpoint with a dialog for specifying conditions.
    /// </summary>
    IRelayCommand<DebuggerLineViewModel> CreateExecutionBreakpointWithDialogCommand { get; }

    /// <summary>
    ///     Command to edit an existing execution breakpoint.
    /// </summary>
    IRelayCommand<DebuggerLineViewModel> EditExecutionBreakpointCommand { get; }

    /// <summary>
    ///     Command to remove an execution breakpoint at the current instruction.
    /// </summary>
    IRelayCommand<DebuggerLineViewModel> RemoveExecutionBreakpointHereCommand { get; }

    /// <summary>
    ///     Command to disable a breakpoint.
    /// </summary>
    IRelayCommand<BreakpointViewModel> DisableBreakpointCommand { get; }

    /// <summary>
    ///     Command to enable a breakpoint.
    /// </summary>
    IRelayCommand<BreakpointViewModel> EnableBreakpointCommand { get; }

    /// <summary>
    ///     Command to toggle a breakpoint at the current instruction.
    /// </summary>
    IRelayCommand<DebuggerLineViewModel> ToggleBreakpointCommand { get; }

    /// <summary>
    ///     Command to move the CS:IP to the current instruction.
    /// </summary>
    IRelayCommand MoveCsIpHereCommand { get; }

    /// <summary>
    ///     Command to confirm breakpoint creation from the dialog.
    /// </summary>
    IRelayCommand ConfirmBreakpointCreationCommand { get; }

    /// <summary>
    ///     Command to cancel breakpoint creation from the dialog.
    /// </summary>
    IRelayCommand CancelBreakpointCreationCommand { get; }
}