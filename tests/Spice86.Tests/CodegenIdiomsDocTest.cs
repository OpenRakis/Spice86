namespace Spice86.Tests;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Tests.AsmFixtures;

using System.Text;

using FluentAssertions;

using Xunit;

/// <summary>
/// Renders doc/codegenIdioms.md from the idiom showcase fixtures: per idiom a heading, the
/// one-sentence description, the ASM source and the program-only generated C#. The test fails when
/// the committed document differs from the rendering; regenerate it with
/// SPICE86_WRITE_CODEGEN_IDIOMS=1 dotnet test --project tests/Spice86.Tests -- --filter-class '*CodegenIdiomsDocTest*'
/// </summary>
public sealed class CodegenIdiomsDocTest {
    private static readonly (string Key, string StrictKey, string AsmFile, string Title, string Description)[] Idioms = [
        ("idiom_fail_as_untested.spec", "", "idiom_fail_as_untested.asm", "Fail-as-untested guards",
            "Unobserved conditional edges and indirect targets become guards that throw FailAsUntested instead of executing unchecked code."),
        ("idiom_typed_signature.spec", "", "idiom_typed_signature.asm", "Typed method signatures",
            "A method with register parameters and results becomes a typed C# method returning a tuple."),
        ("idiom_stack_save_restore.spec", "", "idiom_stack_save_restore.asm", "Stack save, restore and release",
            "Pushes and pops lower to typed stack operations, with discarded slots released instead of popped."),
        ("idiom_div16_named.spec", "", "idiom_div16_named.asm", "Named Div16 arguments",
            "A 32/16 division becomes a Div16 call with named divisor and dividend arguments."),
        ("idiom_dead_store.spec", "", "idiom_dead_store.asm", "Dead store elimination",
            "A store overwritten before any read disappears from the generated code."),
        ("idiom_wide_values.spec", "", "idiom_wide_values.asm", "Wide value merging",
            "Byte and word pieces written separately merge into one wide read when the program reads them as a single value."),
        ("idiom_flag_idioms.spec", "", "idiom_flag_idioms.asm", "Flag idioms",
            "A known flag folds into its branch while a data-dependent comparison keeps its tested condition."),
        ("idiom_call_sites.spec", "", "idiom_call_sites.asm", "Call sites",
            "Calls with stack arguments, discarded outputs and 32-bit operand width each get their own typed call helper."),
        ("idiom_locals_naming.spec", "", "idiom_locals_naming.asm", "Locals naming",
            "In-place index updates become role-named locals instead of register temporaries."),
        ("idiom_contracts.spec", "", "idiom_contracts.asm", "Interrupt handler contracts",
            "An observed interrupt handler carries its Inputs/Outputs contract as a doc comment."),
        ("idiom_relaxation_abandoned_stack_slots.spec.stk", "idiom_strict_abandoned_stack_slots.spec", "idiom_relaxation_stack.asm", "Relaxation: abandoned stack slots",
            "With AbandonedStackSlotsUnobserved, a private typed stack argument's bytes are treated as unobserved after the call."),
        ("idiom_jump_table_switch.spec", "", "idiom_jump_table_switch.asm", "Jump table switch",
            "An indexed jump table with every target observed at one site lowers to a switch with one case per target."),
        ("idiom_memory_accessors.spec", "", "idiom_memory_accessors.asm", "Memory accessors",
            "Absolute accesses become typed segment globals and indexed accesses become wrapping 16-bit indexers."),
        ("idiom_stack_frames.spec", "", "idiom_stack_frames.asm", "Stack frames",
            "A bp frame with a mov sp,bp epilogue becomes a typed frame struct with named slots."),
        ("idiom_fault_dispatch.spec", "", "idiom_fault_dispatch.asm", "Fault dispatch",
            "A CPU fault with an observed handler becomes a try/catch that dispatches the fault to the handler.")
    ];

    [Fact]
    public void CommittedDocMatchesRendering() {
        string rendered = Render();
        string path = DocPath();
        if (Environment.GetEnvironmentVariable("SPICE86_WRITE_CODEGEN_IDIOMS") == "1") {
            File.WriteAllText(path, rendered);
            return;
        }
        string committed = File.Exists(path) ? File.ReadAllText(path) : "";
        committed.Should().Be(rendered,
            "doc/codegenIdioms.md differs from the rendering; regenerate it with: "
            + "SPICE86_WRITE_CODEGEN_IDIOMS=1 dotnet test --project tests/Spice86.Tests -- --filter-class '*CodegenIdiomsDocTest*'");
    }

