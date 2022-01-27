namespace Spice86.UI.Views;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Spice86.UI.ViewModels;

public partial class VideoBufferView : UserControl {
    public VideoBufferView() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
        Initialized += VideoBufferView_Initialized;
    }

    private void VideoBufferView_Initialized(object? sender, System.EventArgs e) {
        if(this.DataContext is VideoBufferViewModel vm) {
            Image image = this.FindControl<Image>(nameof(Image));
            vm.Invalidator = new Emulator.UI.UIInvalidator(image);
            // TODO: Might not make sense to set this for every video buffer ?
            image.PointerMoved += (s, e) => vm.MainWindowViewModel?.OnMouseMoved(e, image);
            image.PointerPressed += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, true);
            image.PointerReleased += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, false);
            image.KeyDown += (s, e) => vm.MainWindowViewModel?.OnKeyPressed(e);
            image.KeyUp += (s, e) => vm.MainWindowViewModel?.OnKeyReleased(e);
        }
    }
}
