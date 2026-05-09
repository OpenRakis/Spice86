namespace Spice86.ViewModels;

using Spice86.ViewModels.Services;

public sealed class DebuggerSubTabViewModel {
    public DebuggerTabId Id { get; }
    public IDebuggerTabContentViewModel ViewModel { get; }
    public string Header => ViewModel.Header;

    public DebuggerSubTabViewModel(DebuggerTabId id, IDebuggerTabContentViewModel viewModel) {
        Id = id;
        ViewModel = viewModel;
    }
}