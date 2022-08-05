namespace Spice86.WPF.Views;

using Spice86.UI.ViewModels;

using System.Windows;
using System.Windows.Controls;

/// <summary>
/// Interaction logic for VideoBufferView.xaml
/// </summary>
public partial class VideoBufferView : UserControl {
    public VideoBufferView() {
        InitializeComponent();
        DataContextChanged += VideoBufferView_DataContextChanged;
        MainWindow.AppClosing += MainWindow_AppClosing;
    }

    private void MainWindow_AppClosing(object? sender, System.ComponentModel.CancelEventArgs e) {
        _appClosing = true;
    }
    private Image? _image;
    private bool _appClosing;

    private void VideoBufferView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        if (DataContext is WPFVideoBufferViewModel vm) {
            _image = (Image)this.FindName(nameof(Image));
            if (vm.IsPrimaryDisplay && _image is not null && App.Current.MainWindow.DataContext is WPFMainWindowViewModel mainVm) {
                _image.MouseMove -= (s, e) => mainVm.OnMouseMoved(e, _image);
                _image.MouseDown -= (s, e) => mainVm.OnMouseClick(e, true);
                _image.MouseUp -= (s, e) => mainVm.OnMouseClick(e, false);
                _image.MouseMove += (s, e) => mainVm.OnMouseMoved(e, _image);
                _image.MouseDown += (s, e) => mainVm.OnMouseClick(e, true);
                _image.MouseUp += (s, e) => mainVm.OnMouseClick(e, false);
            }
        }
    }
}
