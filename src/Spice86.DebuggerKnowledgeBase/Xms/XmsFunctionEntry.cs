namespace Spice86.DebuggerKnowledgeBase.Xms;

/// <summary>
/// One row in the XMS AH-to-name/description lookup. Mirrors
/// <c>DosInt21DecodingTables.FunctionEntry</c> but lives in the XMS namespace so the two
/// knowledge bases stay independent.
/// </summary>
internal sealed record XmsFunctionEntry(string Name, string Description);
