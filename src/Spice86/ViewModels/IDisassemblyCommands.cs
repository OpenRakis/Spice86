namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.Input;

using Spice86.Shared.Emulator.Memory;

/// <summary>
///     Interface containing all the commands related to disassembly functionality.
/// </summary>
public interface IDisassemblyCommands {
    /// <summary>
    ///     Command to update the disassembly.
    /// </summary>
    IAsyncRelayCommand UpdateDisassemblyCommand { get; }

    /// <summary>
    ///     Command to create a new disassembly view.
    /// </summary>
    IAsyncRelayCommand NewDisassemblyViewCommand { get; }

    /// <summary>
    ///     Command to copy the selected line.
    /// </summary>
    IRelayCommand CopyLineCommand { get; }

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
    IRelayCommand GoToFunctionCommand { get; }

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
}