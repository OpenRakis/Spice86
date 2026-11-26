namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using System.Text;

/// <summary>
/// Asserts the check lines a scenario declares on its program-only generated code: the files the
/// scenario's program produced, without the support wiring and the segment globals classes.
/// </summary>
internal static class GeneratedCodeChecks {
    /// <summary>Checks every line against the program-only text of <paramref name="program"/>. Each
    /// failure message names the golden key, the check kind and the checked text.</summary>
    /// <param name="goldenKey">Golden key of the case the program was generated for.</param>
    /// <param name="program">The generated program.</param>
    /// <param name="checks">The check lines of the discovery scenario.</param>
    public static void Assert(string goldenKey, GeneratedCSharpProgram program,
        IReadOnlyList<GeneratedCodeCheck> checks) {
        if (checks.Count == 0) {
            return;
        }
        string programText = ProgramOnlyText(program);
        foreach (GeneratedCodeCheck check in checks) {
            switch (check.Kind) {
                case GeneratedCodeCheckKind.Contains:
                    programText.Should().Contain(check.Text,
                        $"{goldenKey}: Contains check '{check.Text}'");
                    break;
                case GeneratedCodeCheckKind.DoesNotContain:
                    programText.Should().NotContain(check.Text,
                        $"{goldenKey}: DoesNotContain check '{check.Text}'");
                    break;
                case GeneratedCodeCheckKind.MethodSignature:
                    programText.Split('\n').Select(line => line.Trim())
                        .Should().Contain(check.Text,
                            $"{goldenKey}: MethodSignature check '{check.Text}'");
                    break;
            }
        }
    }

    /// <summary>Concatenates the program files of <paramref name="program"/>, dropping the support
    /// wiring files (<c>CfgGeneratedOverrides.Support.*.cs</c>) and the segment globals classes
    /// (<c>*Globals.cs</c>).</summary>
    private static string ProgramOnlyText(GeneratedCSharpProgram program) {
        const string marker = "// ==== file: ";
        StringBuilder builder = new();
        bool keep = true;
        foreach (string line in program.SourceText.Split('\n')) {
            if (line.StartsWith(marker, StringComparison.Ordinal)) {
                keep = IsProgramFile(line[marker.Length..].TrimEnd('=', ' ').TrimEnd());
                continue;
            }
            if (keep) {
                builder.AppendLine(line);
            }
        }
        return builder.ToString();
    }

    private static bool IsProgramFile(string name) =>
        !name.StartsWith("CfgGeneratedOverrides.Support.", StringComparison.Ordinal)
        && !name.EndsWith("Globals.cs", StringComparison.Ordinal);
}
