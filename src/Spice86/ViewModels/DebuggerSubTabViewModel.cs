namespace Spice86.ViewModels;

public sealed class DebuggerSubTabViewModel {
    public string Id { get; }
    public string Header { get; }
    public object ViewModel { get; }

    public DebuggerSubTabViewModel(string id, string header, object viewModel) {
        Id = id;
        Header = header;
        ViewModel = viewModel;
    }
}
