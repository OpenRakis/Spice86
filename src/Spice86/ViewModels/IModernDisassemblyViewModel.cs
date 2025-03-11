namespace Spice86.ViewModels;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.Input;
using Spice86.Core.Emulator.Function;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;
using System.ComponentModel;

/// <summary>
/// Interface for the ModernDisassemblyViewModel to support proper MVVM separation.
/// This interface defines the contract between the View and ViewModel, allowing for
/// better testability and decoupling.
/// </summary>
public interface IModernDisassemblyViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// The physical address of the current instruction. This is updated when the emulator pauses.
    /// </summary>
    uint CurrentInstructionAddress { get; }

    /// <summary>
    /// Collection of debugger lines indexed by their physical address.
    /// </summary>
    AvaloniaDictionary<uint, DebuggerLineViewModel> DebuggerLines { get; }

    /// <summary>
    /// The currently selected debugger line in the UI.
    /// </summary>
    DebuggerLineViewModel? SelectedDebuggerLine { get; set; }

    /// <summary>
    /// Indicates whether the disassembly is currently loading.
    /// </summary>
    bool IsLoading { get; }

    /// <summary>
    /// Indicates whether the emulator is currently paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Indicates whether function information is available.
    /// </summary>
    bool IsFunctionInformationProvided { get; }

    /// <summary>
    /// Collection of available functions.
    /// </summary>
    AvaloniaList<FunctionInfo> Functions { get; }

    /// <summary>
    /// The currently selected function.
    /// </summary>
    FunctionInfo? SelectedFunction { get; set; }

    /// <summary>
    /// The header text for the view.
    /// </summary>
    string Header { get; }

    /// <summary>
    /// Command to update the disassembly.
    /// </summary>
    IAsyncRelayCommand UpdateDisassemblyCommand { get; }

    /// <summary>
    /// Command to navigate to the current CS:IP location.
    /// </summary>
    IAsyncRelayCommand GoToCsIpCommand { get; }

    /// <summary>
    /// Command to create a new disassembly view.
    /// </summary>
    IAsyncRelayCommand NewDisassemblyViewCommand { get; }

    /// <summary>
    /// Command to copy the selected line.
    /// </summary>
    IRelayCommand CopyLineCommand { get; }

    /// <summary>
    /// Command to step into the current instruction.
    /// </summary>
    IAsyncRelayCommand StepIntoCommand { get; }

    /// <summary>
    /// Command to step over the current instruction.
    /// </summary>
    IAsyncRelayCommand StepOverCommand { get; }

    /// <summary>
    /// Command to go to a specific function.
    /// </summary>
    IAsyncRelayCommand GoToFunctionCommand { get; }

    /// <summary>
    /// Command to close the current tab.
    /// </summary>
    IRelayCommand CloseTabCommand { get; }
    
    /// <summary>
    /// Command to scroll to a specific address in the disassembly view.
    /// </summary>
    IRelayCommand<uint> ScrollToAddressCommand { get; }
    
    /// <summary>
    /// Command to create an execution breakpoint at the current instruction.
    /// </summary>
    IAsyncRelayCommand CreateExecutionBreakpointHereCommand { get; }
    
    /// <summary>
    /// Command to remove an execution breakpoint at the current instruction.
    /// </summary>
    IRelayCommand RemoveExecutionBreakpointHereCommand { get; }
    
    /// <summary>
    /// Command to disable a breakpoint at the current instruction.
    /// </summary>
    IRelayCommand DisableBreakpointCommand { get; }
    
    /// <summary>
    /// Command to enable a breakpoint at the current instruction.
    /// </summary>
    IRelayCommand EnableBreakpointCommand { get; }
    
    /// <summary>
    /// Command to move CS:IP to the current instruction.
    /// </summary>
    IRelayCommand MoveCsIpHereCommand { get; }
}
