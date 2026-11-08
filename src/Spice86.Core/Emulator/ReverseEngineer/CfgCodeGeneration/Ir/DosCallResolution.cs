namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>What <see cref="DosCallComments.Resolve"/> decided for one <c>int 21h</c> site:
/// a <c>// DOS ...</c> comment line, or the constant string a typed service call reads at
/// <c>DS:DX</c>, printed as a checked literal argument.</summary>
internal sealed record DosCallResolution(string? Comment, string? StringLiteral);
