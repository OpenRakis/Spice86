namespace Spice86.ViewModels;

using Spice86.ViewModels.Services;

public sealed class DebuggerSubTabViewModel {
    public DebuggerTabId Id { get; }
    public string Header { get; }
    public object ViewModel { get; }

    public DebuggerSubTabViewModel(DebuggerTabId id, string header, object viewModel) {
        Id = id;
        Header = header;
        ViewModel = viewModel;
    }
}
