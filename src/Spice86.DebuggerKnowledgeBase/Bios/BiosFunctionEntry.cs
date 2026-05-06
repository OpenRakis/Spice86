namespace Spice86.DebuggerKnowledgeBase.Bios;

/// <summary>
/// One row in a BIOS AH→name/description lookup. Mirrors <c>DosInt21DecodingTables.FunctionEntry</c>
/// but lives in the BIOS namespace so the two knowledge bases stay independent.
/// </summary>
internal sealed record BiosFunctionEntry(string Name, string Description);
