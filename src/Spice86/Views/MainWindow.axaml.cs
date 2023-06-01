namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System.ComponentModel;

internal partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
        Closing += MainWindow_Closing;
    }

    private Image? _videoBufferImage;

    public void SetPrimaryDisplayControl(Image image) {
        if(_videoBufferImage != image) {
            _videoBufferImage = image;
            FocusOnVideoBuffer();
        }
    }

    private void FocusOnVideoBuffer() {
        if (_videoBufferImage is not null) {
            _videoBufferImage.IsEnabled = false;
            _videoBufferImage.Focus();
            _videoBufferImage.IsEnabled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        FocusOnVideoBuffer();
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        FocusOnVideoBuffer();
    }

    public static event EventHandler<CancelEventArgs>? AppClosing;

    private void MainWindow_Closing(object? sender, CancelEventArgs e) {
        AppClosing?.Invoke(sender, e);
    }
}