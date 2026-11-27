namespace Spice86.Tests.AsmFixtures;

/// <summary>How a check line is matched against the program-only generated code of a scenario.</summary>
internal enum GeneratedCodeCheckKind {
    /// <summary>The program-only text must contain the text.</summary>
    Contains,
    /// <summary>The program-only text must not contain the text.</summary>
    DoesNotContain,
    /// <summary>One trimmed line of the program-only text must equal the text.</summary>
    MethodSignature
}

/// <summary>One expectation on the program-only generated code of a scenario.</summary>
/// <param name="Kind">How the text is matched.</param>
/// <param name="Text">The text to look for.</param>
internal sealed record GeneratedCodeCheck(GeneratedCodeCheckKind Kind, string Text);
