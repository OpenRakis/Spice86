namespace Spice86.ViewModels.Services;

public interface IDebuggerTabRegistry {
    void Add(string tabId, object viewModel);
    void AddSubTab(string groupId, DebuggerSubTabViewModel subTab);
    T Get<T>(string tabId);
    IReadOnlyList<DebuggerSubTabViewModel> GetSubTabs(string groupId);
}
