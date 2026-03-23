namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

/// <summary>
/// Represents a formatted text offset with its kind.
/// </summary>
public class FormattedTextOffset {
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of text (used for formatting).
    /// </summary>
    public FormatterTextKind Kind { get; set; }
}