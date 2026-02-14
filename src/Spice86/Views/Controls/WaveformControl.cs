namespace Spice86.Views.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using System;
using System.Collections.Generic;

/// <summary>
/// A waveform display control that shows audio samples as a waveform visualization.
/// Similar to the waveform view in Audacity.
/// </summary>
public sealed class WaveformControl : Control {
    private const double WaveformPadding = 2.0;

    /// <summary>
    /// Defines the SamplesLeft property for left channel waveform data.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> SamplesLeftProperty =
        AvaloniaProperty.Register<WaveformControl, IReadOnlyList<float>?>(nameof(SamplesLeft));

    /// <summary>
    /// Defines the SamplesRight property for right channel waveform data.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> SamplesRightProperty =
        AvaloniaProperty.Register<WaveformControl, IReadOnlyList<float>?>(nameof(SamplesRight));

    /// <summary>
    /// Gets or sets the left channel samples for waveform display.
    /// Values should be normalized to -1.0 to 1.0 range.
    /// </summary>
    public IReadOnlyList<float>? SamplesLeft {
        get => GetValue(SamplesLeftProperty);
        set => SetValue(SamplesLeftProperty, value);
    }

    /// <summary>
    /// Gets or sets the right channel samples for waveform display.
    /// Values should be normalized to -1.0 to 1.0 range.
    /// </summary>
    public IReadOnlyList<float>? SamplesRight {
        get => GetValue(SamplesRightProperty);
        set => SetValue(SamplesRightProperty, value);
    }

    private static readonly SolidColorBrush LeftChannelBrush = new(Color.FromRgb(100, 180, 100));
    private static readonly SolidColorBrush RightChannelBrush = new(Color.FromRgb(100, 140, 200));
    private static readonly SolidColorBrush CenterLineBrush = new(Color.FromRgb(80, 80, 80));
    private static readonly Pen LeftChannelPen = new(LeftChannelBrush, 1.0);
    private static readonly Pen RightChannelPen = new(RightChannelBrush, 1.0);
    private static readonly Pen CenterLinePen = new(CenterLineBrush, 0.5);

    static WaveformControl() {
        AffectsRender<WaveformControl>(SamplesLeftProperty, SamplesRightProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WaveformControl"/> class.
    /// </summary>
    public WaveformControl() {
        MinHeight = 60;
        MinWidth = 100;
    }

    /// <summary>
    /// Renders the waveform display.
    /// </summary>
    public override void Render(DrawingContext context) {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;

        if (width <= 0 || height <= 0) {
            return;
        }

        // Draw background
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 35)), new Rect(0, 0, width, height));

        // Calculate channel heights (split vertically for stereo)
        double channelHeight = (height - WaveformPadding) / 2.0;
        double leftCenterY = channelHeight / 2.0;
        double rightCenterY = channelHeight + WaveformPadding + channelHeight / 2.0;

        // Draw center lines
        context.DrawLine(CenterLinePen, new Point(0, leftCenterY), new Point(width, leftCenterY));
        context.DrawLine(CenterLinePen, new Point(0, rightCenterY), new Point(width, rightCenterY));

        // Draw left channel waveform
        IReadOnlyList<float>? leftSamples = SamplesLeft;
        if (leftSamples is { Count: > 0 }) {
            DrawWaveform(context, leftSamples, 0, channelHeight, width, LeftChannelPen);
        }

        // Draw right channel waveform
        IReadOnlyList<float>? rightSamples = SamplesRight;
        if (rightSamples is { Count: > 0 }) {
            DrawWaveform(context, rightSamples, channelHeight + WaveformPadding, channelHeight, width, RightChannelPen);
        }
    }

    /// <summary>
    /// Draws a waveform for a single channel.
    /// </summary>
    private static void DrawWaveform(
        DrawingContext context,
        IReadOnlyList<float> samples,
        double yOffset,
        double channelHeight,
        double width,
        Pen pen) {

        int sampleCount = samples.Count;
        if (sampleCount == 0) {
            return;
        }

        double centerY = yOffset + channelHeight / 2.0;
        double amplitude = (channelHeight / 2.0) - 2.0; // Leave 2px margin

        // Calculate how many samples per pixel
        double samplesPerPixel = (double)sampleCount / width;

        if (samplesPerPixel <= 1.0) {
            // Less samples than pixels - draw point to point
            DrawPointToPoint(context, samples, centerY, amplitude, width, pen);
        } else {
            // More samples than pixels - draw min/max envelope
            DrawEnvelope(context, samples, centerY, amplitude, width, samplesPerPixel, pen);
        }
    }

    /// <summary>
    /// Draws waveform as connected points (when zoomed in or few samples).
    /// </summary>
    private static void DrawPointToPoint(
        DrawingContext context,
        IReadOnlyList<float> samples,
        double centerY,
        double amplitude,
        double width,
        Pen pen) {

        int sampleCount = samples.Count;
        double pixelsPerSample = width / Math.Max(sampleCount - 1, 1);

        Point? lastPoint = null;

        for (int i = 0; i < sampleCount; i++) {
            double x = i * pixelsPerSample;
            double y = centerY - samples[i] * amplitude;
            Point currentPoint = new(x, y);

            if (lastPoint.HasValue) {
                context.DrawLine(pen, lastPoint.Value, currentPoint);
            }

            lastPoint = currentPoint;
        }
    }

    /// <summary>
    /// Draws waveform as min/max envelope (when zoomed out or many samples).
    /// This is the Audacity-style rendering for efficiency.
    /// </summary>
    private static void DrawEnvelope(
        DrawingContext context,
        IReadOnlyList<float> samples,
        double centerY,
        double amplitude,
        double width,
        double samplesPerPixel,
        Pen pen) {

        int sampleCount = samples.Count;
        int pixelCount = (int)width;

        for (int pixel = 0; pixel < pixelCount; pixel++) {
            int startSample = (int)(pixel * samplesPerPixel);
            int endSample = Math.Min((int)((pixel + 1) * samplesPerPixel), sampleCount);

            if (startSample >= sampleCount) {
                break;
            }

            // Find min and max in this pixel's sample range
            float minVal = samples[startSample];
            float maxVal = samples[startSample];

            for (int i = startSample + 1; i < endSample; i++) {
                float sample = samples[i];
                if (sample < minVal) {
                    minVal = sample;
                }
                if (sample > maxVal) {
                    maxVal = sample;
                }
            }

            // Draw vertical line from min to max
            double yMin = centerY - maxVal * amplitude;
            double yMax = centerY - minVal * amplitude;

            // Ensure we draw at least 1 pixel
            if (Math.Abs(yMax - yMin) < 1.0) {
                double mid = (yMin + yMax) / 2.0;
                yMin = mid - 0.5;
                yMax = mid + 0.5;
            }

            context.DrawLine(pen, new Point(pixel, yMin), new Point(pixel, yMax));
        }
    }
}
