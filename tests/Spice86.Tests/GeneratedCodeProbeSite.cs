namespace Spice86.Tests;

/// <summary>One probed statement of a generated source.</summary>
/// <param name="Id">Index of the probe, in pre-order of the statements.</param>
/// <param name="Method">The method or constructor that holds the statement.</param>
/// <param name="Line">The 1-based line of the statement in the original source.</param>
/// <param name="Context">The construct whose statement list holds the statement (method, then, else, do, whileTrue, while, case, default, try, catch, block, other).</param>
/// <param name="Kind">The statement kind: a Roslyn syntax kind name, or failAsUntested for a <c>throw FailAsUntested(...)</c>.</param>
/// <param name="Text">The first line of the statement text.</param>
internal sealed record GeneratedCodeProbeSite(int Id, string Method, int Line, string Context, string Kind, string Text);
