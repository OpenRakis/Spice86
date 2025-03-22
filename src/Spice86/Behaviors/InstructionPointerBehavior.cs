using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Spice86.ViewModels;

namespace Spice86.Behaviors;

/// <summary>
/// Attached behavior for handling instruction pointer events in the disassembly view.
/// This behavior encapsulates the UI-specific pointer event logic to improve separation of concerns.
/// </summary>
public class InstructionPointerBehavior
{
    // Attached property for enabling the behavior
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<InstructionPointerBehavior, Control, bool>("IsEnabled");

    // Helper methods for getting/setting the attached properties
    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    // Static constructor to register property changed handlers
    static InstructionPointerBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    // Handler for when IsEnabled property changes
    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        bool isEnabled = (bool)e.NewValue!;
        if (isEnabled)
        {
            // Subscribe to pointer events
            control.PointerPressed += OnPointerPressed;
        }
        else
        {
            // Unsubscribe from pointer events
            control.PointerPressed -= OnPointerPressed;
        }
    }

    // Handler for pointer pressed events
    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Console.WriteLine($"Pointer pressed on control: {sender?.GetType()}");
        if (sender is not Control control)
        {
            return;
        }

        // Get the data context and view model
        if (control.DataContext is not DebuggerLineViewModel line)
        {
            return;
        }

        // Find the parent control with the view model
        Control? parent = control;
        IModernDisassemblyViewModel? viewModel = null;

        while (parent != null)
        {
            if (parent.DataContext is IModernDisassemblyViewModel vm)
            {
                viewModel = vm;
                break;
            }
            parent = parent.Parent as Control;
        }

        if (viewModel == null)
        {
            return;
        }

        // Select the line
        viewModel.SelectedDebuggerLine = line;
    }
}
