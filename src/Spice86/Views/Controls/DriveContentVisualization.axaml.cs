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
        } else if (map.IsFloppy && map.Clusters != null) {
            BuildFloppyLayout(map);
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
            ColumnDefinitions = new ColumnDefinitions("320,*"),
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
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
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

    private void BuildFloppyLayout(DriveContentMap map) {
        IReadOnlyList<DriveClusterInfo>? clusters = map.Clusters;
        if (clusters == null) {
            Header = "No floppy layout information available";
            Legend = string.Empty;
            Body = null;
            return;
        }

        int shown = clusters.Count;
        int total = map.TotalClusters > 0 ? map.TotalClusters : shown;
        int used = 0;
        int bad = 0;
        int reserved = 0;
        for (int i = 0; i < clusters.Count; i++) {
            DriveClusterState state = clusters[i].State;
            if (state == DriveClusterState.Used) {
                used++;
            } else if (state == DriveClusterState.Bad) {
                bad++;
            } else if (state == DriveClusterState.Reserved) {
                reserved++;
            }
        }
        double usedPercent = total > 0 ? used * 100.0 / total : 0.0;

        Header = $"Drive {map.DriveLetter}: Floppy Disk";

        Grid layout = new() {
            ColumnDefinitions = new ColumnDefinitions("260,*"),
            RowDefinitions = new RowDefinitions("Auto")
        };

        Control floppyVisual = BuildFloppyVisual(map.DriveLetter, usedPercent, total);
        layout.Children.Add(floppyVisual);
        Grid.SetColumn(floppyVisual, 0);

        StackPanel right = new() {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Margin = new Thickness(14, 4, 0, 0)
        };

        right.Children.Add(BuildStatRow("Drive", $"{map.DriveLetter}:"));
        if (!string.IsNullOrEmpty(map.FileSystemLabel)) {
            right.Children.Add(BuildStatRow("Filesystem", map.FileSystemLabel));
        }
        right.Children.Add(BuildStatRow("Clusters", $"{used:N0} used / {total:N0} total"));
        right.Children.Add(BuildStatRow("Usage", $"{usedPercent:0.0}%"));
        if (reserved > 0) {
            right.Children.Add(BuildStatRow("Reserved", $"{reserved:N0}"));
        }
        if (bad > 0) {
            right.Children.Add(BuildStatRow("Bad", $"{bad:N0}"));
        }

        TextBlock mapHeader = new() {
            Text = total > shown
                ? $"Cluster map (first {shown:N0} of {total:N0})"
                : "Cluster map",
            Foreground = new SolidColorBrush(Color.Parse("#BFD0E8")),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 6, 0, 2)
        };
        right.Children.Add(mapHeader);

        right.Children.Add(BuildClusterGrid(clusters, cellSize: 10, columns: 64));

        layout.Children.Add(right);
        Grid.SetColumn(right, 1);

        Body = layout;
        Legend = "Blue = used, gray = free, red = bad, dim = reserved.";
    }

    private static Control BuildFloppyVisual(char driveLetter, double usedPercent, int totalClusters) {
        const double bodyW = 240;
        const double bodyH = 268;
        const double shutterW = 188;
        const double shutterH = 54;
        const double labelW = 200;
        const double labelH = 130;

        Canvas root = new() {
            Width = bodyW + 8,
            Height = bodyH + 8
        };

        Rectangle body = new() {
            Width = bodyW,
            Height = bodyH,
            RadiusX = 10,
            RadiusY = 10,
            Fill = new LinearGradientBrush {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops {
                    new GradientStop(Color.Parse("#3A4654"), 0.0),
                    new GradientStop(Color.Parse("#1E2632"), 1.0)
                }
            },
            Stroke = new SolidColorBrush(Color.Parse("#0B0F15")),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(body, 4);
        Canvas.SetTop(body, 4);
        root.Children.Add(body);

        // Write-protect notch (top-left)
        Rectangle notch = new() {
            Width = 20,
            Height = 12,
            Fill = new SolidColorBrush(Color.Parse("#0A0E14"))
        };
        Canvas.SetLeft(notch, 12);
        Canvas.SetTop(notch, 4);
        root.Children.Add(notch);

        // Characteristic cut corner on 3.5-inch floppy shells.
        Polygon cutCorner = new() {
            Fill = new SolidColorBrush(Color.Parse("#0A0E14")),
            Points = new Points {
                new Point(4 + bodyW - 42, 4 + bodyH),
                new Point(4 + bodyW, 4 + bodyH),
                new Point(4 + bodyW, 4 + bodyH - 42)
            }
        };
        root.Children.Add(cutCorner);

        // Metal shutter
        double shutterX = 4 + (bodyW - shutterW) / 2;
        const double shutterY = 18;
        Rectangle shutter = new() {
            Width = shutterW,
            Height = shutterH,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new LinearGradientBrush {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops {
                    new GradientStop(Color.Parse("#EEF2F6"), 0.0),
                    new GradientStop(Color.Parse("#A9B4C0"), 0.5),
                    new GradientStop(Color.Parse("#7D8997"), 1.0)
                }
            },
            Stroke = new SolidColorBrush(Color.Parse("#3A4654")),
            StrokeThickness = 1
        };
        Canvas.SetLeft(shutter, shutterX);
        Canvas.SetTop(shutter, shutterY);
        root.Children.Add(shutter);

        // Shutter slot (read/write head opening)
        Rectangle slot = new() {
            Width = shutterW - 56,
            Height = 16,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new SolidColorBrush(Color.Parse("#0B0F15"))
        };
        Canvas.SetLeft(slot, shutterX + 20);
        Canvas.SetTop(slot, shutterY + (shutterH - 16) / 2);
        root.Children.Add(slot);

        // Right latch window typically present on floppy shutter assemblies.
        Rectangle latchWindow = new() {
            Width = 11,
            Height = 22,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new SolidColorBrush(Color.Parse("#626F7F")),
            Stroke = new SolidColorBrush(Color.Parse("#42505F")),
            StrokeThickness = 1
        };
        Canvas.SetLeft(latchWindow, shutterX + shutterW - 18);
        Canvas.SetTop(latchWindow, shutterY + (shutterH - 22) / 2);
        root.Children.Add(latchWindow);

        // Rivet details to make the shutter read as stamped metal.
        Ellipse rivetLeft = new() {
            Width = 5,
            Height = 5,
            Fill = new SolidColorBrush(Color.Parse("#7C8795")),
            Stroke = new SolidColorBrush(Color.Parse("#4C5866")),
            StrokeThickness = 1
        };
        Canvas.SetLeft(rivetLeft, shutterX + 10);
        Canvas.SetTop(rivetLeft, shutterY + 8);
        root.Children.Add(rivetLeft);

        Ellipse rivetRight = new() {
            Width = 5,
            Height = 5,
            Fill = new SolidColorBrush(Color.Parse("#7C8795")),
            Stroke = new SolidColorBrush(Color.Parse("#4C5866")),
            StrokeThickness = 1
        };
        Canvas.SetLeft(rivetRight, shutterX + shutterW - 16);
        Canvas.SetTop(rivetRight, shutterY + 8);
        root.Children.Add(rivetRight);

        // Label
        double labelX = 4 + (bodyW - labelW) / 2;
        double labelY = shutterY + shutterH + 14;
        Rectangle label = new() {
            Width = labelW,
            Height = labelH,
            RadiusX = 4,
            RadiusY = 4,
            Fill = new LinearGradientBrush {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops {
                    new GradientStop(Color.Parse("#FBFCFE"), 0.0),
                    new GradientStop(Color.Parse("#E2E8EF"), 1.0)
                }
            },
            Stroke = new SolidColorBrush(Color.Parse("#8A95A3")),
            StrokeThickness = 1
        };
        Canvas.SetLeft(label, labelX);
        Canvas.SetTop(label, labelY);
        root.Children.Add(label);

        // Drive letter (big)
        TextBlock letter = new() {
            Text = $"{driveLetter}:",
            Foreground = new SolidColorBrush(Color.Parse("#1C2736")),
            FontWeight = FontWeight.Bold,
            FontSize = 44,
            Width = labelW,
            TextAlignment = TextAlignment.Center
        };
        Canvas.SetLeft(letter, labelX);
        Canvas.SetTop(letter, labelY + 8);
        root.Children.Add(letter);

        // Subtitle
        TextBlock subtitle = new() {
            Text = "Virtual Disk",
            Foreground = new SolidColorBrush(Color.Parse("#4A5664")),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Width = labelW,
            TextAlignment = TextAlignment.Center
        };
        Canvas.SetLeft(subtitle, labelX);
        Canvas.SetTop(subtitle, labelY + 64);
        root.Children.Add(subtitle);

        // Usage bar
        const double barW = labelW - 28;
        const double barH = 10;
        double barX = labelX + 14;
        double barY = labelY + labelH - 26;
        Rectangle barBack = new() {
            Width = barW,
            Height = barH,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new SolidColorBrush(Color.Parse("#C4CCD6"))
        };
        Canvas.SetLeft(barBack, barX);
        Canvas.SetTop(barBack, barY);
        root.Children.Add(barBack);

        double fillFraction = usedPercent / 100.0;
        if (fillFraction < 0) {
            fillFraction = 0;
        }
        if (fillFraction > 1) {
            fillFraction = 1;
        }
        double fillWidth = barW * fillFraction;
        if (fillWidth > 0) {
            Rectangle barFill = new() {
                Width = fillWidth,
                Height = barH,
                RadiusX = 3,
                RadiusY = 3,
                Fill = new SolidColorBrush(Color.Parse("#4FA3F7"))
            };
            Canvas.SetLeft(barFill, barX);
            Canvas.SetTop(barFill, barY);
            root.Children.Add(barFill);
        }

        TextBlock barText = new() {
            Text = totalClusters > 0
                ? $"{usedPercent:0.0}% used"
                : "empty / unformatted",
            Foreground = new SolidColorBrush(Color.Parse("#4A5664")),
            FontSize = 10,
            Width = labelW,
            TextAlignment = TextAlignment.Center
        };
        Canvas.SetLeft(barText, labelX);
        Canvas.SetTop(barText, barY - 14);
        root.Children.Add(barText);

        return root;
    }

    private static Grid BuildStatRow(string label, string value) {
        Grid row = new() {
            ColumnDefinitions = new ColumnDefinitions("80,*")
        };
        TextBlock labelBlock = new() {
            Text = label,
            Foreground = new SolidColorBrush(Color.Parse("#8FA0B8")),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(labelBlock);
        TextBlock valueBlock = new() {
            Text = value,
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);
        return row;
    }

    private static WrapPanel BuildClusterGrid(IReadOnlyList<DriveClusterInfo> clusters, double cellSize, int columns) {
        const double gap = 2;
        double slot = cellSize + gap;
        WrapPanel panel = new() {
            Orientation = Orientation.Horizontal,
            MaxWidth = slot * columns
        };
        for (int i = 0; i < clusters.Count; i++) {
            DriveClusterState state = clusters[i].State;
            (IBrush fill, IBrush border) = state switch {
                DriveClusterState.Used => ((IBrush)new SolidColorBrush(Color.Parse("#4FA3F7")), (IBrush)new SolidColorBrush(Color.Parse("#1F4E7A"))),
                DriveClusterState.Bad => ((IBrush)new SolidColorBrush(Color.Parse("#E04545")), (IBrush)new SolidColorBrush(Color.Parse("#6B1414"))),
                DriveClusterState.Reserved => ((IBrush)new SolidColorBrush(Color.Parse("#5A6776")), (IBrush)new SolidColorBrush(Color.Parse("#2A323D"))),
                _ => ((IBrush)new SolidColorBrush(Color.Parse("#CCD3DC")), (IBrush)new SolidColorBrush(Color.Parse("#6A7280")))
            };
            Border cell = new() {
                Width = cellSize,
                Height = cellSize,
                Background = fill,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, gap, gap)
            };
            panel.Children.Add(cell);
        }
        return panel;
    }
}