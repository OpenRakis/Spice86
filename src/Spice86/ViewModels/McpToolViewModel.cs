namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using System;

public partial class McpToolViewModel : ViewModelBase {
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _argumentsTemplateJson;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _canToggle;

    public McpToolViewModel(string name, string description, string argumentsTemplateJson,
        bool isEnabled, bool canToggle) {
        _name = name;
        _description = description;
        _argumentsTemplateJson = argumentsTemplateJson;
        _isEnabled = isEnabled;
        _canToggle = canToggle;
    }

    public bool MatchesFilter(string filter) {
        if (string.IsNullOrWhiteSpace(filter)) {
            return true;
        }

        return Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

}
