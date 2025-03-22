namespace Spice86.ViewModels;

using Iced.Intel;

/// <summary>
/// Thread-safe formatter output that doesn't create UI elements.
/// </summary>
public class ThreadSafeFormatterOutput : FormatterOutput {
    /// <summary>
    /// Gets the list of formatted text segments.
    /// </summary>
    public List<FormattedTextSegment> Segments { get; } = [];

    /// <summary>
    /// Writes a segment of text with the specified kind.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <param name="kind">The kind of text.</param>
    public override void Write(string text, FormatterTextKind kind) {
        Segments.Add(new FormattedTextSegment {
            Text = text,
            Kind = kind
        });
    }
}