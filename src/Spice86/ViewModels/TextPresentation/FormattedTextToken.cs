namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

/// <summary>
/// Represents a formatted text token with its kind.
/// </summary>
public class FormattedTextToken {
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of text (used for formatting).
    /// </summary>
    public FormatterTextKind Kind { get; set; }
}