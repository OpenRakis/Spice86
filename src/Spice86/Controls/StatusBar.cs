using Avalonia.Controls;

namespace Spice86.Controls;

using Avalonia.Layout;

/// <summary>
/// StatusBar is a visual indicator of the operational status of an application and/or
/// its components running in a window.  StatusBar control consists of a series of zones
/// on a band that can display text, graphics, or other rich content. The control can
/// group items within these zones to emphasize relational similarities or functional
/// connections. The StatusBar can accommodate multiple sets of UI or functionality that
/// can be chosen even within the same application.
/// </summary>
internal sealed class StatusBar : StackPanel {
    protected override Type StyleKeyOverride { get; } = typeof(StackPanel);
    
    static StatusBar() {
        OrientationProperty.OverrideDefaultValue<StatusBar>(Orientation.Horizontal);
        HorizontalAlignmentProperty.OverrideDefaultValue<StatusBar>(HorizontalAlignment.Stretch);
    }
}