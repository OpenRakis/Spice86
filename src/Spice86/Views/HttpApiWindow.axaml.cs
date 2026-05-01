namespace Spice86.Views;

using System;
using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;

using Spice86.ViewModels;

/// <summary>Code-behind for the HTTP API launcher window.</summary>
public sealed partial class HttpApiWindow : Window {
    /// <summary>Initializes a new instance of <see cref="HttpApiWindow"/>.</summary>
    public HttpApiWindow() {
        InitializeComponent();
    }

    /// <summary>Gets or sets the TCP port the embedded HTTP API server is listening on.</summary>
    internal int Port { get; init; }

    /// <inheritdoc />
    protected override async void OnOpened(EventArgs e) {
        base.OnOpened(e);
        HttpApiViewModel viewModel = await HttpApiViewModel.CreateAsync(Port > 0, Port);
        DataContext = viewModel;
    }

    private void OpenSwaggerUI(object? sender, RoutedEventArgs e) {
        if (DataContext is not HttpApiViewModel viewModel || string.IsNullOrWhiteSpace(viewModel.SwaggerUiUrl)) {
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = viewModel.SwaggerUiUrl, UseShellExecute = true });
    }

    private async void CopyUrlToClipboard(object? sender, RoutedEventArgs e) {
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url)) {
            return;
        }
        if (Clipboard is not null) {
            await Clipboard.SetTextAsync(url);
        }
    }
}