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

    /// <inheritdoc />
    public void Dispose() {
        foreach (object value in _items.Values) {
            if (value is IDisposable disposable) {
                disposable.Dispose();
            } else if (value is IEnumerable<object> collection) {
                foreach (object item in collection) {
                    if (item is IDisposable disposableItem) {
                        disposableItem.Dispose();
                    }
                }
            }
        }
        foreach (List<DebuggerSubTabViewModel> subTabs in _subTabsByGroup.Values) {
            foreach (DebuggerSubTabViewModel subTab in subTabs) {
                if (subTab.ViewModel is IDisposable disposable) {
                    disposable.Dispose();
                }
            }
        }
    }
}