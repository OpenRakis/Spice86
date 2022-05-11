namespace Spice86.Controls;
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
    public static readonly DirectProperty<GroupBox, string> HeaderProperty =
        AvaloniaProperty.RegisterDirect<GroupBox, string>(
            nameof(Header),
            o => o.Header,
            (o, v) => o.Header = o.HeaderUpperCase ? v?.ToUpperInvariant() : v);

    /// <summary>
    /// Defines the <see cref="HeaderBackground"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush> HeaderBackgroundProperty =
        AvaloniaProperty.Register<GroupBox, IBrush>(nameof(HeaderBackground));

    /// <summary>
    /// Defines the <see cref="HeaderForeground"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush> HeaderForegroundProperty =
        AvaloniaProperty.Register<GroupBox, IBrush>(nameof(HeaderForeground));

    /// <summary>
    /// Defines the <see cref="HeaderMargin"/> property.
    /// </summary>
    public static readonly StyledProperty<Thickness> HeaderMarginProperty =
        AvaloniaProperty.Register<GroupBox, Thickness>(nameof(HeaderMargin));

    /// <summary>
    /// Defines the <see cref="HeaderUpperCase"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> HeaderUpperCaseProperty =
        AvaloniaProperty.Register<GroupBox, bool>(nameof(HeaderUpperCase));

    private string _header;

    /// <summary>
    /// Gets or sets a brush with which to paint the Header background.
    /// </summary>
    public IBrush HeaderBackground {
        get { return GetValue(HeaderBackgroundProperty); }
        set { SetValue(HeaderBackgroundProperty, value); }
    }

    /// <summary>
    /// Gets or sets a brush with which to paint the Header text.
    /// </summary>
    public IBrush HeaderForeground {
        get { return GetValue(HeaderForegroundProperty); }
        set { SetValue(HeaderForegroundProperty, value); }
    }

    /// <summary>
    /// Gets or sets a margin for the header text.
    /// </summary>
    public Thickness HeaderMargin {
        get { return GetValue(HeaderMarginProperty); }
        set { SetValue(HeaderMarginProperty, value); }
    }

    /// <summary>
    /// Gets or sets if the header text should be converted to upper-case
    /// </summary>
    public bool HeaderUpperCase {
        get { return GetValue(HeaderUpperCaseProperty); }
        set { SetValue(HeaderUpperCaseProperty, value); }
    }

    /// <summary>
    /// Gets or sets header text
    /// </summary>
    public string Header {
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
