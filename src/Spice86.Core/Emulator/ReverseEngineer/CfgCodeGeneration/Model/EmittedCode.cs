namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

using System.Linq;

/// <summary>
/// The value an execution-AST node lowers to under the C# generator: either a single <em>expression</em>
/// (the precedence/type-carrying <see cref="CSharpFragment"/>) or an ordered sequence of <em>statements</em>.
/// Lowering decisions become inspectable data that <see cref="EmittedCodeRenderer"/> renders mechanically
/// through <see cref="CSharpSourceWriter"/>. Statement-vs-expression position is resolved through the
/// expression arm's precedence (see <see cref="CSharpFragment"/>), not emitter state.
/// </summary>
internal abstract record EmittedCode {
    /// <summary>An empty statement sequence (no output).</summary>
    public static EmittedCode None { get; } = new StatementsCode([]);

    /// <summary>Wraps an expression fragment as an expression-arm value.</summary>
    public static EmittedCode Expression(CSharpFragment fragment) => new ExpressionCode(fragment);

    /// <summary>Builds a statement-arm value from ordered statement items.</summary>
    public static EmittedCode Statements(IEnumerable<StatementItem> items) => new StatementsCode(items.ToList());

    /// <summary>Builds a statement-arm value from statement items.</summary>
    public static EmittedCode Statements(params StatementItem[] items) => new StatementsCode(items);

    /// <summary>
    /// Concatenates several values into a single statement sequence, normalizing each to statements (an
    /// expression becomes a terminated line). Used to compose instruction bodies, conditional arms, and the
    /// cpu-fault wrapper from smaller lowered pieces.
    /// </summary>
    public static EmittedCode Concat(params EmittedCode[] parts) =>
        new StatementsCode(parts.SelectMany(part => part.AsStatements()).ToList());

    /// <summary>Builds a statement-arm value from a single statement line.</summary>
    public static EmittedCode Line(string text) => new StatementsCode([new LineStatement(text)]);

    /// <summary>
    /// Builds a statement-arm value from a single line that diverges (transfers control away and never falls
    /// through to the following statement): a <c>return</c>, <c>goto</c>, or <c>throw</c>. Used by completion
    /// analysis (<see cref="CompletesNormally"/>) to decide whether a method body can fall off its end.
    /// </summary>
    public static EmittedCode Diverging(string text) => new StatementsCode([new LineStatement(text, Diverges: true)]);

    /// <summary>A raw string lowers to an atomic expression of unknown type.</summary>
    public static implicit operator EmittedCode(string text) => new ExpressionCode(new CSharpFragment(text));

    /// <summary>A fragment lowers directly to the expression arm.</summary>
    public static implicit operator EmittedCode(CSharpFragment fragment) => new ExpressionCode(fragment);

    /// <summary>True when this value produces no output (an empty statement sequence).</summary>
    public bool IsEmpty => this is StatementsCode { Items.Count: 0 };

    /// <summary>
    /// Whether control can fall through past this value to the statement that follows it. An expression in
    /// statement position always completes normally. A statement sequence completes normally unless its last
    /// item diverges (a <c>return</c>/<c>goto</c>/<c>throw</c>, or a conditional/switch all of whose paths
    /// diverge). The emitted-code shape is the analysis substrate: completion is computed from the structure
    /// the visitor already produced, not by re-parsing generated text.
    /// </summary>
    public bool CompletesNormally => this switch {
        ExpressionCode => true,
        StatementsCode statements => StatementsCompleteNormally(statements.Items),
        _ => throw new InvalidOperationException($"Unsupported {nameof(EmittedCode)} shape {GetType().Name}.")
    };

    private static bool StatementsCompleteNormally(IReadOnlyList<StatementItem> items) =>
        SequenceCompletesNormally(items);

    /// <summary>
    /// The single sequence-completion rule reused everywhere completion is computed (here, the switch-case
    /// break decision in <see cref="EmittedCodeRenderer"/>, and <see cref="SwitchStatement"/>): a statement
    /// sequence completes normally unless its last item diverges; an empty sequence falls through.
    /// </summary>
    public static bool SequenceCompletesNormally(IReadOnlyList<StatementItem> items) =>
        items.Count == 0 || items[^1].CompletesNormally;

    /// <summary>
    /// Returns the expression fragment, throwing when this value is a statement sequence (a generator bug:
    /// an expression was expected in the position the node was lowered for).
    /// </summary>
    public CSharpFragment AsExpression() => this switch {
        ExpressionCode expression => expression.Fragment,
        _ => throw new InvalidOperationException("Expected an expression but the AST node lowered to statements.")
    };

    /// <summary>
    /// Normalizes this value to statement items. An expression in statement position becomes a single line
    /// terminated with <c>;</c>; a statement sequence is returned as-is.
    /// </summary>
    public IReadOnlyList<StatementItem> AsStatements() => this switch {
        StatementsCode statements => statements.Items,
        ExpressionCode expression => [new LineStatement(expression.Fragment.Text + ";")],
        _ => throw new InvalidOperationException($"Unsupported {nameof(EmittedCode)} shape {GetType().Name}.")
    };
}
