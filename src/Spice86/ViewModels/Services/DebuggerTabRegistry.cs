namespace Spice86.ViewModels.Services;

internal sealed class DebuggerTabRegistry : IDebuggerTabRegistry {
    private readonly Dictionary<DebuggerTabId, object> _items = new();
    private readonly Dictionary<DebuggerTabId, List<DebuggerSubTabViewModel>> _subTabsByGroup = new();

    public void Add(DebuggerTabId tabId, object viewModel) => _items[tabId] = viewModel;

    public void AddSubTab(DebuggerTabId groupId, DebuggerSubTabViewModel subTab) {
        if (!_subTabsByGroup.TryGetValue(groupId, out List<DebuggerSubTabViewModel>? subTabs)) {
            subTabs = new List<DebuggerSubTabViewModel>();
            _subTabsByGroup[groupId] = subTabs;
        }
        subTabs.Add(subTab);
    }

    public T Get<T>(DebuggerTabId tabId) => (T)_items[tabId];

    public IReadOnlyList<DebuggerSubTabViewModel> GetSubTabs(DebuggerTabId groupId) {
        if (_subTabsByGroup.TryGetValue(groupId, out List<DebuggerSubTabViewModel>? subTabs)) {
            return subTabs;
        }
        return Array.Empty<DebuggerSubTabViewModel>();
    }
}
