namespace Spice86.UI.Controls;
using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Styling;

public class GroupBox : UserControl, IStyleable {
    /// <summary>
    /// Defines the <see cref="Header"/> property.
    /// </summary>
    public static readonly DirectProperty<GroupBox, string?> HeaderProperty =
        AvaloniaProperty.RegisterDirect<GroupBox, string?>(
            nameof(Header),
            o => o.Header,
            (o, v) => o.Header = v);

    private string? _header;


    /// <summary>
    /// Gets or sets header text
    /// </summary>
    public string? Header {
        get { return _header; }
        set { SetAndRaise(HeaderProperty, ref _header, value); }
    }

    Type IStyleable.StyleKey => typeof(GroupBox);

    public GroupBox() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}
