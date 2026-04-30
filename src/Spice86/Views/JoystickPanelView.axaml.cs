namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Spice86.ViewModels;
using Spice86.ViewModels.Services;

/// <summary>
/// Window displaying a visual representation of a classic PC gameport joystick
/// for manual testing and feedback. Supports mouse drag in the stick area and
/// keyboard shortcuts (arrow keys for stick, Z/X for fire buttons).
/// Manages the DispatcherTimer lifecycle for gameport diagnostics polling.
/// </summary>
public partial class JoystickPanelView : Window {
    private bool _isDragging;
    private readonly Panel? _stickArea;
    private DispatcherTimer? _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoystickPanelView"/> class.
    /// </summary>
    public JoystickPanelView() {
        InitializeComponent();
        _stickArea = this.FindControl<Panel>("StickArea");
        if (_stickArea is not null) {
            _stickArea.PointerPressed += OnStickAreaPointerPressed;
            _stickArea.PointerMoved += OnStickAreaPointerMoved;
            _stickArea.PointerReleased += OnStickAreaPointerReleased;
        }
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is JoystickPanelViewModel vm) {
            _timer = DispatcherTimerStarter.StartNewDispatcherTimer(
                TimeSpan.FromMilliseconds(50),
                DispatcherPriority.Background,
                vm.UpdateValues);
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        _timer?.Stop();
        _timer = null;

        if (_stickArea is not null) {
            _stickArea.PointerPressed -= OnStickAreaPointerPressed;
            _stickArea.PointerMoved -= OnStickAreaPointerMoved;
            _stickArea.PointerReleased -= OnStickAreaPointerReleased;
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
        Point pos = e.GetPosition(panel);
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
                vm.ButtonA1Pressed = true;
                e.Handled = true;
                break;
            case Key.X:
                vm.ButtonA2Pressed = true;
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

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e) {
        if (DataContext is not JoystickPanelViewModel vm) {
            base.OnKeyUp(e);
            return;
        }

        switch (e.Key) {
            case Key.Z:
                vm.ButtonA1Pressed = false;
                e.Handled = true;
                break;
            case Key.X:
                vm.ButtonA2Pressed = false;
                e.Handled = true;
                break;
            default:
                base.OnKeyUp(e);
                break;
        }
    }
}
