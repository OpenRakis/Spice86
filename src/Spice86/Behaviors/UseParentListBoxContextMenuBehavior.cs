using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spice86.Behaviors;

public class UseParentListBoxContextMenuBehavior {
    public static readonly AttachedProperty<bool> UseParentContextMenuProperty = AvaloniaProperty.RegisterAttached<Control, bool>("UseParentContextMenu", typeof(UseParentListBoxContextMenuBehavior));

    static UseParentListBoxContextMenuBehavior() {
        UseParentContextMenuProperty.Changed.AddClassHandler<Control>((control, e) => {
            if (e.NewValue is true) {
                control.AddHandler(Control.ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
            } else {
                control.RemoveHandler(Control.ContextRequestedEvent, OnContextRequested);
            }
        });
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e) {
        if (sender is Control control) {
            e.Handled = true;

            // Find parent ListBox and the associated ListBoxItem
            Control? parent = control;
            ListBox? listBox = null;
            ListBoxItem? listBoxItem = null;

            while (parent != null) {
                if (parent is ListBoxItem lbi) {
                    listBoxItem = lbi;
                }

                if (parent is ListBox lb) {
                    listBox = lb;

                    break;
                }

                parent = parent.Parent as Control;
            }

            if (listBox?.ContextMenu != null && listBoxItem != null) {
                // Important: Select the item we right-clicked on
                listBox.SelectedItem = listBoxItem.DataContext;

                // Open the context menu
                listBox.ContextMenu.Open(listBox);
            }
        }
    }

    public static void SetUseParentContextMenu(Control element, bool value) {
        element.SetValue(UseParentContextMenuProperty, value);
    }

    public static bool GetUseParentContextMenu(Control element) {
        return element.GetValue(UseParentContextMenuProperty);
    }
}