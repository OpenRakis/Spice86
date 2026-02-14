namespace Spice86.Views.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using System;
using System.Collections.Generic;

/// <summary>
/// A waveform display control that shows audio samples as a waveform visualization.
/// Similar to the waveform view in Audacity. Scale-aware so rendering stays crisp
/// inside a <see cref="Viewbox"/> or under DPI scaling.
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
    private static readonly SolidColorBrush BackgroundBrush = new(Color.FromRgb(30, 30, 35));
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
    /// Computes the horizontal and vertical scale factors from the visual transform chain
    /// (Viewbox, DPI, etc.) so drawing can target actual screen pixels.
    /// </summary>
    private (double ScaleX, double ScaleY) GetEffectiveScale() {
        Visual? root = VisualRoot as Visual;
        if (root is null) {
            return (1.0, 1.0);
        }
        Matrix? transform = this.TransformToVisual(root);
        if (!transform.HasValue) {
            return (1.0, 1.0);
        }
        Matrix m = transform.Value;
        double scaleX = Math.Sqrt(m.M11 * m.M11 + m.M21 * m.M21);
        double scaleY = Math.Sqrt(m.M12 * m.M12 + m.M22 * m.M22);
        return (Math.Max(scaleX, 1.0), Math.Max(scaleY, 1.0));
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

        (double scaleX, double scaleY) = GetEffectiveScale();

        Pen leftPen = scaleX > 1.0 ? new Pen(LeftChannelBrush, 1.0 / scaleX) : LeftChannelPen;
        Pen rightPen = scaleX > 1.0 ? new Pen(RightChannelBrush, 1.0 / scaleX) : RightChannelPen;
        Pen centerPen = scaleX > 1.0 ? new Pen(CenterLineBrush, 0.5 / scaleX) : CenterLinePen;

        context.FillRectangle(BackgroundBrush, new Rect(0, 0, width, height));

        double channelHeight = (height - WaveformPadding) / 2.0;
        double leftCenterY = channelHeight / 2.0;
        double rightCenterY = channelHeight + WaveformPadding + channelHeight / 2.0;

        context.DrawLine(centerPen, new Point(0, leftCenterY), new Point(width, leftCenterY));
        context.DrawLine(centerPen, new Point(0, rightCenterY), new Point(width, rightCenterY));

        IReadOnlyList<float>? leftSamples = SamplesLeft;
        if (leftSamples is { Count: > 0 }) {
            DrawWaveform(context, leftSamples, 0, channelHeight, width, scaleX, scaleY, leftPen);
        }

        IReadOnlyList<float>? rightSamples = SamplesRight;
        if (rightSamples is { Count: > 0 }) {
            DrawWaveform(context, rightSamples, channelHeight + WaveformPadding, channelHeight, width, scaleX, scaleY, rightPen);
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
        double scaleX,
        double scaleY,
        Pen pen) {

        int sampleCount = samples.Count;
        if (sampleCount == 0) {
            return;
        }

        double centerY = yOffset + channelHeight / 2.0;
        double amplitude = (channelHeight / 2.0) - 2.0;

        double effectiveWidth = width * scaleX;
        double samplesPerPixel = sampleCount / effectiveWidth;

        if (samplesPerPixel <= 1.0) {
            DrawPointToPoint(context, samples, centerY, amplitude, width, pen);
        } else {
            DrawEnvelope(context, samples, centerY, amplitude, effectiveWidth, scaleX, scaleY, pen);
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
    /// Draws waveform as min/max envelope at effective screen resolution.
    /// Each iteration targets one actual screen pixel, mapped back to local coordinates.
    /// </summary>
    private static void DrawEnvelope(
        DrawingContext context,
        IReadOnlyList<float> samples,
        double centerY,
        double amplitude,
        double effectiveWidth,
        double scaleX,
        double scaleY,
        Pen pen) {

        int sampleCount = samples.Count;
        int pixelCount = (int)effectiveWidth;
        double samplesPerPixel = (double)sampleCount / effectiveWidth;
        double inverseScaleX = 1.0 / scaleX;
        double minLineHeight = 1.0 / scaleY;

        for (int pixel = 0; pixel < pixelCount; pixel++) {
            int startSample = (int)(pixel * samplesPerPixel);
            int endSample = Math.Min((int)((pixel + 1) * samplesPerPixel), sampleCount);

            if (startSample >= sampleCount) {
                break;
            }

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

            double x = pixel * inverseScaleX;
            double yMin = centerY - maxVal * amplitude;
            double yMax = centerY - minVal * amplitude;

            if (Math.Abs(yMax - yMin) < minLineHeight) {
                double mid = (yMin + yMax) / 2.0;
                yMin = mid - minLineHeight / 2.0;
                yMax = mid + minLineHeight / 2.0;
            }

            context.DrawLine(pen, new Point(x, yMin), new Point(x, yMax));
        }
    }
}
