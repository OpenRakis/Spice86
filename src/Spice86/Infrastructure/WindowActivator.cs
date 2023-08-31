namespace Spice86.Infrastructure;

using Avalonia.Controls;

using Spice86.ViewModels;

using System;
using System.Collections.Generic;

/// <inheritdoc cref="IWindowActivator" />
internal class WindowActivator : IWindowActivator {
    private readonly Dictionary<Type, Window> _createdWindows = new();

    /// <inheritdoc />
    public void Activate<T>(params object[]? parameters) where T : ViewModelBase {
        if(_createdWindows.TryGetValue(typeof(T), out Window? window)) {
            window.Activate();
        }
        object? viewModel = Activator.CreateInstance(typeof(T), parameters);
        var name = typeof(T).FullName!.Replace("ViewModels", "Views").Replace("ViewModel", "Window");
        var typeOfWindow = Type.GetType(name);

        if (typeOfWindow != null) {
            Window? windowCreated = (Window?)Activator.CreateInstance(typeOfWindow);
            if(windowCreated is not null) {
                windowCreated.Show();
                windowCreated.DataContext = viewModel;
                windowCreated.Closed += (_, _) => _createdWindows.Remove(typeof(T));
                _createdWindows.Add(typeof(T), windowCreated);
            }
        }
    }

    public void Clear() {
        for(int i = 0; i < _createdWindows.Count; i++) {
            _createdWindows[_createdWindows.Keys.ElementAt(i)].Close();
        }
        _createdWindows.Clear();
    }
}
