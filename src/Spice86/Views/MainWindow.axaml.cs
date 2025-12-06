namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Spice86.ViewModels;

using System;

internal partial class MainWindow : Window {
    /// <summary>
    /// Initializes a new instance
    /// </summary>
    public MainWindow() {
        InitializeComponent();
        this.Menu.KeyDown += OnMenuKeyDown;
        this.Menu.KeyDown += OnMenuKeyUp;
        this.Menu.GotFocus += OnMenuGotFocus;
        this.Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        Dispatcher.UIThread.Post(() => {
            if (DataContext is MainWindowViewModel viewModel) {
                // Wire up OpenGL frame updates
                viewModel.UpdateOpenGlFrame += OnUpdateOpenGlFrame;
                
                // Track window size changes for shader selection
                this.PropertyChanged += OnWindowPropertyChanged;
                
                // Set initial host output resolution
                UpdateHostOutputResolution();
                
                viewModel.StartEmulator();
            }
        }, DispatcherPriority.Background);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
        if (e.Property == ClientSizeProperty || e.Property == WindowStateProperty) {
            UpdateHostOutputResolution();
        }
    }

    private void UpdateHostOutputResolution() {
        if (DataContext is MainWindowViewModel viewModel) {
            // Get the window's client area size - this is the actual rendering output size
            int outputWidth = (int)ClientSize.Width;
            int outputHeight = (int)ClientSize.Height;
            
            if (outputHeight > 0) {
                viewModel.UpdateHostOutputResolution(outputWidth, outputHeight);
            }
        }
    }

    private void OnUpdateOpenGlFrame(uint[] frameBuffer, int width, int height) {
        OpenGlVideo?.UpdateFrame(frameBuffer, width, height);
    }

    public static readonly StyledProperty<PerformanceViewModel?> PerformanceViewModelProperty =
        AvaloniaProperty.Register<MainWindow, PerformanceViewModel?>(nameof(PerformanceViewModel),
            defaultValue: null);

    public PerformanceViewModel? PerformanceViewModel {
        get => GetValue(PerformanceViewModelProperty);
        set => SetValue(PerformanceViewModelProperty, value);
    }


    private void OnMenuGotFocus(object? sender, GotFocusEventArgs e) {
        FocusOnVideoBuffer();
        e.Handled = true;
    }

    private void OnMenuKeyUp(object? sender, KeyEventArgs e) {
          (DataContext as MainWindowViewModel)?.OnKeyUp(e);
          e.Handled = true;
    }

    private void OnMenuKeyDown(object? sender, KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyDown(e);
        e.Handled = true;
    }

    private void FocusOnVideoBuffer() {
        OpenGlVideo?.Focus();
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        if (DataContext is not MainWindowViewModel mainVm) {
            return;
        }
        mainVm.CloseMainWindow += (_, _) => Close();
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        FocusOnVideoBuffer();
        var mainWindowViewModel = (DataContext as MainWindowViewModel);
        mainWindowViewModel?.OnKeyUp(e);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        FocusOnVideoBuffer();
        (DataContext as MainWindowViewModel)?.OnKeyDown(e);
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnMainWindowClosing();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e) {
        if (DataContext is MainWindowViewModel viewModel) {
            viewModel.UpdateOpenGlFrame -= OnUpdateOpenGlFrame;
        }
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}