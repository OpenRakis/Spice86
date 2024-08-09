namespace Spice86;

using Avalonia.Controls;
using Avalonia.Controls.Templates;

using Spice86.ViewModels;

using System;

internal sealed class ViewLocator : IDataTemplate {
    public bool SupportsRecycling => false;

    public Control Build(object? data) {
        string? name = data?.GetType().FullName?.Replace("ViewModel", "View");
        if (string.IsNullOrWhiteSpace(name)) {
            return new TextBlock { Text = "Not Found: " + name };
        }
        Type? type = Type.GetType(name);

        if (type != null) {
            Control control = (Control)Activator.CreateInstance(type)!;
            return control;
        } else {
            return new TextBlock { Text = "Not Found: " + name };
        }
    }

    public bool Match(object? data) {
        return data is ViewModelBase;
    }
}