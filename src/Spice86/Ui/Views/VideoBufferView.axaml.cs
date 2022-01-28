namespace Spice86.UI.Views;

using System;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Spice86.UI.ViewModels;

public partial class VideoBufferView : UserControl {
    public VideoBufferView() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
        Initialized += VideoBufferView_Initialized;
    }

    private Image? _image;

    private void VideoBufferView_Initialized(object? sender, EventArgs e) {
        if(this.DataContext is VideoBufferViewModel vm) {
            _image = this.FindControl<Image>(nameof(Image));
            VideoBufferViewModel.IsDirty += VideoBufferViewModel_IsDirty;
            if (vm.IsPrimaryDisplay) {
                _image.PointerMoved += (s, e) => vm.MainWindowViewModel?.OnMouseMoved(e, _image);
                _image.PointerPressed += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, true);
                _image.PointerReleased += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, false);
                _image.KeyDown += (s, e) => vm.MainWindowViewModel?.OnKeyPressed(e);
                _image.KeyUp += (s, e) => vm.MainWindowViewModel?.OnKeyReleased(e);
            }
        }
    }

    private async void VideoBufferViewModel_IsDirty(object? sender, EventArgs e) {
        await Dispatcher.UIThread.InvokeAsync(() => {
            if (sender == this.DataContext) {
                _image?.InvalidateVisual();
            }
        });
    }
}
