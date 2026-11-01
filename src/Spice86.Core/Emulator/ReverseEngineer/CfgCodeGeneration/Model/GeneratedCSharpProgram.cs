namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

using System.Text;

internal sealed class GeneratedCSharpProgram {
    /// <summary>
    /// The generated files in concatenation order: the supplier file, the main overrides file (fields
    /// and constructor), one or more files per code segment holding the methods, then one globals class
    /// file per segment register the program accesses at constant offsets. Empty when no CFG partitions
    /// were observed.
    /// </summary>
    public required IReadOnlyList<GeneratedSourceFile> Files { get; init; }

    /// <summary>
    /// Golden form of the program: every file preceded by a <c>// ==== file: &lt;name&gt; ====</c>
    /// marker line, files separated by one empty line. A single comment line when no CFG partitions
    /// were observed.
    /// </summary>
    public string SourceText {
        get {
            if (Files.Count == 0) {
                return "// No CFG partitions were observed.\n";
            }
            StringBuilder builder = new();
            for (int i = 0; i < Files.Count; i++) {
                if (i > 0) {
                    builder.AppendLine();
                }
                builder.Append("// ==== file: ").Append(Files[i].Name).AppendLine(" ====");
                builder.Append(Files[i].Text);
            }
            return builder.ToString();
        }
    }
    /// <summary>
    /// The census of the <c>goto</c>s the printed program holds; empty for programs built without generation. A
    /// diagnostic of the generator, read by the <c>goto</c> census report of the generated-code tests.
    /// </summary>
    public GotoCensus GotoCensus { get; init; } = new();
    /// <summary>
    /// The census of the status-flag reads the printed program holds; empty for programs built without
    /// generation. A diagnostic of the generator, read by the flag-read census report of the generated-code tests.
    /// </summary>
    public FlagReadCensus FlagReadCensus { get; init; } = new();
    /// <summary>
    /// The census of the register webs of the printed program's methods; empty for programs built without
    /// generation. A diagnostic of the generator, read by the web census report of the generated-code tests.
    /// </summary>
    public WebCensus WebCensus { get; init; } = new();
    /// <summary>
    /// The constant-offset memory accesses the printed program's methods make through a segment
    /// register other than SS, collected from their final IR; empty for programs built without
    /// generation. The globals classes the generator emits cover exactly these accesses.
    /// </summary>
    public ConstantMemoryAccessTable ConstantMemoryAccesses { get; init; } = new();


    /// <summary>
    /// The data symbols with a known segmented address, exported to the Ghidra symbols file as
    /// labels: one per CS global access per code segment that accesses it.
    /// </summary>
    public IReadOnlyList<DataSymbolLabel> DataSymbolLabels { get; init; } = [];


}
