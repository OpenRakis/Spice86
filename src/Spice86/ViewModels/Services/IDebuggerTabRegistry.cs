namespace Spice86.ViewModels.Services;

public interface IDebuggerTabRegistry {
    void Add(DebuggerTabId tabId, object viewModel);
    void AddSubTab(DebuggerTabId groupId, DebuggerSubTabViewModel subTab);
    T Get<T>(DebuggerTabId tabId);
    IReadOnlyList<DebuggerSubTabViewModel> GetSubTabs(DebuggerTabId groupId);
}
