namespace Spice86.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System.Reflection;

/// <summary>
/// Opt-in report of the generated statements that the generated-code runs execute. When the
/// <see cref="ReportPathVariable"/> environment variable names a file, the runner compiles an instrumented copy of the
/// generated source for the runs, in which every statement of a block or a switch section is preceded by a probe
/// call, and appends one row per statement with whether it ran. The golden comparison and the metrics use the
/// original source. When the variable is unset or empty nothing changes.
/// </summary>
internal static class GeneratedCodeProbes {
    /// <summary>Environment variable holding the absolute path of the TSV report file.</summary>
    public const string ReportPathVariable = "SPICE86_GENERATED_CODE_PROBES";

    private const string HitsClassName = "GeneratedCodeProbeHits";
    private const int MaximumTextLength = 100;

    private static readonly object ReportFileLock = new();
    private static bool _reportFileInitialized;

    /// <summary>The report path, or <c>null</c> when probing is off.</summary>
    public static string? ReportPath {
        get {
            string? path = Environment.GetEnvironmentVariable(ReportPathVariable);
            if (string.IsNullOrEmpty(path)) {
                return null;
            }
            return path;
        }
    }

    /// <summary><paramref name="source"/> with a probe before every statement of its blocks and switch sections.</summary>
    public static InstrumentedGeneratedSource Instrument(string source) {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        ProbeRewriter rewriter = new();
        SyntaxNode rewritten = rewriter.Visit(tree.GetRoot()) ?? tree.GetRoot();
        string hitsClass = $$"""

            internal static class {{HitsClassName}} {
                public static readonly bool[] Hits = new bool[{{rewriter.Sites.Count}}];
                public static void Hit(int id) => Hits[id] = true;
            }

            """;
        return new InstrumentedGeneratedSource(rewritten.ToFullString() + hitsClass, rewriter.Sites);
    }

    /// <summary>The hit flags of an instrumented program loaded in <paramref name="assembly"/>, indexed by probe id.</summary>
    public static IReadOnlyList<bool> ReadHits(Assembly assembly) {
        Type hitsType = assembly.GetTypes().Single(type => type.Name == HitsClassName);
        FieldInfo field = hitsType.GetField("Hits")
            ?? throw new InvalidOperationException($"{HitsClassName}.Hits is missing.");
        return field.GetValue(null) as bool[]
            ?? throw new InvalidOperationException($"{HitsClassName}.Hits is not a bool array.");
    }

    /// <summary>Appends one row per probed statement of <paramref name="instrumented"/> to the report file.</summary>
    public static void Append(string reportPath, string goldenKey, InstrumentedGeneratedSource instrumented, IReadOnlyList<bool> hits) {
        IEnumerable<string> rows = instrumented.Sites.Select(site => string.Join('\t',
            goldenKey, site.Id, hits[site.Id] ? 1 : 0, site.Method, site.Line, site.Context, site.Kind, site.Text));
        lock (ReportFileLock) {
            if (!_reportFileInitialized) {
                File.WriteAllText(reportPath, "goldenKey\tsite\thit\tmethod\tline\tcontext\tkind\ttext" + Environment.NewLine);
                _reportFileInitialized = true;
            }
            File.AppendAllLines(reportPath, rows);
        }
    }

    private sealed class ProbeRewriter : CSharpSyntaxRewriter {
        public List<GeneratedCodeProbeSite> Sites { get; } = [];

        public override SyntaxNode? VisitBlock(BlockSyntax node) => node.WithStatements(Probed(node.Statements));

        public override SyntaxNode? VisitSwitchSection(SwitchSectionSyntax node) => node.WithStatements(Probed(node.Statements));

        private SyntaxList<StatementSyntax> Probed(SyntaxList<StatementSyntax> statements) {
            List<StatementSyntax> probed = [];
            foreach (StatementSyntax statement in statements) {
                StatementSyntax probe = SyntaxFactory.ParseStatement($"{HitsClassName}.Hit({AddSite(statement)});");
                if (statement is LabeledStatementSyntax labeled) {
                    probed.Add(labeled.WithStatement(probe));
                    probed.Add(Rewrite(labeled.Statement));
                } else {
                    probed.Add(probe);
                    probed.Add(Rewrite(statement));
                }
            }
            return SyntaxFactory.List(probed);
        }

        private StatementSyntax Rewrite(StatementSyntax statement) => Visit(statement) as StatementSyntax ?? statement;

        private int AddSite(StatementSyntax statement) {
            int id = Sites.Count;
            StatementSyntax inner = statement;
            if (statement is LabeledStatementSyntax labeled) {
                inner = labeled.Statement;
            }
            int line = statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            Sites.Add(new GeneratedCodeProbeSite(id, MethodOf(statement), line, ContextOf(statement), KindOf(inner), TextOf(inner)));
            return id;
        }

        private static string MethodOf(StatementSyntax statement) => statement.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault() switch {
            MethodDeclarationSyntax method => method.Identifier.Text,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.Text + ".ctor",
            _ => ""
        };

        private static string ContextOf(StatementSyntax statement) => statement.Parent switch {
            SwitchSectionSyntax section when section.Labels.Any(label => label is DefaultSwitchLabelSyntax) => "default",
            SwitchSectionSyntax => "case",
            BlockSyntax block => BlockContextOf(block),
            _ => "other"
        };

        private static string BlockContextOf(BlockSyntax block) => block.Parent switch {
            BaseMethodDeclarationSyntax => "method",
            DoStatementSyntax => "do",
            WhileStatementSyntax loop when loop.Condition.IsKind(SyntaxKind.TrueLiteralExpression) => "whileTrue",
            WhileStatementSyntax => "while",
            IfStatementSyntax => "then",
            ElseClauseSyntax => "else",
            TryStatementSyntax => "try",
            CatchClauseSyntax => "catch",
            _ => "block"
        };

        private static string KindOf(StatementSyntax statement) => statement switch {
            ThrowStatementSyntax { Expression: InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "FailAsUntested" } } } => "failAsUntested",
            _ => statement.Kind().ToString()
        };

        private static string TextOf(StatementSyntax statement) {
            string firstLine = statement.ToString().Split('\n')[0].Trim().Replace('\t', ' ');
            if (firstLine.Length > MaximumTextLength) {
                return firstLine[..MaximumTextLength];
            }
            return firstLine;
        }
    }
}
