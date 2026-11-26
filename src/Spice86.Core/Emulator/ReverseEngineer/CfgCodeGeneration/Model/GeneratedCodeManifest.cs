namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>The statement or expression a branch probe belongs to.</summary>
public enum BranchProbeKind {
    /// <summary>The condition of an <c>if</c> or <c>else if</c>.</summary>
    If,

    /// <summary>The condition of a <c>while</c> loop.</summary>
    While,

    /// <summary>The condition of a <c>do</c> ... <c>while</c> loop.</summary>
    DoWhile,

    /// <summary>The condition of a <c>for</c> loop.</summary>
    For,

    /// <summary>The condition of a conditional expression (<c>c ? a : b</c>).</summary>
    ConditionalValue,

    /// <summary>The value of a <c>switch</c>. Reported through <c>Selector</c>, the other kinds through
    /// <c>Branch</c>.</summary>
    Switch
}

/// <summary>One branch probe of a generated program, as the generator numbered it.</summary>
/// <param name="Id">The site number, the first argument of the probe call in the source.</param>
/// <param name="Kind">The statement or expression the probe belongs to.</param>
/// <param name="Method">Name of the generated method that contains the probe.</param>
/// <param name="Address">Address of the instruction that decides the condition or dispatches on the value:
/// the conditional jump, loop, indirect jump or call, or the instruction with a condition of its own that the
/// condition was lifted from. Each operand of an <c>&amp;&amp;</c> or <c>||</c> that the generator merged from
/// several jumps has the address of its own jump. <c>null</c> for a synthetic condition that no single
/// instruction decides, such as a 32-bit comparison folded from the tests of several jumps.</param>
/// <param name="Text">The printed condition or switch value the probe wraps.</param>
internal sealed record BranchProbeSite(int Id, BranchProbeKind Kind, string Method, SegmentedAddress? Address,
    string Text);

/// <summary>
/// Describes a generated program for tools that run it: its methods, where they start, which registers their
/// parameters and results stand for, and the branch probes they contain. Written as
/// <see cref="FileName"/> next to the generated sources when the generator's manifest option is on.
/// </summary>
/// <param name="Methods">The generated methods, in generation order.</param>
public sealed record GeneratedCodeManifest(IReadOnlyList<GeneratedMethodManifest> Methods) {
    /// <summary>Name of the manifest file.</summary>
    public const string FileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Where each instruction of the program lives in the generated files, recorded while
    /// the methods are printed. Present in every manifest; probe sites stay conditional on the probes
    /// option.</summary>
    public IReadOnlyList<AddressMapEntryManifest> AddressMap { get; init; } = [];

    /// <summary>Every probe site of the program, in site number order.</summary>
    [JsonIgnore]
    public IReadOnlyList<ProbeSiteManifest> Sites =>
        Methods.SelectMany(method => method.Sites).OrderBy(site => site.Id).ToList();

    /// <summary>The manifest as indented JSON with camel case property names.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Reads a manifest written by <see cref="ToJson"/>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <exception cref="JsonException">The text is not a manifest.</exception>
    public static GeneratedCodeManifest FromJson(string json) =>
        JsonSerializer.Deserialize<GeneratedCodeManifest>(json, JsonOptions)
        ?? throw new JsonException("The manifest is empty.");
}

/// <summary>One generated method.</summary>
/// <param name="Name">Name of the method in the generated class.</param>
/// <param name="Entries">The addresses the method can be entered at. The first is the primary entry. A
/// method with more than one takes the entry offset as its <c>loadOffset</c> argument.</param>
/// <param name="Parameters">The typed parameters of the method. Empty for a method without a typed
/// signature, which reads its inputs from the CPU state.</param>
/// <param name="Outputs">The typed results of the method. Empty for a method without a typed signature.
/// </param>
/// <param name="Sites">The branch probes printed in the method, in site number order.</param>
public sealed record GeneratedMethodManifest(string Name, IReadOnlyList<MethodEntryManifest> Entries,
    IReadOnlyList<MethodValueManifest> Parameters, IReadOnlyList<MethodValueManifest> Outputs,
    IReadOnlyList<ProbeSiteManifest> Sites);

/// <summary>An address a generated method can be entered at.</summary>
/// <param name="Segment">The code segment.</param>
/// <param name="Offset">The offset in the segment.</param>
public sealed record MethodEntryManifest(ushort Segment, ushort Offset);

/// <summary>A parameter or a result of a typed method.</summary>
/// <param name="Name">Name of the parameter or tuple element in the source.</param>
/// <param name="Type">Its C# type.</param>
/// <param name="Register">The register or flag it stands for.</param>
public sealed record MethodValueManifest(string Name, string Type, string Register);

/// <summary>One branch probe.</summary>
/// <param name="Id">The site number, the first argument of the <c>Branch</c> or <c>Selector</c> call.</param>
/// <param name="Kind">The statement or expression the probe belongs to. <see cref="BranchProbeKind.Switch"/>
/// sites report through <c>Selector</c>, the others through <c>Branch</c>.</param>
/// <param name="Segment">Segment of the instruction that decides the condition or dispatches on the value, or
/// <c>null</c> for a synthetic site.</param>
/// <param name="Offset">Offset of that instruction, or <c>null</c> for a synthetic site.</param>
/// <param name="Text">The printed condition or switch value the probe wraps.</param>
public sealed record ProbeSiteManifest(int Id, BranchProbeKind Kind, ushort? Segment, ushort? Offset,
    string Text) {
    /// <summary>Whether no single instruction decides the site, so it has no address. The generator built the
    /// condition from the tests of several instructions, or from none.</summary>
    public bool Synthetic => Segment is null;
}

/// <summary>Where one instruction lives in the generated files: <see cref="FirstLine"/> and
/// <see cref="LastLine"/> are 1-based lines of <see cref="File"/>; the instruction's first line is the
/// one its ASM comment would have started at, so with comments off it is the line of the instruction's
/// first printed statement. An instruction that prints nothing is an empty range: <see cref="LastLine"/>
/// is <see cref="FirstLine"/> - 1.</summary>
/// <param name="Address">The instruction address, <c>"SSSS:OOOO"</c> hex.</param>
/// <param name="File">Name of the generated file the instruction printed into.</param>
/// <param name="Method">Name of the generated method the instruction printed into.</param>
/// <param name="FirstLine">First 1-based line of the instruction in <see cref="File"/>.</param>
/// <param name="LastLine">Last 1-based line of the instruction in <see cref="File"/>.</param>
public sealed record AddressMapEntryManifest(string Address, string File, string Method,
    int FirstLine, int LastLine);
