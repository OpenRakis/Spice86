namespace Spice86.Controls;

using Avalonia.Controls.Primitives;

/// <summary>
/// A simple control composed of a header and content.
/// </summary>
internal sealed class GroupBox : HeaderedContentControl {
    static GroupBox() {
        IsTabStopProperty.OverrideDefaultValue(typeof(GroupBox), false);
        FocusableProperty.OverrideDefaultValue(typeof(GroupBox), false);
    }
}