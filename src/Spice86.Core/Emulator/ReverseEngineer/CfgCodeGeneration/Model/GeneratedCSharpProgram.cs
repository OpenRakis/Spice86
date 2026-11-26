namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Shared.Emulator.Memory;

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
    /// The manifest of the program, or <c>null</c> when the generator's manifest option is off. It is no
    /// part of <see cref="SourceText"/>.
    /// </summary>
    public GeneratedCodeManifest? Manifest { get; init; }

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
    /// <summary>
    /// One entry per printed instruction - address, file, method and 1-based line range - the data
    /// behind <c>GeneratedAddressMap.json</c>; empty for programs built without generation.
    /// </summary>
    public IReadOnlyList<GeneratedAddressMapEntry> AddressMap { get; init; } = [];

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
    /// Every candidate method whose generated body stayed untyped, with the first rule of the
    /// typed-signature analysis that refused it; empty for programs built without generation. A
    /// diagnostic of the generator, read by the untyped census report of the generated-code tests.
    /// </summary>
    public IReadOnlyList<UntypedMethod> UntypedMethods { get; init; } = [];
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

    /// <summary>The addresses of the <c>int</c> instructions whose typed service call's frame
    /// bytes are excluded from the differential gate: frameless calls, which write no interrupt
    /// frame at all, and terminating calls, whose pushed frame's flag image can legitimately
    /// differ because the typed convention drops flag outputs. The gate excludes the six bytes
    /// such a frame occupies below the recorded caller stack pointer.</summary>
    public IReadOnlyList<SegmentedAddress> FramelessServiceCallSites { get; init; } = [];

    /// <summary>The <c>call</c> instruction address and reserved argument bytes of every
    /// <c>CallWithStackSpace</c> site the program contains: the bytes the interpreted run's
    /// argument pushes wrote and the relaxed run only reserves. The differential gate excludes
    /// exactly that many bytes above the stack pointer the interpreted run had at the call, and
    /// only under the abandoned-stack-slots relaxation. Empty otherwise.</summary>
    public IReadOnlyList<(SegmentedAddress Address, int ArgumentBytes)> StackSpaceCallSites {
        get; init;
    } = [];

    /// <summary>The <c>push</c> instruction address and pushed byte count of every same-register
    /// save pair a typed method dropped under the abandoned-stack-slots relaxation: the bytes the
    /// interpreted run's push wrote below its stack pointer. The differential gate excludes
    /// exactly that many bytes below the stack pointer the interpreted run had at the push, and
    /// only under that relaxation. Empty otherwise.</summary>
    public IReadOnlyList<(SegmentedAddress Address, int Bytes)> DroppedStackSlotSites {
        get; init;
    } = [];


}
