namespace Spice86.Tests;

/// <summary>A generated source with a probe call before each statement, and the probed statements.</summary>
/// <param name="Source">The instrumented source, with the hit recorder class appended.</param>
/// <param name="Sites">The probed statements, indexed by probe id.</param>
internal sealed record InstrumentedGeneratedSource(string Source, IReadOnlyList<GeneratedCodeProbeSite> Sites);
