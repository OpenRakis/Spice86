namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;

using Spice86.ViewModels;

public partial class CfgCpuView : UserControl {
    public CfgCpuView() {
        InitializeComponent();
        ZoomBorder.PointerPressed += ZoomBorder_PointerPressed;
        ZoomBorder.PointerReleased += ZoomBorder_PointerReleased;
    }

    private void ZoomBorder_PointerReleased(object? sender, PointerReleasedEventArgs e) {
        ZoomBorder.Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void ZoomBorder_PointerPressed(object? sender, PointerPressedEventArgs e) {
        Avalonia.Input.MouseButton mouseButton = e.GetCurrentPoint(ZoomBorder).Properties.PointerUpdateKind.GetMouseButton();
        if (mouseButton == MouseButton.Middle) {
            ZoomBorder.Cursor = new Cursor(StandardCursorType.Hand);
        }
    }

    private void OnAutoCompleteKeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Enter && DataContext is CfgCpuViewModel viewModel) {
            viewModel.NavigateToSelectedNodeCommand.Execute(null);
            e.Handled = true;
        }
    }
}