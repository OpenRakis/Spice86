namespace Spice86.ViewModels;

using System.Windows.Input;

public interface IMemorySearchViewModel {
    bool IsSearchingMemory { get; set; }
    bool SearchDataTypeIsBinary { get; }
    bool SearchDataTypeIsAscii { get; }
    bool IsBusy { get; }
    string? MemorySearchValue { get; set; }
    string FoundOccurrenceDisplay { get; }
    ICommand SetSearchDataTypeToBinaryAction { get; }
    ICommand SetSearchDataTypeToAsciiAction { get; }
    ICommand FirstOccurrenceAction { get; }
    ICommand NextOccurrenceAction { get; }
    ICommand PreviousOccurrenceAction { get; }
    ICommand SearchCancelAction { get; }
    ICommand StartMemorySearchAction { get; }
    ICommand StopMemorySearchAction { get; }
    bool CanOpenFoundOccurrence { get; }
    bool ShowOpenFoundOccurrenceAction { get; }
    ICommand OpenFoundOccurrenceAction { get; }
}