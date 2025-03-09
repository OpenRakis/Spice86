using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace Spice86.Behaviors;

public class UpdateBindingOnEnterBehavior : Behavior<TextBox> {
    protected override void OnAttached() {
        base.OnAttached();
        if (AssociatedObject != null) {
            AssociatedObject.KeyUp += OnKeyUp;
        }
    }

    protected override void OnDetaching() {
        base.OnDetaching();
        if (AssociatedObject != null) {
            AssociatedObject.KeyUp -= OnKeyUp;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) {
        if (e.Key == Key.Enter && AssociatedObject != null) {
            BindingExpressionBase? binding = BindingOperations.
                GetBindingExpressionBase(AssociatedObject, TextBox.TextProperty);
            if (binding != null) {
                binding?.UpdateSource();
            }
        }
    }
}