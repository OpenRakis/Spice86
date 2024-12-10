namespace Spice86.Controls;

using Avalonia.Controls;
using Avalonia.Layout;

/// <summary>
/// Control that implements an item inside a StatusBar.
/// </summary>
internal sealed class StatusBarItem : ContentControl {
    static StatusBarItem() {
        HorizontalContentAlignmentProperty.OverrideDefaultValue<StatusBarItem>(HorizontalAlignment.Stretch);
        IsTabStopProperty.OverrideDefaultValue<StatusBarItem>(false);
    }
}