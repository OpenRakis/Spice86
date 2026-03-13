namespace Spice86.ViewModels.Services;

internal sealed class DebuggerTabRegistry : IDebuggerTabRegistry {
    private readonly Dictionary<string, object> _items = new();
    private readonly Dictionary<string, List<DebuggerSubTabViewModel>> _subTabsByGroup = new();

    public void Add(string tabId, object viewModel) => _items[tabId] = viewModel;

    public void AddSubTab(string groupId, DebuggerSubTabViewModel subTab) {
        if (!_subTabsByGroup.TryGetValue(groupId, out List<DebuggerSubTabViewModel>? subTabs)) {
            subTabs = new List<DebuggerSubTabViewModel>();
            _subTabsByGroup[groupId] = subTabs;
        }
        subTabs.Add(subTab);
    }

    public T Get<T>(string tabId) => (T)_items[tabId];

    public IReadOnlyList<DebuggerSubTabViewModel> GetSubTabs(string groupId) {
        if (_subTabsByGroup.TryGetValue(groupId, out List<DebuggerSubTabViewModel>? subTabs)) {
            return subTabs;
        }
        return Array.Empty<DebuggerSubTabViewModel>();
    }
}
