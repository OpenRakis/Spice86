namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

/// <summary>
/// Represents a token of formatted text with its syntax kind.
/// </summary>
public class FormattedToken {
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of text (used for syntax highlighting).
    /// </summary>
    public FormatterTextKind Kind { get; set; }
}
