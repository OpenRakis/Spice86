namespace Spice86.Tests;

/// <summary>Generated source files with a probe call before each statement, and the probed statements.</summary>
/// <param name="Sources">The instrumented file texts; the hit recorder class is appended to the last one.</param>
/// <param name="Sites">The probed statements, indexed by probe id.</param>
internal sealed record InstrumentedGeneratedSource(IReadOnlyList<string> Sources, IReadOnlyList<GeneratedCodeProbeSite> Sites);
