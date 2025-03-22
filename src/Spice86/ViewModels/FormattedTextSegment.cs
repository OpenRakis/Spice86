namespace Spice86.ViewModels;

using Iced.Intel;

/// <summary>
/// Represents a segment of formatted text with its kind.
/// </summary>
public class FormattedTextSegment {
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of text (used for formatting).
    /// </summary>
    public FormatterTextKind Kind { get; set; }
}