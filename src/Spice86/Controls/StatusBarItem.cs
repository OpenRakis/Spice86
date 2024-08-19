namespace Spice86.Controls;

using Avalonia.Controls;

/// <summary>
/// Control that implements an item inside a StatusBar.
/// </summary>
internal sealed class StatusBarItem : ContentControl {
    static StatusBarItem() {
        IsTabStopProperty.OverrideDefaultValue(typeof(StatusBarItem), false);
    }
}