    private static string Render() {
        using StringWriter writer = new();
        writer.WriteLine("# Generated code idioms");
        writer.WriteLine();
        writer.WriteLine("Each section shows one small ASM fixture and the program-only C# the code generator");
        writer.WriteLine("produces for it. Regenerate with `SPICE86_WRITE_CODEGEN_IDIOMS=1 dotnet test --project tests/Spice86.Tests -- --filter-class '*CodegenIdiomsDocTest*'`.");
        foreach ((string key, string strictKey, string asmFile, string title, string description) in Idioms) {
            writer.WriteLine();
            writer.WriteLine($"## {title}");
            writer.WriteLine();
            writer.WriteLine(description);
            writer.WriteLine();
            writer.WriteLine("```asm");
            writer.WriteLine(ReadAsm(asmFile).TrimEnd());
            writer.WriteLine("```");
            if (strictKey.Length > 0) {
                writer.WriteLine();
                writer.WriteLine("Strict build:");
                writer.WriteLine();
                WriteCSharpBlock(writer, strictKey);
                writer.WriteLine();
                writer.WriteLine("Relaxed build:");
            }
            writer.WriteLine();
            WriteCSharpBlock(writer, key);
        }
        return writer.ToString();
    }

    /// <summary>Removes the <c>// Relaxations: X</c> header line from the program text and returns
    /// it as the prose <c>X</c> part, or null when the text holds no such line.</summary>
    private static (string Text, string? Relaxations) ExtractRelaxations(string programText) {
        const string prefix = "// Relaxations: ";
        string[] lines = programText.Split('\n');
        for (int i = 0; i < lines.Length; i++) {
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal)) {
                string relaxations = lines[i][prefix.Length..].TrimEnd();
                return (string.Join('\n', lines.Where((_, index) => index != i)), relaxations);
            }
        }
        return (programText, null);
    }

    private static void WriteCSharpBlock(StringWriter writer, string key) {
        (string text, string? relaxations) = ExtractRelaxations(ProgramOnly(key));
        if (relaxations is not null) {
            writer.WriteLine($"Relaxations: {relaxations}.");
            writer.WriteLine();
        }
        writer.WriteLine("```csharp");
        writer.WriteLine(text.TrimEnd());
        writer.WriteLine("```");
    }

    private static string ProgramOnly(string key) {
        GeneratedScenarioCase scenarioCase = GeneratedScenarioCatalog.Case(key);
        GeneratedCSharpProgram program = new GeneratedCodeMachineTestRunner().TestGeneratedCode(
            scenarioCase.DiscoveryScenario, scenarioCase.ExecutionScenarios);
        return StripNoise(GeneratedCodeChecks.ProgramOnlyText(program));
    }

    /// <summary>Drops the lines the document does not show: the supplier class, the <c>using</c>
    /// lines and the <c>namespace</c> line. The <c>// Relaxations:</c> line of the supplier file
    /// stays.</summary>
    private static string StripNoise(string programText) {
        string supplierStart = $"public sealed class {GeneratedOverrideNames.SupplierClassName} ";
        StringBuilder builder = new();
        bool inSupplier = false;
        bool previousBlank = true;
        foreach (string line in programText.Split('\n')) {
            if (line.StartsWith(supplierStart, StringComparison.Ordinal)) {
                inSupplier = true;
            }
            if (inSupplier) {
                inSupplier = line.TrimEnd() != "}";
                continue;
            }
            bool blank = string.IsNullOrWhiteSpace(line);
            if (line.StartsWith("using ", StringComparison.Ordinal)
                || line.StartsWith("namespace ", StringComparison.Ordinal)
                || (blank && previousBlank)) {
                continue;
            }
            previousBlank = blank;
            builder.AppendLine(line);
        }
        return builder.ToString().Trim() + Environment.NewLine;
    }

    private static string ReadAsm(string asmFile) {
        return File.ReadAllText(Path.Join(MachineGoldens.SourceResourcesDirectory(), "cpuTests", "asmsrc", asmFile));
    }

    private static string DocPath() {
        string testFile = GetSourceFilePath();
        string repoRoot = Path.GetFullPath(Path.Join(Path.GetDirectoryName(testFile)!, "..", ".."));
        return Path.Join(repoRoot, "doc", "codegenIdioms.md");
    }

    private static string GetSourceFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
