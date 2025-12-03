namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Parser;

/// <summary>
/// Exception thrown when parsing a breakpoint condition expression fails.
/// Includes the character index where the error occurred for user feedback.
/// </summary>
public class ExpressionParseException : Exception {
    /// <summary>
    /// The position in the input string where the parsing error occurred.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// The input expression that failed to parse.
    /// </summary>
    public string Expression { get; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">The error message describing what went wrong.</param>
    /// <param name="expression">The input expression that failed to parse.</param>
    /// <param name="position">The character index where the error occurred.</param>
    public ExpressionParseException(string message, string expression, int position)
        : base($"{message} at position {position}") {
        Expression = expression;
        Position = position;
    }

    /// <inheritdoc />
    public override string ToString() {
        string contextSnippet = GetContextSnippet();
        return $"Expression Parse Error at position {Position}: {Message}\n{contextSnippet}";
    }

    private string GetContextSnippet() {
        if (string.IsNullOrEmpty(Expression)) {
            return string.Empty;
        }

        int start = Math.Max(0, Position - 10);
        int end = Math.Min(Expression.Length, Position + 10);
        string snippet = Expression.Substring(start, end - start);
        int caretPosition = Position - start;
        string caret = new string(' ', caretPosition) + "^";
        
        return $"  {snippet}\n  {caret}";
    }
}
