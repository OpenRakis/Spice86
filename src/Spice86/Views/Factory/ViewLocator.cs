namespace Spice86.Views.Factory;

using Avalonia.Controls;
using Avalonia.Controls.Templates;

using System;
using System.ComponentModel;

internal sealed class ViewLocator : IDataTemplate {
    public bool SupportsRecycling => false;

    /// <summary>
    /// Takes a ViewModel and returns a new instance of the corresponding View.
    /// This is supported by the convention that the View is named the same as the ViewModel with "View" instead of "ViewModel".
    /// </summary>
    /// <param name="data">The ViewModel we search the view for.</param>
    /// <returns>The corresponding View, or a TextBlock with the "Not Found" message if a match wasn't found.</returns>
    public Control Build(object? data) {
        string? name = data?.GetType().FullName?.Replace("ViewModel", "View");
        if (name == "Spice86.Views.StackMemoryView") {
            name = "Spice86.Views.MemoryView";
        }
        if (name == "Spice86.Views.DataSegmentMemoryView") {
            name = "Spice86.Views.MemoryView";
        }
        if (string.IsNullOrWhiteSpace(name)) {
            return new TextBlock { Text = "Not Found: " + name };
        }
        var type = Type.GetType(name);

        if (type != null && type.IsAssignableTo(typeof(Control))) {
            var control = (Control)Activator.CreateInstance(type)!;
            return control;
        } else {
            return new TextBlock { Text = "Not Found: " + name };
        }
    }

    public bool Match(object? data) {
        return data is INotifyPropertyChanged;
    }
}