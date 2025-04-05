namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;

using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

/// <summary>
///     Interface for the DisassemblyViewModel to support proper MVVM separation.
///     This interface defines the contract between the View and ViewModel, allowing for
///     better testability and decoupling.
/// </summary>
public interface IDisassemblyViewModel : INotifyPropertyChanged, IDisassemblyCommands {
    /// <summary>
    ///     The address of the current instruction. This is updated when the emulator pauses.
    /// </summary>
    SegmentedAddress? CurrentInstructionAddress { get; set; }

    /// <summary>
    ///     Gets a sorted view of the debugger lines for UI display.
    /// </summary>
    ObservableCollection<DebuggerLineViewModel> SortedDebuggerLinesView { get; }

    /// <summary>
    ///     The currently selected debugger line in the UI.
    /// </summary>
    DebuggerLineViewModel? SelectedDebuggerLine { get; set; }

    /// <summary>
    ///     Indicates whether the disassembly is currently loading.
    /// </summary>
    bool IsLoading { get; }

    /// <summary>
    ///     Indicates whether the emulator is currently paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    ///     Indicates whether function information is available.
    /// </summary>
    bool IsFunctionInformationProvided { get; }

    /// <summary>
    ///     Indicates whether the tab can be closed
    /// </summary>
    bool CanCloseTab { get; }

    /// <summary>
    ///     Collection of available functions.
    /// </summary>
    AvaloniaList<FunctionInfo> Functions { get; }

    /// <summary>
    ///     The currently selected function.
    /// </summary>
    FunctionInfo? SelectedFunction { get; set; }

    /// <summary>
    ///     The header text for the view.
    /// </summary>
    string Header { get; }

    /// <summary>
    ///     The view model for CPU registers.
    /// </summary>
    IRegistersViewModel Registers { get; }

    /// <summary>
    ///     Defines a filter for the autocomplete functionality, filtering structures based on the search text and their size.
    /// </summary>
    AutoCompleteFilterPredicate<object?> FunctionFilter { get; }

    /// <summary>
    ///     Create the text that is displayed in the textbox when a function is selected.
    /// </summary>
    AutoCompleteSelector<object>? FunctionItemSelector { get; }

    /// <summary>
    ///    Attempts to get a debugger line by its address
    /// </summary>
    /// <param name="address"></param>
    /// <param name="debuggerLine"></param>
    /// <returns></returns>
    bool TryGetLineByAddress(uint address, [NotNullWhen(true)] out DebuggerLineViewModel? debuggerLine);

    /// <summary>
    ///     Attempts to get a debugger line by its segmented address
    /// </summary>
    /// <param name="address"></param>
    /// <param name="debuggerLine"></param>
    /// <returns></returns>
    bool TryGetLineByAddress(SegmentedAddress address, [NotNullWhen(true)] out DebuggerLineViewModel? debuggerLine);
}