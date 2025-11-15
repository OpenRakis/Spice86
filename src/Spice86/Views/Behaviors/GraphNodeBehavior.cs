namespace Spice86.Views.Behaviors;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Reactive;
using Avalonia.VisualTree;

using AvaloniaGraphControl;

using System.Windows.Input;

public class GraphNodeBehavior {
    // Attached command property
    public static readonly AttachedProperty<ICommand> NodeClickCommandProperty =
        AvaloniaProperty.RegisterAttached<GraphNodeBehavior, Control, ICommand>(
            "NodeClickCommand");

    // Get/Set methods for the attached property
    public static void SetNodeClickCommand(Control element, ICommand value) =>
        element.SetValue(NodeClickCommandProperty, value);

    public static ICommand GetNodeClickCommand(Control element) =>
        element.GetValue(NodeClickCommandProperty);

    // Called when the property is attached to a control
    static GraphNodeBehavior() {
        NodeClickCommandProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<ICommand>>(OnNodeClickCommandChanged));
    }

    // Add/remove event handler when the property changes
    private static void OnNodeClickCommandChanged(AvaloniaPropertyChangedEventArgs<ICommand> args) {
        if (args.Sender is Control control) {
            // When the command is attached, add the event handler
            if (args.NewValue.Value != null) {
                // Attach to Tapped event (works for all controls)
                control.Tapped += Control_Tapped;
            } else {
                // When command is removed, remove the event handler
                control.Tapped -= Control_Tapped;
            }
        }
    }

    // Handle the click event - this will work for both the panel itself and any child nodes
    private static void Control_Tapped(object? sender, TappedEventArgs e) {
        if (sender is Control control) {
            // Get the position within the control
            Point position = e.GetPosition(control);

            // Find the target element at the clicked position
            IInputElement? hitTestResult = control.InputHitTest(position);

            // Find the relevant control that was clicked
            Control? clickedControl = hitTestResult as Control;
            if (clickedControl == null && hitTestResult is Control visual) {
                clickedControl = visual.FindAncestorOfType<Control>();
            }

            // If we found a control with DataContext, use it
            if (clickedControl != null && clickedControl.DataContext != null) {
                // Find the GraphPanel this control belongs to
                GraphPanel? graphPanel = control as GraphPanel ??
                    control.GetSelfAndVisualAncestors()
                           .OfType<GraphPanel>()
                           .FirstOrDefault();

                if (graphPanel != null) {
                    ICommand? command = GetNodeClickCommand(graphPanel);
                    if (command != null && command.CanExecute(clickedControl.DataContext)) {
                        command.Execute(clickedControl.DataContext);
                        e.Handled = true;
                    }
                }
            }
        }
    }
}