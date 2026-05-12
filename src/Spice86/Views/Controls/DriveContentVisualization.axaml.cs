namespace Spice86.Views.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using Spice86.Shared.Emulator.Storage;

using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Compact visualisation of a <see cref="DriveContentMap"/> shown in a tooltip.
/// Renders horizontal track stripes for CD-ROMs or a wrap-panel of cluster cells for FAT volumes.
/// </summary>
public partial class DriveContentVisualization : UserControl {
    /// <summary>Identifies the <see cref="ContentMap"/> styled property.</summary>
    public static readonly StyledProperty<DriveContentMap?> ContentMapProperty =
        AvaloniaProperty.Register<DriveContentVisualization, DriveContentMap?>(nameof(ContentMap));

    /// <summary>Identifies the <see cref="Header"/> direct property.</summary>
    public static readonly DirectProperty<DriveContentVisualization, string> HeaderProperty =
        AvaloniaProperty.RegisterDirect<DriveContentVisualization, string>(nameof(Header), o => o.Header);

    /// <summary>Identifies the <see cref="Legend"/> direct property.</summary>
    public static readonly DirectProperty<DriveContentVisualization, string> LegendProperty =
        AvaloniaProperty.RegisterDirect<DriveContentVisualization, string>(nameof(Legend), o => o.Legend);

    /// <summary>Identifies the <see cref="Body"/> direct property.</summary>
    public static readonly DirectProperty<DriveContentVisualization, Control?> BodyProperty =
        AvaloniaProperty.RegisterDirect<DriveContentVisualization, Control?>(nameof(Body), o => o.Body);

    private string _header = string.Empty;
    private string _legend = string.Empty;
    private Control? _body;

    /// <summary>Initialises a new instance of <see cref="DriveContentVisualization"/>.</summary>
    public DriveContentVisualization() {
        InitializeComponent();
        Rebuild();
    }

    /// <summary>Gets or sets the content map to visualise.</summary>
    public DriveContentMap? ContentMap {
        get => GetValue(ContentMapProperty);
        set => SetValue(ContentMapProperty, value);
    }

    /// <summary>Gets the header text describing the visualisation.</summary>
    public string Header {
        get => _header;
        private set => SetAndRaise(HeaderProperty, ref _header, value);
    }

    /// <summary>Gets the legend text shown beneath the visualisation.</summary>
    public string Legend {
        get => _legend;
        private set => SetAndRaise(LegendProperty, ref _legend, value);
    }

    /// <summary>Gets the rendered body control (track stripe or cluster grid).</summary>
    public Control? Body {
        get => _body;
        private set => SetAndRaise(BodyProperty, ref _body, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == ContentMapProperty) {
            Rebuild();
        }
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void Rebuild() {
        DriveContentMap? map = ContentMap;
        if (map == null) {
            Header = "No layout information available";
            Legend = string.Empty;
            Body = null;
            return;
        }
        if (map.IsCdRom && map.Tracks != null) {
            BuildCdLayout(map);
        } else if (map.IsFat && map.Clusters != null) {
            BuildFatLayout(map);
        } else {
            Header = string.Empty;
            Legend = string.Empty;
            Body = null;
        }
    }

    private void BuildCdLayout(DriveContentMap map) {
        IReadOnlyList<DriveCdTrackInfo> tracks = map.Tracks!;
        Header = $"Drive {map.DriveLetter}: - {tracks.Count} track(s)";
        StackPanel panel = new() {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            Height = 28
        };
        uint total = map.TotalSectors == 0 ? 1u : map.TotalSectors;
        const double maxWidth = 300.0;
        for (int i = 0; i < tracks.Count; i++) {
            DriveCdTrackInfo track = tracks[i];
            double width = System.Math.Max(8.0, maxWidth * track.LengthSectors / total);
            Border cell = new() {
                Width = width,
                Height = 28,
                Background = track.IsAudio ? Brushes.MediumPurple : Brushes.SteelBlue,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Child = new TextBlock {
                    Text = track.Number.ToString(CultureInfo.InvariantCulture),
                    Foreground = Brushes.White,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(cell, $"Track {track.Number} ({(track.IsAudio ? "audio" : "data")}) - {track.LengthSectors} sectors");
            panel.Children.Add(cell);
        }
        Body = panel;
        Legend = "Purple = audio, blue = data";
    }

    private void BuildFatLayout(DriveContentMap map) {
        IReadOnlyList<DriveClusterInfo> clusters = map.Clusters!;
        int shown = clusters.Count;
        int total = map.TotalClusters;
        Header = total > shown
            ? $"Drive {map.DriveLetter}: - clusters 0..{shown - 1} of {total}"
            : $"Drive {map.DriveLetter}: - {shown} cluster(s)";
        WrapPanel panel = new() {
            Orientation = Orientation.Horizontal,
            MaxWidth = 320
        };
        for (int i = 0; i < clusters.Count; i++) {
            DriveClusterState state = clusters[i].State;
            IBrush fill = state switch {
                DriveClusterState.Used => Brushes.RoyalBlue,
                DriveClusterState.Bad => Brushes.Red,
                DriveClusterState.Reserved => Brushes.DimGray,
                _ => Brushes.LightGray
            };
            Rectangle cell = new() {
                Width = 4,
                Height = 4,
                Fill = fill,
                Margin = new Thickness(0)
            };
            panel.Children.Add(cell);
        }
        Body = panel;
        Legend = "Blue = used, gray = free, red = bad";
    }
}
