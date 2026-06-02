namespace Spice86.Views.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using Spice86.Shared.Emulator.Storage;

using System;
using System.Collections.Generic;

/// <summary>
/// Compact visualisation of a <see cref="DriveContentMap"/> shown in a tooltip.
/// Renders horizontal track stripes for CD-ROMs or a wrap-panel of cluster cells for FAT volumes.
/// </summary>
public partial class DriveContentVisualization : UserControl {
    private const double CdVisualSize = 312;
    private const double CdOuterRadius = 148;
    private const double CdInnerRadius = 54;
    private const double StartAngleDegrees = -90;

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
        LoadView();
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

    private void LoadView() {
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
        IReadOnlyList<DriveCdTrackInfo>? tracks = map.Tracks;
        if (tracks == null) {
            Header = "No CD track information available";
            Legend = string.Empty;
            Body = null;
            return;
        }
        Header = $"Drive {map.DriveLetter}: Compact Disc Visual";

        Grid layout = new() {
            ColumnDefinitions = new ColumnDefinitions("340,*"),
            RowDefinitions = new RowDefinitions("Auto")
        };

        Grid cdVisual = BuildCdVisual(tracks, map.TotalSectors);
        layout.Children.Add(cdVisual);
        Grid.SetColumn(cdVisual, 0);

        StackPanel trackList = new() {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Margin = new Thickness(14, 2, 0, 0)
        };

        uint totalSectors = map.TotalSectors == 0 ? 1u : map.TotalSectors;
        for (int i = 0; i < tracks.Count; i++) {
            DriveCdTrackInfo track = tracks[i];
            IBrush color = GetTrackBrush(track.IsAudio);
            double percent = track.LengthSectors * 100.0 / totalSectors;

            Grid row = new() {
                ColumnDefinitions = new ColumnDefinitions("18,Auto,*")
            };

            Border swatch = new() {
                Width = 12,
                Height = 12,
                Margin = new Thickness(0, 2, 0, 0),
                Background = color,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };
            row.Children.Add(swatch);

            TextBlock label = new() {
                Text = $"T{track.Number:00}",
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(6, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(label);
            Grid.SetColumn(label, 1);

            TextBlock meta = new() {
                Text = $"{(track.IsAudio ? "Audio" : "Data")}  {track.LengthSectors:N0} sectors  ({percent:0.0}%)",
                Foreground = new SolidColorBrush(Color.Parse("#BFD0E8")),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(meta);
            Grid.SetColumn(meta, 2);

            trackList.Children.Add(row);
        }

        layout.Children.Add(trackList);
        Grid.SetColumn(trackList, 1);

        Body = layout;
        Legend = "Circular ring segments match track length. Warm tones = audio tracks, cool tones = data tracks.";
    }

    private static Grid BuildCdVisual(IReadOnlyList<DriveCdTrackInfo> tracks, uint totalSectorsRaw) {
        Grid root = new() {
            Width = CdVisualSize,
            Height = CdVisualSize
        };

        Ellipse discBase = new() {
            Width = CdVisualSize,
            Height = CdVisualSize,
            Fill = new RadialGradientBrush {
                GradientOrigin = new RelativePoint(0.25, 0.25, RelativeUnit.Relative),
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientStops = new GradientStops {
                    new GradientStop(Color.Parse("#F6FBFF"), 0.0),
                    new GradientStop(Color.Parse("#AFBECF"), 0.52),
                    new GradientStop(Color.Parse("#708198"), 1.0)
                }
            },
            Stroke = new SolidColorBrush(Color.Parse("#2E3948")),
            StrokeThickness = 2
        };
        root.Children.Add(discBase);

        Canvas trackCanvas = new() {
            Width = CdVisualSize,
            Height = CdVisualSize,
            IsHitTestVisible = false
        };

        uint totalSectors = totalSectorsRaw == 0 ? 1u : totalSectorsRaw;
        double startAngle = StartAngleDegrees;
        for (int i = 0; i < tracks.Count; i++) {
            DriveCdTrackInfo track = tracks[i];
            double sweep = 360.0 * track.LengthSectors / totalSectors;
            if (sweep < 1.5) {
                sweep = 1.5;
            }

            Path slice = CreateRingSlice(startAngle, sweep, GetTrackBrush(track.IsAudio));
            ToolTip.SetTip(slice, $"Track {track.Number} ({(track.IsAudio ? "Audio" : "Data")})\n{track.LengthSectors:N0} sectors");
            trackCanvas.Children.Add(slice);
            startAngle += sweep;
        }
        root.Children.Add(trackCanvas);

        Ellipse spindleHole = new() {
            Width = CdInnerRadius * 2,
            Height = CdInnerRadius * 2,
            Fill = new RadialGradientBrush {
                GradientOrigin = new RelativePoint(0.3, 0.3, RelativeUnit.Relative),
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientStops = new GradientStops {
                    new GradientStop(Color.Parse("#1A1F27"), 0.0),
                    new GradientStop(Color.Parse("#0B0E14"), 1.0)
                }
            },
            Stroke = new SolidColorBrush(Color.Parse("#CCD8E8")),
            StrokeThickness = 2
        };
        root.Children.Add(spindleHole);

        TextBlock discLabel = new() {
            Text = "COMPACT DISC",
            Foreground = new SolidColorBrush(Color.Parse("#1C2736")),
            FontWeight = FontWeight.Bold,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 52, 0, 0)
        };
        root.Children.Add(discLabel);

        return root;
    }

    private static Path CreateRingSlice(double startAngleDegrees, double sweepDegrees, IBrush fill) {
        if (sweepDegrees > 359.95) {
            sweepDegrees = 359.95;
        }

        Point center = new(CdVisualSize / 2, CdVisualSize / 2);
        Point outerStart = PointOnCircle(center, CdOuterRadius, startAngleDegrees);
        Point outerEnd = PointOnCircle(center, CdOuterRadius, startAngleDegrees + sweepDegrees);
        Point innerEnd = PointOnCircle(center, CdInnerRadius, startAngleDegrees + sweepDegrees);
        Point innerStart = PointOnCircle(center, CdInnerRadius, startAngleDegrees);
        bool isLargeArc = sweepDegrees >= 180;

        PathFigure figure = new() {
            StartPoint = outerStart,
            IsClosed = true,
            Segments = new PathSegments {
                new ArcSegment {
                    Point = outerEnd,
                    Size = new Size(CdOuterRadius, CdOuterRadius),
                    SweepDirection = SweepDirection.Clockwise,
                    IsLargeArc = isLargeArc
                },
                new LineSegment {
                    Point = innerEnd
                },
                new ArcSegment {
                    Point = innerStart,
                    Size = new Size(CdInnerRadius, CdInnerRadius),
                    SweepDirection = SweepDirection.CounterClockwise,
                    IsLargeArc = isLargeArc
                }
            }
        };

        PathGeometry geometry = new() {
            Figures = new PathFigures { figure }
        };

        return new Path {
            Data = geometry,
            Fill = fill,
            Stroke = new SolidColorBrush(Color.Parse("#1B2430")),
            StrokeThickness = 1
        };
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees) {
        double angleRadians = angleDegrees * Math.PI / 180.0;
        double x = center.X + radius * Math.Cos(angleRadians);
        double y = center.Y + radius * Math.Sin(angleRadians);
        return new Point(x, y);
    }

    private static IBrush GetTrackBrush(bool isAudioTrack) {
        if (isAudioTrack) {
            return new SolidColorBrush(Color.Parse("#F37B36"));
        }
        return new SolidColorBrush(Color.Parse("#4FA3F7"));
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