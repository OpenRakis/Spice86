namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>What <see cref="DosCallComments.Resolve"/> decided for one <c>int 21h</c> site:
/// either a <c>// DOS ...</c> comment line, or the constant string the typed call reads at
/// <c>DS:DX</c>, appended to the call as its last argument.</summary>
internal sealed record DosCallResolution(string? Comment, string? StringLiteral);
