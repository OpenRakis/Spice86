namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;

using Spice86.ViewModels;

/// <summary>
/// Window displaying a visual representation of a classic PC gameport joystick
/// for manual testing and feedback. Supports mouse drag in the stick area and
/// keyboard shortcuts (arrow keys for stick, Z/X for fire buttons).
/// </summary>
public partial class JoystickPanelView : Window {
    private bool _isDragging;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoystickPanelView"/> class.
    /// </summary>
    public JoystickPanelView() {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLoaded(global::Avalonia.Interactivity.RoutedEventArgs e) {
        base.OnLoaded(e);
        Panel? stickArea = this.FindControl<Panel>("StickArea");
        if (stickArea is not null) {
            stickArea.PointerPressed += OnStickAreaPointerPressed;
            stickArea.PointerMoved += OnStickAreaPointerMoved;
            stickArea.PointerReleased += OnStickAreaPointerReleased;
        }
    }

    private void OnStickAreaPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (sender is not Panel panel) {
            return;
        }
        _isDragging = true;
        e.Pointer.Capture(panel);
        UpdateStickFromPointer(panel, e);
    }

    private void OnStickAreaPointerMoved(object? sender, PointerEventArgs e) {
        if (!_isDragging || sender is not Panel panel) {
            return;
        }
        UpdateStickFromPointer(panel, e);
    }

    private void OnStickAreaPointerReleased(object? sender, PointerReleasedEventArgs e) {
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    private void UpdateStickFromPointer(Panel panel, PointerEventArgs e) {
        if (DataContext is not JoystickPanelViewModel vm) {
            return;
        }
        global::Avalonia.Point pos = e.GetPosition(panel);
        vm.SetStickPositionFromMouse(pos.X, pos.Y);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e) {
        if (DataContext is not JoystickPanelViewModel vm) {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key) {
            case Key.Left:
                vm.StickLeftCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                vm.StickRightCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                vm.StickUpCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                vm.StickDownCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Z:
                vm.ButtonA1Pressed = !vm.ButtonA1Pressed;
                e.Handled = true;
                break;
            case Key.X:
                vm.ButtonA2Pressed = !vm.ButtonA2Pressed;
                e.Handled = true;
                break;
            case Key.C:
                vm.CenterStickCommand.Execute(null);
                e.Handled = true;
                break;
            default:
                base.OnKeyDown(e);
                break;
        }
    }
}
